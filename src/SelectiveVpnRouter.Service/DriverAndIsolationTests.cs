using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using SelectiveVpnRouter.Core;
using SelectiveVpnRouter.Network;

namespace SelectiveVpnRouter.Service;

internal static class DriverAndIsolationTests
{
    public static async Task<DiagnosticResult> RunAsync(RouterEngine engine, string name, bool confirm, CancellationToken ct)
        => name switch
        {
            "wdk-build" => Wdk(),
            "driver-binary" => Binary(),
            "driver-signature" => Signature(),
            "secure-boot" => Info("secure-boot", DriverEnvironment.Capture().SecureBoot + ". App/scripts never change this."),
            "testsigning" => Info("testsigning", DriverEnvironment.Capture().TestSigning + ". App/scripts never enable TESTSIGNING."),
            "hvci" => Info("hvci", DriverEnvironment.Capture().Hvci + ". App/scripts never change Memory Integrity."),
            "driver-install" => await Script("driver-install", "install-driver.ps1", confirm, ct).ConfigureAwait(false),
            "driver-start" => Sc("driver-start", "start", confirm),
            "callout-registration" => Callout(engine),
            "temp-probe-rule" => await TempProbeRule(engine, apply: true).ConfigureAwait(false),
            "remove-temp-rule" => await TempProbeRule(engine, apply: false).ConfigureAwait(false),
            "transparent-routing" => await TransparentRouting(engine, ct).ConfigureAwait(false),
            "driver-stop" => Sc("driver-stop", "stop", confirm),
            "driver-uninstall" => await Script("driver-uninstall", "uninstall-driver.ps1", confirm, ct).ConfigureAwait(false),
            "preferred-default" => PreferredRoutes.Evaluate(engine.VpnAdapter?.Ipv4Index),
            "proxy-loop" => await ProxyLoop(engine, ct).ConfigureAwait(false),
            "parent-child" => await ParentChild(engine, ct).ConfigureAwait(false),
            "ipv6-leak" => await Ipv6Leak(engine, ct).ConfigureAwait(false),
            "kill-service" => KillService(engine, confirm),
            _ => Fail(name, "Unknown diagnostic '" + name + "'."),
        };

    private static DiagnosticResult Wdk()
    {
        DriverEnvironmentReport r = DriverEnvironment.Capture();
        return r.WdkReady
            ? Pass("wdk-build", r.WdkMessage)
            : Fail("wdk-build", r.WdkMessage + " Run scripts\\build-driver.ps1 after installing WDK. Source is not a loaded driver.");
    }

    private static DiagnosticResult Binary()
    {
        string? sys = DriverEnvironment.FindSys();
        return sys is null
            ? Fail("driver-binary", "SelectiveVpnCallout.sys not found. WDK build has not produced a binary on this machine.")
            : Pass("driver-binary", sys);
    }

    private static DiagnosticResult Signature()
    {
        DriverEnvironmentReport r = DriverEnvironment.Capture();
        if (r.SysPath is null)
        {
            return Fail("driver-signature", "No SYS to inspect.");
        }

        return Warn("driver-signature", r.SignatureStatus + " / testsigning=" + r.TestSigning + ". See docs/DRIVER_SIGNING.md. Nothing was changed.");
    }

    private static DiagnosticResult Callout(RouterEngine engine)
    {
        ServiceSnapshot s = engine.Snapshot();
        if (!s.DriverLoaded)
        {
            return Fail("callout-registration", "\\\\.\\SelectiveVpnCallout is not open. Install/start the driver first (manual TESTSIGNING if required).");
        }

        CalloutArmStatus a = s.Callout;
        string msg = $"device open, enabled={a.Enabled}, calloutId={a.CalloutId}, proxyPid={a.ProxyPid}, proxyPort={a.ProxyPort}, handles={a.OpenHandles}, redirects={a.Redirects}. Fail-open: last handle close disables redirect even if the .sys stays loaded.";
        return a.CalloutId != 0 || a.DeviceOpen ? Pass("callout-registration", msg) : Fail("callout-registration", msg);
    }

    private static async Task<DiagnosticResult> TempProbeRule(RouterEngine engine, bool apply)
    {
        string? probe = FindProbe();
        if (probe is null)
        {
            return Fail("temp-probe-rule", "SelectiveVpnRouter.Probe.exe not next to the service.");
        }

        AppConfiguration cfg = engine.Config;
        List<RoutingRule> rules = cfg.Rules.Where(r => r.Name != "tmp-probe-vpn").ToList();
        if (apply)
        {
            rules.Add(RoutingRule.Create(RuleType.Application, "tmp-probe-vpn", probe, RouteMode.Vpn));
        }

        engine.SaveConfig(cfg with { Rules = rules });
        await engine.RefreshPolicyAsync().ConfigureAwait(false);
        return Pass(apply ? "temp-probe-rule" : "remove-temp-rule",
            apply ? "Temporary full-path VPN rule for Probe.exe is active." : "Temporary Probe VPN rule removed.");
    }

    private static async Task<DiagnosticResult> TransparentRouting(RouterEngine engine, CancellationToken ct)
    {
        string? probe = FindProbe();
        if (probe is null)
        {
            return Fail("transparent-routing", "Probe.exe missing.");
        }

        if (!engine.Snapshot().DriverLoaded)
        {
            return Fail("transparent-routing", "PER-PROCESS ISOLATION: FAIL — callout driver is not loaded. Application routing is not proven.");
        }

        if (engine.ProxyPort is null)
        {
            return Fail("transparent-routing", "PER-PROCESS ISOLATION: FAIL — proxy is not running. Connect VPN first.");
        }

        string dir = Path.Combine(Path.GetTempPath(), "svr-iso-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string vpnExe = Path.Combine(dir, "SvrVpnProbe.exe");
        string directExe = Path.Combine(dir, "SvrDirectProbe.exe");
        File.Copy(probe, vpnExe, true);
        File.Copy(probe, directExe, true);

        AppConfiguration previous = engine.Config;
        try
        {
            RoutingRule vpnRule = RoutingRule.Create(RuleType.Application, "tmp-iso-vpn", vpnExe, RouteMode.Vpn);
            engine.SaveConfig(previous with { Rules = previous.Rules.Concat([vpnRule]).ToList() });
            await engine.RefreshPolicyAsync().ConfigureAwait(false);

            string url = engine.Config.Vpn.PublicIpEndpoint ?? "https://api.ipify.org";
            Task<string> vpnOut = RunProbe(vpnExe, ["--http", url], ct);
            Task<string> directOut = RunProbe(directExe, ["--http", url], ct);
            string[] results = await Task.WhenAll(vpnOut, directOut).ConfigureAwait(false);

            bool vpnOk = results[0].Contains("http OK", StringComparison.Ordinal);
            bool directOk = results[1].Contains("http OK", StringComparison.Ordinal);
            string? vpnLocal = ParseLocal(results[0]);
            string? directLocal = ParseLocal(results[1]);
            bool wfpFlow = engine.Proxy?.Flows.Any(f =>
                f.ProcessPath.Contains("SvrVpnProbe", StringComparison.OrdinalIgnoreCase) && f.WfpRedirect) == true;

            IReadOnlyList<string> vpnAddrs = engine.VpnAdapter?.Ipv4 ?? [];
            bool vpnOnTunnel = vpnLocal is not null && vpnAddrs.Any(a => vpnLocal.StartsWith(a, StringComparison.Ordinal));
            bool localsDiffer = vpnLocal is not null && directLocal is not null && vpnLocal != directLocal;

            string detail =
                $"VPN-probe local={vpnLocal} ok={vpnOk} wfpRedirectFlow={wfpFlow}; " +
                $"DIRECT-probe local={directLocal} ok={directOk}; simultaneous=yes. " +
                results[0].Split('\n').FirstOrDefault() + " | " + results[1].Split('\n').FirstOrDefault();

            if (vpnOk && directOk && (wfpFlow || vpnOnTunnel) && localsDiffer)
            {
                return Pass("transparent-routing", "PER-PROCESS ISOLATION: PASS. " + detail);
            }

            if (!engine.Snapshot().Vpn.Connected)
            {
                return Fail("transparent-routing", "PER-PROCESS ISOLATION: FAIL (VPN not connected, cannot prove egress). " + detail);
            }

            return Fail("transparent-routing", "PER-PROCESS ISOLATION: FAIL. " + detail);
        }
        finally
        {
            engine.SaveConfig(previous);
            await engine.RefreshPolicyAsync().ConfigureAwait(false);
            try { Directory.Delete(dir, true); } catch (Exception) { }
        }
    }

    private static async Task<DiagnosticResult> ParentChild(RouterEngine engine, CancellationToken ct)
    {
        string? probe = FindProbe();
        if (probe is null)
        {
            return Fail("parent-child", "Probe.exe missing.");
        }

        if (!engine.Snapshot().DriverLoaded)
        {
            return Fail("parent-child", "Driver not loaded. Rule matching is unit-tested (git does not inherit Cursor), but socket-owner isolation cannot be proven without the callout.");
        }

        string dir = Path.Combine(Path.GetTempPath(), "svr-pc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string parent = Path.Combine(dir, "SvrParentHost.exe");
        string child = Path.Combine(dir, "SvrChildProbe.exe");
        File.Copy(probe, parent, true);
        File.Copy(probe, child, true);

        AppConfiguration previous = engine.Config;
        try
        {
            RoutingRule parentVpn = RoutingRule.Create(RuleType.Application, "tmp-parent-vpn", parent, RouteMode.Vpn);
            RoutingRule childDirect = RoutingRule.Create(RuleType.Application, "tmp-child-direct", child, RouteMode.Direct);
            engine.SaveConfig(previous with { Rules = previous.Rules.Concat([parentVpn, childDirect]).ToList() });
            await engine.RefreshPolicyAsync().ConfigureAwait(false);

            string url = engine.Config.Vpn.PublicIpEndpoint ?? "https://api.ipify.org";
            string output = await RunProbe(parent, ["--spawn", child, "--http", url], ct).ConfigureAwait(false);
            bool childFlowVpn = engine.Proxy?.Flows.Any(f =>
                f.ProcessPath.Contains("SvrChildProbe", StringComparison.OrdinalIgnoreCase) && f.WfpRedirect) == true;
            bool parentFlow = engine.Proxy?.Flows.Any(f =>
                f.ProcessPath.Contains("SvrParentHost", StringComparison.OrdinalIgnoreCase) && f.WfpRedirect) == true;

            if (output.Contains("http OK", StringComparison.Ordinal) && !childFlowVpn)
            {
                return Pass("parent-child",
                    "Child Probe remained DIRECT (no WFP redirect) even though parent executable has a VPN rule. parentRedirect=" + parentFlow + ". " + output.Split('\n')[0]);
            }

            return Fail("parent-child", "Child may have been redirected or HTTP failed. childWfp=" + childFlowVpn + " " + output);
        }
        finally
        {
            engine.SaveConfig(previous);
            await engine.RefreshPolicyAsync().ConfigureAwait(false);
            try { Directory.Delete(dir, true); } catch (Exception) { }
        }
    }

    private static async Task<DiagnosticResult> Ipv6Leak(RouterEngine engine, CancellationToken ct)
    {
        bool vpn6 = engine.VpnAdapter?.Ipv6.Any(a => !a.StartsWith("fe80", StringComparison.OrdinalIgnoreCase)) == true;
        Ipv6Policy policy = engine.Config.Vpn.Ipv6Policy;
        string note = engine.Snapshot().Ipv6PolicyNote;
        if (policy is Ipv6Policy.AllowDirect)
        {
            return Warn("ipv6-leak", "Policy is Allow Direct (unsafe). " + note);
        }

        if (vpn6)
        {
            return Pass("ipv6-leak", "Tunnel has global IPv6. Leak block not required. " + note);
        }

        string? probe = FindProbe();
        if (probe is null || !engine.Snapshot().DriverLoaded)
        {
            return Warn("ipv6-leak", "Cannot spawn a VPN-routed Probe IPv6 connect (driver/probe missing). IPv4-only tunnel + Block/Auto policy is configured. " + note);
        }

        AppConfiguration previous = engine.Config;
        string copy = Path.Combine(Path.GetTempPath(), "SvrIpv6Probe.exe");
        try
        {
            File.Copy(probe, copy, true);
            engine.SaveConfig(previous with
            {
                Rules = previous.Rules.Concat([RoutingRule.Create(RuleType.Application, "tmp-v6", copy, RouteMode.Vpn)]).ToList(),
            });
            await engine.RefreshPolicyAsync().ConfigureAwait(false);
            string output = await RunProbe(copy, ["--tcp6", "2001:4860:4860::8888", "443"], ct).ConfigureAwait(false);
            bool failed = output.Contains("FAIL", StringComparison.OrdinalIgnoreCase);
            return failed
                ? Pass("ipv6-leak", "VPN-routed Probe IPv6 connect did not succeed (expected with leak-safe policy). " + output.Trim())
                : Fail("ipv6-leak", "VPN-routed Probe IPv6 connect succeeded while tunnel has no IPv6 — possible Direct leak. " + output.Trim());
        }
        finally
        {
            engine.SaveConfig(previous);
            await engine.RefreshPolicyAsync().ConfigureAwait(false);
            try { File.Delete(copy); } catch (Exception) { }
        }
    }

    private static async Task<DiagnosticResult> ProxyLoop(RouterEngine engine, CancellationToken ct)
    {
        if (engine.ProxyPort is not int port)
        {
            return Fail("proxy-loop", "Proxy is not running. Connect VPN first (or start service with a tunnel) so the local relay exists.");
        }

        using var client = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token).ConfigureAwait(false);
        using NetworkStream ns = client.GetStream();
        await ns.WriteAsync(new byte[] { 5, 1, 0 }, timeout.Token).ConfigureAwait(false);
        var hello = new byte[2];
        _ = await ns.ReadAsync(hello, timeout.Token).ConfigureAwait(false);
        byte[] req = [5, 1, 0, 1, 127, 0, 0, 1, (byte)(port >> 8), (byte)(port & 0xFF)];
        await ns.WriteAsync(req, timeout.Token).ConfigureAwait(false);
        await Task.Delay(400, ct).ConfigureAwait(false);
        bool rejected = engine.Proxy?.Flows.Any(f => f.Status == "loop-rejected") == true;
        return rejected
            ? Pass("proxy-loop", "SOCKS connect to the proxy's own listen port was rejected. One hop only.")
            : Fail("proxy-loop", "Did not observe loop-rejected. Driver still skips proxy PID + already-loopback dest.");
    }

    private static DiagnosticResult KillService(RouterEngine engine, bool confirm)
    {
        if (!confirm)
        {
            return Warn("kill-service", "Refused. Confirm in Test Center. This kills the elevated service: dynamic WFP filters drop, device handle close fail-opens the callout, OpenVPN job should die. Restart the service afterwards. Scripts never change boot security.");
        }

        engine.Log("fail-open diagnostic: killing service process on request.");
        _ = Task.Run(async () =>
        {
            await Task.Delay(300).ConfigureAwait(false);
            Environment.FailFast("SelectiveVpnRouter fail-open diagnostic kill");
        });
        return Pass("kill-service", "Service will terminate in 300ms. Expect Direct internet to keep working; restart SelectiveVpnRouter.Service afterwards.");
    }

    private static DiagnosticResult Sc(string name, string action, bool confirm)
    {
        if (!confirm)
        {
            return Warn(name, "Refused without explicit confirmation.");
        }

        var p = Process.Start(new ProcessStartInfo
        {
            FileName = "sc.exe",
            Arguments = action + " SelectiveVpnCallout",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        });
        p?.WaitForExit(8000);
        string o = (p?.StandardOutput.ReadToEnd() ?? "") + (p?.StandardError.ReadToEnd() ?? "");
        return (p?.ExitCode ?? 1) == 0 ? Pass(name, o.Trim()) : Fail(name, o.Trim());
    }

    private static async Task<DiagnosticResult> Script(string name, string file, bool confirm, CancellationToken ct)
    {
        if (!confirm)
        {
            return Warn(name, "Refused without explicit confirmation. This does not change TESTSIGNING/Secure Boot/HVCI.");
        }

        string script = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "scripts", file);
        script = Path.GetFullPath(script);
        if (!File.Exists(script))
        {
            string alt = Path.Combine(AppContext.BaseDirectory, "scripts", file);
            if (File.Exists(alt))
            {
                script = alt;
            }
        }

        if (!File.Exists(script))
        {
            return Fail(name, "Script not found: " + file + ". Run from repo scripts\\ folder.");
        }

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            ArgumentList = { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi);
        if (p is null)
        {
            return Fail(name, "Could not start PowerShell.");
        }

        string o = await p.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
        string e = await p.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
        await p.WaitForExitAsync(ct).ConfigureAwait(false);
        return p.ExitCode == 0 ? Pass(name, (o + e).Trim()) : Fail(name, (o + e).Trim());
    }

    private static string? FindProbe()
    {
        string[] paths =
        [
            Path.Combine(AppContext.BaseDirectory, "SelectiveVpnRouter.Probe.exe"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "SelectiveVpnRouter.Probe", "bin", "Release", "net10.0-windows", "SelectiveVpnRouter.Probe.exe")),
        ];
        return paths.FirstOrDefault(File.Exists);
    }

    private static async Task<string> RunProbe(string exe, IReadOnlyList<string> args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (string a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using var p = Process.Start(psi);
        if (p is null)
        {
            return "FAIL start";
        }

        string o = await p.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
        string e = await p.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
        await p.WaitForExitAsync(ct).WaitAsync(TimeSpan.FromSeconds(25), ct).ConfigureAwait(false);
        return o + e;
    }

    private static string? ParseLocal(string output)
    {
        const string key = "local ";
        int i = output.IndexOf(key, StringComparison.Ordinal);
        if (i < 0)
        {
            return null;
        }

        string rest = output[(i + key.Length)..];
        return rest.Split(' ', '\r', '\n')[0].Trim();
    }

    private static DiagnosticResult Pass(string n, string m) => new() { Name = n, Outcome = DiagnosticOutcomes.Pass, Message = m };
    private static DiagnosticResult Fail(string n, string m) => new() { Name = n, Outcome = DiagnosticOutcomes.Fail, Message = m };
    private static DiagnosticResult Warn(string n, string m) => new() { Name = n, Outcome = DiagnosticOutcomes.Warning, Message = m };
    private static DiagnosticResult Info(string n, string m) => new() { Name = n, Outcome = DiagnosticOutcomes.Pass, Message = m };
}
