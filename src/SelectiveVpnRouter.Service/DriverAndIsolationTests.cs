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
            "wfp-app-filters" => WfpAppFilters(engine),
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
            return Fail("callout-registration", Bilingual(
                "\\\\.\\SelectiveVpnCallout is not open. Install/start the driver first (manual TESTSIGNING if required).",
                "\\\\.\\SelectiveVpnCallout не открыт. Сначала установите/запустите драйвер (при необходимости вручную TESTSIGNING)."));
        }

        CalloutArmStatus a = s.Callout;
        TransparentProxyDiagnostics p = s.ProxyDiagnostics;
        string msg = Bilingual(
            $"device open, enabled={a.Enabled}, calloutId={a.CalloutId}, proxyPid={a.ProxyPid}, proxyPort={a.ProxyPort}, handles={a.OpenHandles}, attempts={a.RedirectAttempts}, applySuccess={a.RedirectApplySuccess}, applyFailures={a.RedirectApplyFailures}, lastApplyStatus=0x{a.LastRedirectApplyStatus:X8}, redirects(success)={a.Redirects}; proxy accepted={p.AcceptedConnections} ctxQueries={p.RedirectContextQueries} ctxSuccess={p.RedirectContextSuccess} ctxFailures={p.RedirectContextFailures} lastCtxErr={p.LastRedirectContextError}. Fail-open: last handle close disables redirect even if the .sys stays loaded.",
            $"устройство открыто, enabled={a.Enabled}, calloutId={a.CalloutId}, proxyPid={a.ProxyPid}, proxyPort={a.ProxyPort}, handles={a.OpenHandles}, attempts={a.RedirectAttempts}, applySuccess={a.RedirectApplySuccess}, applyFailures={a.RedirectApplyFailures}, lastApplyStatus=0x{a.LastRedirectApplyStatus:X8}, redirects(успех)={a.Redirects}; proxy принято={p.AcceptedConnections} ctxQueries={p.RedirectContextQueries} ctxSuccess={p.RedirectContextSuccess} ctxFailures={p.RedirectContextFailures} lastCtxErr={p.LastRedirectContextError}. Fail-open: закрытие последнего handle отключает redirect, даже если .sys остаётся загруженным.");
        WfpPolicyDiagnostics wfp = s.WfpPolicy;
        if (!wfp.PolicyHealthy)
        {
            msg += " / " + Bilingual(
                "WFP policy unhealthy: " + FormatWfpPolicySummary(wfp),
                "WFP policy нездорова: " + FormatWfpPolicySummary(wfp));
        }

        return a.CalloutId != 0 || a.DeviceOpen ? Pass("callout-registration", msg) : Fail("callout-registration", msg);
    }

    private static DiagnosticResult WfpAppFilters(RouterEngine engine)
    {
        WfpPolicyDiagnostics wfp = engine.Snapshot().WfpPolicy;
        string summary = FormatWfpPolicySummary(wfp);
        string msg = Bilingual(summary, summary);
        return wfp.PolicyHealthy ? Pass("wfp-app-filters", msg) : Fail("wfp-app-filters", msg);
    }

    private static async Task<DiagnosticResult> TempProbeRule(RouterEngine engine, bool apply)
    {
        string? probe = FindProbe();
        if (probe is null)
        {
            return Fail("temp-probe-rule", Bilingual(
                "SelectiveVpnRouter.Probe.exe not next to the service.",
                "SelectiveVpnRouter.Probe.exe не найден рядом со службой."));
        }

        AppConfiguration cfg = engine.Config;
        List<RoutingRule> rules = cfg.Rules.Where(r => r.Name != "tmp-probe-vpn").ToList();
        if (apply)
        {
            rules.Add(RoutingRule.Create(RuleType.Application, "tmp-probe-vpn", probe, RouteMode.Vpn));
        }

        engine.SaveConfig(cfg with { Rules = rules });
        await engine.RefreshPolicyAsync().ConfigureAwait(false);
        return Pass(apply ? "temp-probe-rule" : "remove-temp-rule", apply
            ? Bilingual("Temporary full-path VPN rule for Probe.exe is active.", "Временное VPN-правило по полному пути для Probe.exe активно.")
            : Bilingual("Temporary Probe VPN rule removed.", "Временное VPN-правило Probe удалено."));
    }

    private static async Task<DiagnosticResult> TransparentRouting(RouterEngine engine, CancellationToken ct)
    {
        if (FindProbe() is null)
        {
            return Fail("transparent-routing", Bilingual("Probe.exe missing.", "Probe.exe не найден."));
        }

        if (!engine.Snapshot().DriverLoaded)
        {
            return Fail("transparent-routing", Bilingual(
                "PER-PROCESS ISOLATION: FAIL — callout driver is not loaded. Application routing is not proven.",
                "ИЗОЛЯЦИЯ ПО ПРОЦЕССАМ: FAIL — callout-драйвер не загружен. Маршрутизация приложений не проверена."));
        }

        if (engine.ProxyPort is null)
        {
            return Fail("transparent-routing", Bilingual(
                "PER-PROCESS ISOLATION: FAIL — proxy is not running. Connect VPN first.",
                "ИЗОЛЯЦИЯ ПО ПРОЦЕССАМ: FAIL — прокси не запущен. Сначала подключите VPN."));
        }

        string sessionDir = Path.Combine(Path.GetTempPath(), "svr-iso-" + Guid.NewGuid().ToString("N"));
        string vpnExe;
        string directExe;
        try
        {
            vpnExe = Path.GetFullPath(ProbeCopyHelper.PrepareProbeCopy(Path.Combine(sessionDir, "vpn")));
            directExe = Path.GetFullPath(ProbeCopyHelper.PrepareProbeCopy(Path.Combine(sessionDir, "direct")));
        }
        catch (Exception ex)
        {
            ProbeCopyHelper.Cleanup(sessionDir);
            return Fail("transparent-routing", ProbeInfrastructureFailure(ex.Message));
        }

        CalloutArmStatus calloutBefore = engine.Snapshot().Callout;
        TransparentProxyDiagnostics proxyBefore = engine.Snapshot().ProxyDiagnostics;

        AppConfiguration previous = engine.Config;
        try
        {
            RoutingRule vpnRule = RoutingRule.Create(RuleType.Application, "tmp-iso-vpn", vpnExe, RouteMode.Vpn);
            engine.SaveConfig(previous with { Rules = previous.Rules.Concat([vpnRule]).ToList() });
            await engine.RefreshPolicyAsync().ConfigureAwait(false);

            WfpPolicyDiagnostics wfpPolicy = engine.WfpPolicy;
            WfpFilterInstallResult? vpnFilter = WfpPolicyHealth.FindCalloutFilter(wfpPolicy, vpnExe);
            if (!WfpPolicyHealth.IsExeFilterReady(vpnFilter))
            {
                return Fail("transparent-routing", WfpPolicyFailure(vpnExe, vpnFilter, wfpPolicy));
            }

            string url = engine.Config.Vpn.PublicIpEndpoint ?? "https://api.ipify.org";
            Task<ProbeRunResult> vpnOut = RunProbe(vpnExe, ["--http", url], ct);
            Task<ProbeRunResult> directOut = RunProbe(directExe, ["--http", url], ct);
            ProbeRunResult[] results = await Task.WhenAll(vpnOut, directOut).ConfigureAwait(false);

            if (ProbeLaunchFailure("transparent-routing", results) is DiagnosticResult infraFail)
            {
                return infraFail;
            }

            ServiceSnapshot afterSnap = engine.Snapshot();
            CalloutArmStatus calloutAfter = afterSnap.Callout;
            TransparentProxyDiagnostics proxyAfter = afterSnap.ProxyDiagnostics;

            bool calloutMatched = calloutAfter.RedirectAttempts > calloutBefore.RedirectAttempts;
            bool applyModified = calloutAfter.RedirectApplySuccess > calloutBefore.RedirectApplySuccess;
            bool proxyAccepted = proxyAfter.AcceptedConnections > proxyBefore.AcceptedConnections;
            bool redirectContextRecovered = proxyAfter.RedirectContextSuccess > proxyBefore.RedirectContextSuccess;

            bool vpnOk = results[0].Output.Contains("http OK", StringComparison.Ordinal);
            bool directOk = results[1].Output.Contains("http OK", StringComparison.Ordinal);
            string? vpnLocal = ParseLocal(results[0].Output);
            string? directLocal = ParseLocal(results[1].Output);
            bool proxyFlowObserved = engine.Proxy?.Flows.Any(f =>
                string.Equals(f.ProcessPath, vpnExe, StringComparison.OrdinalIgnoreCase) && f.WfpRedirect) == true;
            bool directViaProxy = engine.Proxy?.Flows.Any(f =>
                string.Equals(f.ProcessPath, directExe, StringComparison.OrdinalIgnoreCase) && f.WfpRedirect) == true;
            bool localsDiffer = vpnLocal is not null && directLocal is not null && vpnLocal != directLocal;

            string stagesEn =
                $"calloutMatched={calloutMatched} applyModified={applyModified} proxyAccepted={proxyAccepted} redirectContextRecovered={redirectContextRecovered} proxyFlowObserved={proxyFlowObserved}";
            string stagesRu =
                $"calloutMatched={calloutMatched} applyModified={applyModified} proxyAccepted={proxyAccepted} redirectContextRecovered={redirectContextRecovered} proxyFlowObserved={proxyFlowObserved}";
            string calloutDiagEn =
                $"callout attempts={calloutAfter.RedirectAttempts} applySuccess={calloutAfter.RedirectApplySuccess} applyFailures={calloutAfter.RedirectApplyFailures} lastApplyStatus=0x{calloutAfter.LastRedirectApplyStatus:X8}";
            string calloutDiagRu =
                $"callout attempts={calloutAfter.RedirectAttempts} applySuccess={calloutAfter.RedirectApplySuccess} applyFailures={calloutAfter.RedirectApplyFailures} lastApplyStatus=0x{calloutAfter.LastRedirectApplyStatus:X8}";
            string proxyDiagEn =
                $"proxy accepted={proxyAfter.AcceptedConnections} ctxQueries={proxyAfter.RedirectContextQueries} ctxSuccess={proxyAfter.RedirectContextSuccess} ctxFailures={proxyAfter.RedirectContextFailures} lastCtxErr={proxyAfter.LastRedirectContextError}";
            string proxyDiagRu =
                $"proxy принято={proxyAfter.AcceptedConnections} ctxQueries={proxyAfter.RedirectContextQueries} ctxSuccess={proxyAfter.RedirectContextSuccess} ctxFailures={proxyAfter.RedirectContextFailures} lastCtxErr={proxyAfter.LastRedirectContextError}";

            string detailEn =
                stagesEn + "; " + calloutDiagEn + "; " + proxyDiagEn +
                $"; VPN-probe local={vpnLocal} ok={vpnOk}; DIRECT-probe local={directLocal} ok={directOk} directViaProxy={directViaProxy}; simultaneous=yes. " +
                results[0].Output.Split('\n').FirstOrDefault() + " | " + results[1].Output.Split('\n').FirstOrDefault();
            string detailRu =
                stagesRu + "; " + calloutDiagRu + "; " + proxyDiagRu +
                $"; VPN-probe local={vpnLocal} ok={vpnOk}; DIRECT-probe local={directLocal} ok={directOk} directViaProxy={directViaProxy}; simultaneous=yes. " +
                results[0].Output.Split('\n').FirstOrDefault() + " | " + results[1].Output.Split('\n').FirstOrDefault();

            bool vpnThroughRedirect = TransparentRoutingCriteria.IsPass(new TransparentRoutingCriteria.Input(
                calloutMatched,
                applyModified,
                proxyAccepted,
                redirectContextRecovered,
                proxyFlowObserved,
                vpnOk,
                directOk,
                directViaProxy,
                localsDiffer,
                engine.Snapshot().Vpn.Connected));

            if (vpnThroughRedirect)
            {
                return Pass("transparent-routing", Bilingual(
                    "PER-PROCESS ISOLATION: PASS. " + detailEn,
                    "ИЗОЛЯЦИЯ ПО ПРОЦЕССАМ: PASS. " + detailRu));
            }

            if (!engine.Snapshot().Vpn.Connected)
            {
                return Fail("transparent-routing", Bilingual(
                    "PER-PROCESS ISOLATION: FAIL (VPN not connected, cannot prove egress). " + detailEn,
                    "ИЗОЛЯЦИЯ ПО ПРОЦЕССАМ: FAIL (VPN не подключён, нельзя проверить egress). " + detailRu));
            }

            return Fail("transparent-routing", Bilingual(
                "PER-PROCESS ISOLATION: FAIL. " + detailEn,
                "ИЗОЛЯЦИЯ ПО ПРОЦЕССАМ: FAIL. " + detailRu));
        }
        finally
        {
            engine.SaveConfig(previous);
            await engine.RefreshPolicyAsync().ConfigureAwait(false);
            ProbeCopyHelper.Cleanup(sessionDir);
        }
    }

    private static async Task<DiagnosticResult> ParentChild(RouterEngine engine, CancellationToken ct)
    {
        if (FindProbe() is null)
        {
            return Fail("parent-child", Bilingual("Probe.exe missing.", "Probe.exe не найден."));
        }

        if (!engine.Snapshot().DriverLoaded)
        {
            return Fail("parent-child", Bilingual(
                "Driver not loaded. Rule matching is unit-tested (git does not inherit Cursor), but socket-owner isolation cannot be proven without the callout.",
                "Драйвер не загружен. Сопоставление правил проверено юнит-тестами, но изоляция владельца сокета без callout не доказуема."));
        }

        string sessionDir = Path.Combine(Path.GetTempPath(), "svr-pc-" + Guid.NewGuid().ToString("N"));
        string parentExe;
        string childExe;
        try
        {
            parentExe = ProbeCopyHelper.PrepareProbeCopy(Path.Combine(sessionDir, "parent"));
            childExe = ProbeCopyHelper.PrepareProbeCopy(Path.Combine(sessionDir, "child"));
        }
        catch (Exception ex)
        {
            ProbeCopyHelper.Cleanup(sessionDir);
            return Fail("parent-child", ProbeInfrastructureFailure(ex.Message));
        }

        AppConfiguration previous = engine.Config;
        try
        {
            RoutingRule parentVpn = RoutingRule.Create(RuleType.Application, "tmp-parent-vpn", parentExe, RouteMode.Vpn);
            RoutingRule childDirect = RoutingRule.Create(RuleType.Application, "tmp-child-direct", childExe, RouteMode.Direct);
            engine.SaveConfig(previous with { Rules = previous.Rules.Concat([parentVpn, childDirect]).ToList() });
            await engine.RefreshPolicyAsync().ConfigureAwait(false);

            string url = engine.Config.Vpn.PublicIpEndpoint ?? "https://api.ipify.org";
            ProbeRunResult run = await RunProbe(parentExe, ["--spawn", childExe, "--http", url], ct).ConfigureAwait(false);
            if (ProbeLaunchFailure("parent-child", run) is DiagnosticResult infraFail)
            {
                return infraFail;
            }

            string output = run.Output;
            bool childFlowVpn = engine.Proxy?.Flows.Any(f =>
                string.Equals(f.ProcessPath, childExe, StringComparison.OrdinalIgnoreCase) && f.WfpRedirect) == true;
            bool parentFlow = engine.Proxy?.Flows.Any(f =>
                string.Equals(f.ProcessPath, parentExe, StringComparison.OrdinalIgnoreCase) && f.WfpRedirect) == true;

            if (output.Contains("http OK", StringComparison.Ordinal) && !childFlowVpn)
            {
                return Pass("parent-child", Bilingual(
                    "Child Probe remained DIRECT (no WFP redirect) even though parent executable has a VPN rule. parentRedirect=" + parentFlow + ". " + output.Split('\n')[0],
                    "Дочерний Probe остался DIRECT (без WFP redirect), хотя у родителя VPN-правило. parentRedirect=" + parentFlow + ". " + output.Split('\n')[0]));
            }

            return Fail("parent-child", Bilingual(
                "Child may have been redirected or HTTP failed. childWfp=" + childFlowVpn + " " + output,
                "Дочерний процесс мог быть перенаправлен или HTTP не удался. childWfp=" + childFlowVpn + " " + output));
        }
        finally
        {
            engine.SaveConfig(previous);
            await engine.RefreshPolicyAsync().ConfigureAwait(false);
            ProbeCopyHelper.Cleanup(sessionDir);
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

        if (FindProbe() is null || !engine.Snapshot().DriverLoaded)
        {
            return Warn("ipv6-leak", Bilingual(
                "Cannot spawn a VPN-routed Probe IPv6 connect (driver/probe missing). IPv4-only tunnel + Block/Auto policy is configured. " + note,
                "Нельзя запустить VPN-маршрутизированный Probe IPv6 (нет драйвера/probe). Настроены IPv4-only туннель + Block/Auto. " + note));
        }

        AppConfiguration previous = engine.Config;
        string sessionDir = Path.Combine(Path.GetTempPath(), "svr-v6-" + Guid.NewGuid().ToString("N"));
        string probeExe;
        try
        {
            probeExe = ProbeCopyHelper.PrepareProbeCopy(sessionDir);
        }
        catch (Exception ex)
        {
            ProbeCopyHelper.Cleanup(sessionDir);
            return Fail("ipv6-leak", ProbeInfrastructureFailure(ex.Message));
        }

        try
        {
            engine.SaveConfig(previous with
            {
                Rules = previous.Rules.Concat([RoutingRule.Create(RuleType.Application, "tmp-v6", probeExe, RouteMode.Vpn)]).ToList(),
            });
            await engine.RefreshPolicyAsync().ConfigureAwait(false);
            ProbeRunResult run = await RunProbe(probeExe, ["--tcp6", "2001:4860:4860::8888", "443"], ct).ConfigureAwait(false);
            if (ProbeLaunchFailure("ipv6-leak", run) is DiagnosticResult infraFail)
            {
                return infraFail;
            }

            string output = run.Output;
            bool failed = output.Contains("FAIL", StringComparison.OrdinalIgnoreCase);
            return failed
                ? Pass("ipv6-leak", Bilingual(
                    "VPN-routed Probe IPv6 connect did not succeed (expected with leak-safe policy). " + output.Trim(),
                    "VPN-маршрутизированный Probe IPv6 не подключился (ожидаемо при leak-safe политике). " + output.Trim()))
                : Fail("ipv6-leak", Bilingual(
                    "VPN-routed Probe IPv6 connect succeeded while tunnel has no IPv6 — possible Direct leak. " + output.Trim(),
                    "VPN-маршрутизированный Probe IPv6 подключился при туннеле без IPv6 — возможна утечка Direct. " + output.Trim()));
        }
        finally
        {
            engine.SaveConfig(previous);
            await engine.RefreshPolicyAsync().ConfigureAwait(false);
            ProbeCopyHelper.Cleanup(sessionDir);
        }
    }

    private static async Task<DiagnosticResult> ProxyLoop(RouterEngine engine, CancellationToken ct)
    {
        if (engine.ProxyPort is not int port)
        {
            return Fail("proxy-loop", Bilingual(
                "Proxy is not running. Connect VPN first (or start service with a tunnel) so the local relay exists.",
                "Прокси не запущен. Сначала подключите VPN (или запустите службу с туннелем), чтобы локальный relay существовал."));
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
            ? Pass("proxy-loop", Bilingual(
                "SOCKS connect to the proxy's own listen port was rejected. One hop only.",
                "SOCKS-подключение к собственному listen-порту прокси отклонено. Только один hop."))
            : Fail("proxy-loop", Bilingual(
                "Did not observe loop-rejected. Driver still skips proxy PID + already-loopback dest.",
                "Не наблюдался loop-rejected. Драйвер по-прежнему пропускает PID прокси + уже loopback dest."));
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

    private const string CalloutServiceName = "SelectiveVpnCallout";
    private const int ServiceRunningState = 4;

    private static DiagnosticResult Sc(string name, string action, bool confirm)
    {
        if (!confirm)
        {
            return Warn(name, "Refused without explicit confirmation.");
        }

        if (action == "start" && TryQueryServiceState(CalloutServiceName) == ServiceRunningState)
        {
            return Pass(name, "SelectiveVpnCallout уже запущен.");
        }

        (int exitCode, int? win32, _) = RunSc(action, CalloutServiceName);
        if (action == "start")
        {
            if (exitCode == 0)
            {
                return Pass(name, "SelectiveVpnCallout запущен.");
            }

            if (win32 == 1056 || TryQueryServiceState(CalloutServiceName) == ServiceRunningState)
            {
                return Pass(name, "SelectiveVpnCallout уже запущен.");
            }

            return Fail(name, DescribeScFailure("start", CalloutServiceName, exitCode, win32));
        }

        if (action == "stop")
        {
            if (exitCode == 0)
            {
                return Pass(name, "SelectiveVpnCallout остановлен.");
            }

            if (win32 == 1062)
            {
                return Pass(name, "SelectiveVpnCallout уже остановлен.");
            }

            return Fail(name, DescribeScFailure("stop", CalloutServiceName, exitCode, win32));
        }

        return exitCode == 0
            ? Pass(name, DescribeScSuccess(action, CalloutServiceName))
            : Fail(name, DescribeScFailure(action, CalloutServiceName, exitCode, win32));
    }

    private static (int ExitCode, int? Win32, string Raw) RunSc(string action, string serviceName)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "sc.exe",
            Arguments = action + " " + serviceName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        ProcessOutputEncoding.UseConsoleEncoding(psi);
        using var p = Process.Start(psi);
        if (p is null)
        {
            return (1, null, string.Empty);
        }

        string raw = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit(8000);
        return (p.ExitCode, TryParseScWin32(raw), raw);
    }

    private static int? TryQueryServiceState(string serviceName)
    {
        (int exitCode, _, string raw) = RunSc("query", serviceName);
        if (exitCode != 0)
        {
            return null;
        }

        foreach (string line in raw.Split('\n', '\r'))
        {
            int colon = line.IndexOf(':');
            if (colon < 0)
            {
                continue;
            }

            string tail = line[(colon + 1)..].Trim();
            if (int.TryParse(tail.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(), out int state))
            {
                return state;
            }
        }

        return null;
    }

    private static int? TryParseScWin32(string output)
    {
        foreach (string line in output.Split('\n', '\r'))
        {
            int colon = line.IndexOf(':');
            if (colon < 0)
            {
                continue;
            }

            string tail = line[(colon + 1)..].Trim();
            if (int.TryParse(tail.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(), out int code))
            {
                return code;
            }
        }

        return null;
    }

    private static string DescribeScSuccess(string action, string serviceName) =>
        action switch
        {
            "start" => serviceName + " запущен.",
            "stop" => serviceName + " остановлен.",
            _ => "sc " + action + " " + serviceName + ": exit=0",
        };

    private static string DescribeScFailure(string action, string serviceName, int exitCode, int? win32) =>
        "sc " + action + " " + serviceName + " failed: exit=" + exitCode + (win32 is int code ? ", win32=" + code : string.Empty) + ".";

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
        ProcessOutputEncoding.UseConsoleEncoding(psi);
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
        string? dir = ProbeCopyHelper.FindProbeSourceDirectory();
        return dir is null ? null : Path.Combine(dir, ProbeCopyHelper.ProbeExeName);
    }

    private static async Task<ProbeRunResult> RunProbe(string exe, IReadOnlyList<string> args, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? AppContext.BaseDirectory,
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
                return new ProbeRunResult { Launched = false, LaunchError = "Process.Start returned null." };
            }

            string o = await p.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
            string e = await p.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
            await p.WaitForExitAsync(ct).WaitAsync(TimeSpan.FromSeconds(25), ct).ConfigureAwait(false);
            return new ProbeRunResult { Launched = true, Output = o + e };
        }
        catch (Exception ex)
        {
            return new ProbeRunResult { Launched = false, LaunchError = ex.Message };
        }
    }

    private static DiagnosticResult? ProbeLaunchFailure(string testName, params ProbeRunResult[] runs)
    {
        foreach (ProbeRunResult run in runs)
        {
            if (!run.Launched)
            {
                return Fail(testName, ProbeInfrastructureFailure(run.LaunchError ?? "unknown error"));
            }

            if (run.LooksLikeRuntimeLaunchFailure)
            {
                return Fail(testName, ProbeInfrastructureFailure(run.Output.Trim()));
            }
        }

        return null;
    }

    private static string ProbeInfrastructureFailure(string detail) =>
        Bilingual(
            "TEST INFRASTRUCTURE FAIL / Probe launch failed: " + detail,
            "СБОЙ ИНФРАСТРУКТУРЫ ТЕСТА / Probe не запустился: " + detail);

    private static string WfpPolicyFailure(string vpnExe, WfpFilterInstallResult? filter, WfpPolicyDiagnostics policy)
    {
        string detail = filter is null
            ? $"WFP POLICY FAIL: exe={Path.GetFullPath(vpnExe)} not in installed filters. {FormatWfpPolicySummary(policy)}"
            : "WFP POLICY FAIL: " + WfpPolicyHealth.FormatFilterLine(filter);
        string ru = filter is null
            ? $"WFP POLICY FAIL: exe={Path.GetFullPath(vpnExe)} отсутствует среди установленных фильтров. {FormatWfpPolicySummary(policy)}"
            : "WFP POLICY FAIL: " + WfpPolicyHealth.FormatFilterLine(filter);
        return Bilingual(detail, ru);
    }

    private static string FormatWfpPolicySummary(WfpPolicyDiagnostics policy)
    {
        string paths = policy.RequestedPaths.Count == 0
            ? "(none)"
            : string.Join("; ", policy.RequestedPaths);
        string filters = policy.Filters.Count == 0
            ? "(none)"
            : string.Join(" | ", policy.Filters.Where(f => f.IsCalloutFilter).Select(WfpPolicyHealth.FormatFilterLine));
        return $"requestedVpnApps={policy.RequestedVpnApps} installedAppFilters={policy.InstalledAppFilters} policyHealthy={policy.PolicyHealthy} driverPresent={policy.DriverPresent} sessionOpen={policy.SessionOpen} paths=[{paths}] filters=[{filters}] lastError={policy.LastError}";
    }

    private static string Bilingual(string en, string ru) => DiagnosticText.Bilingual(en, ru);

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
