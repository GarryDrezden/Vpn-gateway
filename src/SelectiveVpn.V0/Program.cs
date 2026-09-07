using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Principal;
using SelectiveVpn.V0.Network;
using SelectiveVpn.V0.OpenVpn;
using SelectiveVpn.V0.Profile;

namespace SelectiveVpn.V0;

internal static class Program
{
    private const int ExitPass = 0;
    private const int ExitUsage = 1;
    private const int ExitPartial = 2;
    private const int ExitFail = 3;
    private const int ExitInconclusive = 4;

    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        if (!CliOptions.TryParse(args, out CliOptions? options, out string? error) || options is null)
        {
            if (error is not null)
            {
                Console.WriteLine(error);
                Console.WriteLine();
            }

            CliOptions.PrintHelp();
            return string.IsNullOrEmpty(error) ? ExitPass : ExitUsage;
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Console.WriteLine();
            Console.WriteLine("Ctrl+C received — stopping only the OpenVPN process started by V0...");
            cts.Cancel();
        };

        var report = new Report();
        OpenVpnProcess? vpn = null;
        NetworkSnapshot? before = null;
        AdapterInfo? vpnAdapter = null;
        ResolvedTestTarget? testTarget = null;
        Ipv4RouteEntry? observedHostRoute = null;
        IReadOnlyList<Ipv4RouteEntry> internetWideBefore = [];

        Console.WriteLine("==================================================");
        Console.WriteLine("SELECTIVE VPN ROUTER — V0.1 CONTROLLED /32 ROUTE PROBE");
        Console.WriteLine("==================================================");
        Console.WriteLine();

        try
        {
            if (!RunPreflight(options, report))
            {
                return report.ExitCode;
            }

            if (!RunProfileSafety(options.ProfilePath, report))
            {
                return report.ExitCode;
            }

            Console.WriteLine("[3/9] Resolve TEST_IP + Direct baseline");
            testTarget = await TestTargetResolver.ResolveWorkingAsync(cts.Token).ConfigureAwait(false);
            if (testTarget is null)
            {
                report.Set("INCONCLUSIVE", "No public-IP HTTPS IPv4 destination responded before VPN.");
                return report.ExitCode;
            }

            report.TestHost = testTarget.Host;
            report.TestIp = testTarget.Ipv4.ToString();
            HttpsIpProbeResult baseline = await PublicIpProbe
                .ProbeHttpsToIpAsync(testTarget.Host, testTarget.Path, testTarget.Ipv4, cts.Token)
                .ConfigureAwait(false);
            report.DirectBeforeIp = baseline.PublicIp;
            PrintPinned("DIRECT BASELINE", testTarget, baseline);
            if (!baseline.TcpConnected || !baseline.TlsOk || baseline.PublicIp is null)
            {
                report.Set("INCONCLUSIVE", "Selected TEST_IP stopped working before VPN.");
                return report.ExitCode;
            }

            report.DirectOk = true;
            Console.WriteLine();

            Console.WriteLine("[4/9] Network before VPN");
            before = NetworkInspector.Capture();
            internetWideBefore = NetworkInspector.FindInternetWideRoutes();
            PrintSnapshot(before);
            Console.WriteLine();

            Console.WriteLine("[5/9] Starting OpenVPN");
            Console.WriteLine("  Command: openvpn --config <profile> --route-nopull --route <TEST_IP> 255.255.255.255 vpn_gateway --verb 3");
            Console.WriteLine("  TEST_IP: " + testTarget.Ipv4);
            Console.WriteLine("  --route-nopull: block pushed routes/DNS; local --route is still applied (OpenVPN 2.6 man).");
            Console.WriteLine("  V0.1 will not add 0.0.0.0/0, 0.0.0.0/1, or 128.0.0.0/1.");
            Console.WriteLine();

            vpn = new OpenVpnProcess();
            vpn.Start(
                options.OpenVpnPath,
                options.ProfilePath,
                line => Console.WriteLine("  ovpn | " + line),
                testTarget.Ipv4);
            report.OpenVpnPid = vpn.Pid;
            Console.WriteLine($"  PID: {vpn.Pid}");

            OpenVpnWaitResult wait = await vpn.WaitUntilConnectedAsync(options.Timeout, cts.Token).ConfigureAwait(false);
            switch (wait.Status)
            {
                case OpenVpnWaitStatus.Connected:
                    Console.WriteLine("  Tunnel: CONNECTED");
                    report.TunnelOk = true;
                    break;
                case OpenVpnWaitStatus.AuthInteractionRequired:
                    report.Set("INCONCLUSIVE", wait.Detail ?? "AUTH INTERACTION REQUIRED — not implemented in V0.");
                    Console.WriteLine("  " + report.Conclusion);
                    return report.ExitCode;
                case OpenVpnWaitStatus.ProcessExited:
                    report.Set("FAIL", wait.Detail ?? "OpenVPN exited before the tunnel came up.");
                    PrintRecentOpenVpnErrors(vpn);
                    return report.ExitCode;
                case OpenVpnWaitStatus.TimedOut:
                    report.Set("FAIL", wait.Detail ?? "Timed out waiting for Initialization Sequence Completed.");
                    PrintRecentOpenVpnErrors(vpn);
                    return report.ExitCode;
                default:
                    report.Set("INCONCLUSIVE", wait.Detail ?? "OpenVPN wait ended without a connection.");
                    return report.ExitCode;
            }

            Console.WriteLine();
            Console.WriteLine("[6/9] VPN adapter");
            NetworkSnapshot after = await WaitForAdapterAsync(before, vpn, cts.Token).ConfigureAwait(false);
            VpnAdapterCandidate? chosen = VpnAdapterDetector.Detect(before, after, vpn.RawLogLines, out IReadOnlyList<VpnAdapterCandidate> ranked);
            PrintCandidates(ranked);

            Console.WriteLine();
            Console.WriteLine("[7/9] Network safety check + specific /32 route");
            NetworkDiff safety = NetworkDiffs.Compare(before, after);
            PrintSafety(safety);
            report.DefaultRouteUnchanged = safety.DefaultRouteUnchanged;
            report.DnsUnchanged = safety.DnsUnchanged;

            IReadOnlyList<string> wideNotes = NetworkInspector.DiffNewInternetWideRoutes(
                internetWideBefore,
                NetworkInspector.FindInternetWideRoutes());
            foreach (string note in wideNotes)
            {
                Console.WriteLine("  " + note);
            }

            if (!safety.DefaultRouteUnchanged || !safety.DnsUnchanged || wideNotes.Count > 0)
            {
                Console.WriteLine("  Unexpected system network change — stopping OpenVPN immediately.");
                await StopVpnAsync(vpn).ConfigureAwait(false);
                vpn = null;
                report.Set(
                    "FAIL",
                    wideNotes.Count > 0
                        ? "Internet-wide route appeared (0.0.0.0/0, 0.0.0.0/1, or 128.0.0.0/1). V0.1 refuses to continue."
                        : !safety.DefaultRouteUnchanged
                            ? "Default route changed after OpenVPN start. V0.1 refuses to continue."
                            : "System DNS changed after OpenVPN start. V0.1 refuses to continue.");
                return report.ExitCode;
            }

            if (chosen is null)
            {
                report.Set(
                    "INCONCLUSIVE",
                    ranked.Count == 0
                        ? "Could not identify a VPN adapter (no scored candidates)."
                        : "VPN adapter is ambiguous — V0.1 will not pick one silently.");
                Console.WriteLine("  " + report.Conclusion);
                return report.ExitCode;
            }

            vpnAdapter = chosen.Adapter;
            PrintChosenAdapter(vpnAdapter);
            PrintIpv6Diagnostics(before, vpnAdapter);

            if (vpnAdapter.Ipv4Index is not int ipv4Index)
            {
                report.Set("PARTIAL", "Tunnel is up and Direct routing is unchanged, but the VPN adapter has no IPv4 interface index.");
                return report.ExitCode;
            }

            observedHostRoute = await WaitForHostRouteAsync(testTarget.Ipv4, ipv4Index, cts.Token).ConfigureAwait(false);
            PrintSpecificRoute(testTarget.Ipv4, observedHostRoute);
            if (observedHostRoute is null || observedHostRoute.InterfaceIndex != ipv4Index)
            {
                PrintRouteRelatedLog(vpn);
                report.ControlledRouteFound = false;
                report.Set(
                    "PARTIAL",
                    LooksLikeMissingGateway(vpn)
                        ? "VPN tunnel is up, but OpenVPN could not create the controlled /32 route using vpn_gateway."
                        : "OpenVPN tunnel is up but controlled /32 route was not created.");
                return report.ExitCode;
            }

            report.ControlledRouteFound = true;
            Console.WriteLine();
            Console.WriteLine("[8/9] Direct control while VPN up");
            ResolvedTestTarget? control = await TestTargetResolver.ResolveControlAsync(testTarget, cts.Token).ConfigureAwait(false);
            if (control is null)
            {
                report.Set("INCONCLUSIVE", "No second public-IP IPv4 (different from TEST_IP) for the Direct control request.");
                return report.ExitCode;
            }

            HttpsIpProbeResult controlProbe = await PublicIpProbe
                .ProbeHttpsToIpAsync(control.Host, control.Path, control.Ipv4, cts.Token)
                .ConfigureAwait(false);
            report.DirectAfterIp = controlProbe.PublicIp;
            PrintPinned("DIRECT CONTROL WHILE VPN UP", control, controlProbe);
            if (!controlProbe.TcpConnected || controlProbe.PublicIp is null)
            {
                report.DirectOk = false;
                report.Set("FAIL", "Direct control request failed while the tunnel was up.");
                return report.ExitCode;
            }

            Console.WriteLine();
            Console.WriteLine("[9/9] VPN /32 route probe");
            Console.WriteLine("  No IP_UNICAST_IF — Windows routing table must send TEST_IP via the /32.");
            HttpsIpProbeResult vpnProbe = await PublicIpProbe
                .ProbeHttpsToIpAsync(testTarget.Host, testTarget.Path, testTarget.Ipv4, cts.Token)
                .ConfigureAwait(false);
            report.VpnPublicIp = vpnProbe.PublicIp;
            PrintVpnRouteProbe(testTarget, vpnProbe, report.DirectBeforeIp ?? controlProbe.PublicIp);
            ClassifyV01(report, vpnProbe, controlProbe.PublicIp, report.DirectBeforeIp);
            return report.ExitCode;
        }
        catch (OperationCanceledException)
        {
            if (report.Verdict == "INCONCLUSIVE" && string.IsNullOrEmpty(report.Conclusion))
            {
                report.Set("INCONCLUSIVE", "Canceled before the experiment finished.");
            }

            return report.ExitCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine("UNHANDLED ERROR: " + ex.Message);
            report.Set("FAIL", "Unhandled exception: " + ex.Message);
            return report.ExitCode;
        }
        finally
        {
            try
            {
                await CleanupAsync(vpn, before, vpnAdapter, testTarget?.Ipv4, observedHostRoute, report).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine("  WARNING: cleanup error: " + ex.Message);
            }

            PrintResult(report);
        }
    }

    private static bool RunPreflight(CliOptions options, Report report)
    {
        Console.WriteLine("[1/9] Preflight");

        if (!OperatingSystem.IsWindows())
        {
            report.Set("FAIL", "V0 requires Windows.");
            Console.WriteLine("  OS: " + RuntimeInformation.OSDescription);
            Console.WriteLine("  FAIL: not Windows.");
            return false;
        }

        Console.WriteLine("  Windows: " + RuntimeInformation.OSDescription);
        Console.WriteLine("  .NET: " + RuntimeInformation.FrameworkDescription);

        bool admin = IsAdministrator();
        Console.WriteLine("  Admin: " + (admin ? "YES" : "NO"));
        if (!admin)
        {
            report.Set("FAIL", "Administrator rights are required to start OpenVPN. V0 will not self-elevate.");
            Console.WriteLine("  Open PowerShell as Administrator and run V0 again. See docs/HOW_TO_RUN.md.");
            return false;
        }

        if (Environment.OSVersion.Version.Build is > 0 and < 22000)
        {
            Console.WriteLine("  WARNING: target is Windows 11 (build 22000+). Continuing anyway.");
        }

        Console.WriteLine("  OpenVPN path: " + options.OpenVpnPath);
        if (!File.Exists(options.OpenVpnPath))
        {
            report.Set("FAIL", "openvpn.exe not found.");
            return false;
        }

        try
        {
            OpenVpnVersionResult version = OpenVpnProcess.ReadVersion(options.OpenVpnPath);
            if (!version.Ok)
            {
                if (version.ExitCode != 0)
                {
                    Console.WriteLine("  openvpn.exe --version exit code: " + version.ExitCode);
                    if (!string.IsNullOrWhiteSpace(version.SafeStdErr))
                    {
                        Console.WriteLine("  stderr: " + version.SafeStdErr);
                    }
                }

                report.Set("FAIL", "Cannot execute openvpn.exe --version: " + (version.Error ?? "unknown error"));
                Console.WriteLine("  " + report.Conclusion);
                return false;
            }

            Console.WriteLine("  OpenVPN: " + version.VersionLine);
        }
        catch (Exception ex)
        {
            report.Set("FAIL", "Cannot execute openvpn.exe --version: " + ex.Message);
            Console.WriteLine("  " + report.Conclusion);
            return false;
        }

        Console.WriteLine("  Profile path: " + options.ProfilePath);
        Console.WriteLine("  Profile contents: not printed.");
        if (!File.Exists(options.ProfilePath))
        {
            report.Set("FAIL", ".ovpn profile not found.");
            return false;
        }

        Process[] others = Process.GetProcessesByName("openvpn");
        if (others.Length > 0)
        {
            Console.WriteLine($"  Note: {others.Length} openvpn.exe already running (V0 will not kill them).");
        }

        Console.WriteLine("  Timeout: " + options.Timeout.TotalSeconds.ToString("0") + "s");
        Console.WriteLine();
        return true;
    }

    private static bool RunProfileSafety(string profilePath, Report report)
    {
        Console.WriteLine("[2/9] Profile safety");
        OvpnSafetyScanner.ScanResult scan = OvpnSafetyScanner.Scan(profilePath);
        if (scan.ParseError is not null)
        {
            Console.WriteLine("  " + scan.ParseError);
            report.Set("FAIL", scan.ParseError);
            return false;
        }

        if (scan.Findings.Count > 0)
        {
            foreach (OvpnSafetyScanner.Finding finding in scan.Findings)
            {
                Console.WriteLine("  WARNING:");
                Console.WriteLine($"  Local directive \"{finding.Directive}\" found at line {finding.LineNumber}.");
                Console.WriteLine("  route-nopull protects against pushed server routes, but this directive is local.");
                Console.WriteLine("  V0 refuses to continue because it could affect global routing.");
            }

            report.Set("FAIL", "Local routing/DNS directive in the .ovpn. Profile was not modified.");
            return false;
        }

        if (scan.AuthInteractionRequired)
        {
            Console.WriteLine("  " + scan.AuthReason);
            Console.WriteLine("  AUTH INTERACTION REQUIRED — not implemented in V0.");
            report.Set("INCONCLUSIVE", "AUTH INTERACTION REQUIRED — not implemented in V0.");
            return false;
        }

        Console.WriteLine("  PASS");
        Console.WriteLine();
        return true;
    }

    private static async Task<NetworkSnapshot> WaitForAdapterAsync(
        NetworkSnapshot before,
        OpenVpnProcess vpn,
        CancellationToken cancellationToken)
    {
        NetworkSnapshot after = NetworkInspector.Capture();
        for (int i = 0; i < 16; i++)
        {
            after = NetworkInspector.Capture();
            if (VpnAdapterDetector.Detect(before, after, vpn.RawLogLines, out _) is not null)
            {
                return after;
            }

            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }

        return after;
    }

    private static async Task<Ipv4RouteEntry?> WaitForHostRouteAsync(
        IPAddress testIp,
        int vpnIfIndex,
        CancellationToken cancellationToken)
    {
        Ipv4RouteEntry? found = null;
        for (int i = 0; i < 20; i++)
        {
            found = NetworkInspector.FindHostRoute(testIp, vpnIfIndex);
            if (found is not null)
            {
                return found;
            }

            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }

        return NetworkInspector.FindHostRoute(testIp);
    }

    private static void ClassifyV01(
        Report report,
        HttpsIpProbeResult vpnProbe,
        string? controlPublicIp,
        string? directBeforeIp)
    {
        string? directIp = controlPublicIp ?? directBeforeIp;

        if (!string.IsNullOrEmpty(controlPublicIp)
            && !string.IsNullOrEmpty(vpnProbe.PublicIp)
            && string.Equals(controlPublicIp, vpnProbe.PublicIp, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(directBeforeIp)
            && !string.Equals(directBeforeIp, vpnProbe.PublicIp, StringComparison.OrdinalIgnoreCase))
        {
            report.Set(
                "FAIL",
                "Experiment affected traffic outside the controlled /32 route. Direct control egress matches the VPN probe.");
            return;
        }

        if (!vpnProbe.TcpConnected)
        {
            report.Set(
                "PARTIAL",
                "Controlled route exists but destination is not reachable through VPN. " + (vpnProbe.Error ?? "TCP failed."));
            return;
        }

        if (vpnProbe.PublicIp is null)
        {
            report.Set("PARTIAL", "TCP to TEST_IP worked but the HTTPS public-IP body was not parsed. " + (vpnProbe.Error ?? ""));
            return;
        }

        if (!string.IsNullOrEmpty(directIp)
            && string.Equals(directIp, vpnProbe.PublicIp, StringComparison.OrdinalIgnoreCase))
        {
            report.Set(
                "PARTIAL",
                "Traffic path is not proven to use VPN egress.");
            return;
        }

        report.VpnInternetOk = true;
        report.Set(
            "PASS",
            "OpenVPN can act as an additional Internet transport when Windows has an explicit destination route through the tunnel.");
    }

    private static async Task CleanupAsync(
        OpenVpnProcess? vpn,
        NetworkSnapshot? before,
        AdapterInfo? vpnAdapter,
        IPAddress? testIp,
        Ipv4RouteEntry? observedHostRoute,
        Report report)
    {
        Console.WriteLine();
        Console.WriteLine("CLEANUP");

        bool stopped = vpn is null;
        if (vpn is not null)
        {
            Console.WriteLine(vpn.Pid is int pid
                ? $"  Stopping OpenVPN PID {pid} (this child only)..."
                : "  Stopping OpenVPN child...");
            await StopVpnAsync(vpn).ConfigureAwait(false);
            stopped = true;
        }
        else
        {
            Console.WriteLine("  No OpenVPN child to stop.");
        }

        Console.WriteLine("  OpenVPN stopped: " + YesNo(stopped));

        if (before is null)
        {
            return;
        }

        await Task.Delay(1000).ConfigureAwait(false);
        NetworkSnapshot final = NetworkInspector.Capture();
        for (int i = 0; i < 20 && vpnAdapter is not null; i++)
        {
            AdapterInfo? current = final.Adapters.FirstOrDefault(a =>
                string.Equals(a.Id, vpnAdapter.Id, StringComparison.OrdinalIgnoreCase));
            if (current is null || current.Status != OperationalStatus.Up)
            {
                break;
            }

            await Task.Delay(500).ConfigureAwait(false);
            final = NetworkInspector.Capture();
        }

        bool hostGone = true;
        if (testIp is not null)
        {
            Ipv4RouteEntry? leftover = NetworkInspector.FindHostRoute(testIp);
            hostGone = leftover is null;
            if (leftover is not null)
            {
                Console.WriteLine();
                Console.WriteLine("!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!");
                Console.WriteLine("WARNING: TEST_IP/32 STILL PRESENT AFTER OPENVPN STOP");
                Console.WriteLine("!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!");
                Console.WriteLine("  " + leftover);
                if (observedHostRoute is not null
                    && leftover.InterfaceIndex == observedHostRoute.InterfaceIndex
                    && leftover.NextHop.Equals(observedHostRoute.NextHop)
                    && leftover.MatchesHost(testIp))
                {
                    if (NetworkInspector.TryDeleteExactHostRoute(observedHostRoute, out string deleteMessage))
                    {
                        Console.WriteLine("  Emergency delete of the exact V0.1 /32: " + deleteMessage);
                        leftover = NetworkInspector.FindHostRoute(testIp);
                        hostGone = leftover is null;
                    }
                    else
                    {
                        Console.WriteLine("  Did not delete blindly: " + deleteMessage);
                    }
                }
                else
                {
                    Console.WriteLine("  Not uniquely attributable to this V0.1 run — not deleting.");
                }
            }
        }

        report.RouteRemovedAfterCleanup = hostGone;
        Console.WriteLine("  TEST_IP/32 removed: " + (testIp is null ? "n/a" : YesNo(hostGone)));

        Console.WriteLine("  FINAL network snapshot:");
        PrintSnapshot(final, indent: "    ");
        NetworkDiff diff = NetworkDiffs.Compare(before, final);
        report.DefaultRouteUnchanged = report.DefaultRouteUnchanged && diff.DefaultRouteUnchanged;
        report.DnsUnchanged = report.DnsUnchanged && diff.DnsUnchanged;
        Console.WriteLine("  Default route restored/unchanged: " + YesNo(diff.DefaultRouteUnchanged));
        Console.WriteLine("  DNS restored/unchanged: " + YesNo(diff.DnsUnchanged));
        if (!diff.DefaultRouteUnchanged || !diff.DnsUnchanged)
        {
            Console.WriteLine();
            Console.WriteLine("!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!");
            Console.WriteLine("NETWORK STATE DIFF AFTER CLEANUP");
            Console.WriteLine("!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!");
            PrintSafety(diff);
        }

        DirectProbeResult afterCleanup = await PublicIpProbe.ProbeDirectAsync(CancellationToken.None).ConfigureAwait(false);
        Console.WriteLine("  Direct internet: " + (afterCleanup.Ok ? "OK" : "FAIL"));
        if (afterCleanup.Ok)
        {
            Console.WriteLine("  Direct public IP: " + afterCleanup.PublicIp);
        }
        else
        {
            Console.WriteLine("  " + afterCleanup.Error);
            report.DirectOk = false;
        }

        if (report.Verdict == "PASS")
        {
            if (!hostGone)
            {
                report.Set("PARTIAL", "PASS criteria failed at cleanup: TEST_IP/32 remained after OpenVPN stop.");
            }
            else if (!diff.DefaultRouteUnchanged || !diff.DnsUnchanged || !afterCleanup.Ok)
            {
                report.Set("FAIL", "Network was not restored after OpenVPN stop.");
            }
        }
    }

    private static async Task StopVpnAsync(OpenVpnProcess vpn)
    {
        try
        {
            await vpn.StopAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.WriteLine("  WARNING: OpenVPN stop error: " + ex.Message);
        }

        vpn.Dispose();
    }

    private static void PrintSnapshot(NetworkSnapshot snapshot, string indent = "  ")
    {
        if (snapshot.IPv4DefaultRoutes.Count == 0)
        {
            Console.WriteLine(indent + "Default: (none found)");
        }
        else
        {
            foreach (DefaultRouteEntry route in snapshot.IPv4DefaultRoutes)
            {
                Console.WriteLine(indent + "Default: " + route);
            }
        }

        var dns = snapshot.Dns
            .Where(d => d.Servers.Count > 0)
            .Select(d => $"{d.AdapterName} [{string.Join(", ", d.Servers)}]");
        string dnsLine = string.Join("; ", dns);
        Console.WriteLine(indent + "DNS: " + (string.IsNullOrEmpty(dnsLine) ? "(none)" : dnsLine));

        foreach (AdapterInfo nic in snapshot.Adapters)
        {
            string ipv4 = nic.UnicastIpv4.Count == 0 ? "-" : string.Join(",", nic.UnicastIpv4);
            string ipv6 = nic.UnicastIpv6.Count == 0 ? "-" : string.Join(",", nic.UnicastIpv6);
            Console.WriteLine(
                $"{indent}Adapter: {nic.Name} | {nic.Description} | {nic.Status} | "
                + $"GUID {nic.Id} | IPv4 idx {Fmt(nic.Ipv4Index)} ({ipv4}) | IPv6 idx {Fmt(nic.Ipv6Index)} ({ipv6})");
        }
    }

    private static void PrintSafety(NetworkDiff diff)
    {
        Console.WriteLine("  Default route unchanged: " + (diff.DefaultRouteUnchanged ? "YES" : "NO"));
        foreach (string note in diff.DefaultRouteNotes)
        {
            Console.WriteLine("    " + note);
        }

        Console.WriteLine("  DNS unchanged: " + (diff.DnsUnchanged ? "YES" : "NO"));
        foreach (string note in diff.DnsNotes)
        {
            Console.WriteLine("    " + note);
        }
    }

    private static void PrintCandidates(IReadOnlyList<VpnAdapterCandidate> ranked)
    {
        if (ranked.Count == 0)
        {
            Console.WriteLine("  Candidates: none");
            return;
        }

        Console.WriteLine("  Candidates:");
        foreach (VpnAdapterCandidate c in ranked)
        {
            Console.WriteLine($"    score {c.Score}: {c.Adapter.Name} ({c.Adapter.Description}) — {string.Join("; ", c.Reasons)}");
        }
    }

    private static void PrintChosenAdapter(AdapterInfo nic)
    {
        Console.WriteLine("  Name: " + nic.Name);
        Console.WriteLine("  Description: " + nic.Description);
        Console.WriteLine("  GUID: " + nic.Id);
        Console.WriteLine("  IPv4 index: " + Fmt(nic.Ipv4Index));
        Console.WriteLine("  IPv6 index: " + Fmt(nic.Ipv6Index));
        Console.WriteLine("  IPv4: " + (nic.UnicastIpv4.Count == 0 ? "(none)" : string.Join(", ", nic.UnicastIpv4)));
        Console.WriteLine("  IPv6: " + (nic.UnicastIpv6.Count == 0 ? "(none)" : string.Join(", ", nic.UnicastIpv6)));
    }

    private static void PrintIpv6Diagnostics(NetworkSnapshot before, AdapterInfo vpnAdapter)
    {
        bool directIpv6 = before.Adapters.Any(a =>
            a.Status == OperationalStatus.Up
            && a.UnicastIpv6.Any(ip => !ip.StartsWith("fe80:", StringComparison.OrdinalIgnoreCase)));
        bool vpnIpv6 = vpnAdapter.UnicastIpv6.Any(ip => !ip.StartsWith("fe80:", StringComparison.OrdinalIgnoreCase));
        Console.WriteLine("  IPv6 Direct (global): " + (directIpv6 ? "YES" : "NO"));
        Console.WriteLine("  IPv6 VPN (global): " + (vpnIpv6 ? "YES" : "NO"));
        if (!vpnIpv6)
        {
            Console.WriteLine("  IPv6 VPN test: SKIPPED — tunnel has no IPv6 address.");
        }
    }

    private static void PrintPinned(string title, ResolvedTestTarget target, HttpsIpProbeResult probe)
    {
        Console.WriteLine("  " + title);
        Console.WriteLine("  Host: " + target.Host);
        Console.WriteLine("  Destination IPv4: " + target.Ipv4);
        Console.WriteLine("  TCP: " + (probe.TcpConnected ? "OK" : DescribeHttpsTcp(probe)));
        Console.WriteLine("  TLS: " + (probe.TlsOk ? "OK" : "not completed"));
        Console.WriteLine("  Public IP: " + (probe.PublicIp ?? "(unavailable)"));
        Console.WriteLine("  Status: " + (probe.TcpConnected && probe.TlsOk && probe.PublicIp is not null ? "OK" : "FAIL"));
        if (probe.Error is not null)
        {
            Console.WriteLine("  " + probe.Error);
        }
    }

    private static void PrintSpecificRoute(IPAddress testIp, Ipv4RouteEntry? route)
    {
        Console.WriteLine("  SPECIFIC VPN ROUTE");
        Console.WriteLine("  Destination: " + testIp + "/32");
        if (route is null)
        {
            Console.WriteLine("  Status: NOT FOUND");
            return;
        }

        Console.WriteLine("  Interface: " + route.InterfaceName);
        Console.WriteLine("  Interface index: " + route.InterfaceIndex);
        Console.WriteLine("  Next hop: " + route.NextHop);
        Console.WriteLine("  Metric: " + route.RouteMetric);
        Console.WriteLine("  Status: FOUND");
    }

    private static void PrintVpnRouteProbe(ResolvedTestTarget target, HttpsIpProbeResult probe, string? directIp)
    {
        Console.WriteLine("  VPN /32 ROUTE PROBE");
        Console.WriteLine("  Destination: " + target.Ipv4);
        Console.WriteLine("  Route: " + target.Ipv4 + "/32 → VPN");
        Console.WriteLine("  TCP connect: " + (probe.TcpConnected ? "OK" : DescribeHttpsTcp(probe)));
        Console.WriteLine("  TLS: " + (probe.TlsOk ? "OK" : "not completed"));
        Console.WriteLine("  Public IP: " + (probe.PublicIp ?? "(unavailable)"));
        Console.WriteLine("  Direct public IP: " + (directIp ?? "(unavailable)"));
        bool differs = probe.PublicIp is not null
            && directIp is not null
            && !string.Equals(probe.PublicIp, directIp, StringComparison.OrdinalIgnoreCase);
        Console.WriteLine("  VPN public IP differs from Direct: " + YesNo(differs));
        if (probe.Error is not null)
        {
            Console.WriteLine("  " + probe.Error);
        }
    }

    private static void PrintRouteRelatedLog(OpenVpnProcess vpn)
    {
        string[] lines = vpn.RawLogLines
            .Select(OpenVpnLogRedactor.Redact)
            .Where(l =>
                l.Contains("route", StringComparison.OrdinalIgnoreCase)
                || l.Contains("vpn_gateway", StringComparison.OrdinalIgnoreCase)
                || l.Contains("ERROR", StringComparison.OrdinalIgnoreCase)
                || l.Contains("FATAL", StringComparison.OrdinalIgnoreCase))
            .TakeLast(20)
            .ToArray();
        if (lines.Length == 0)
        {
            return;
        }

        Console.WriteLine("  OpenVPN route-related log:");
        foreach (string line in lines)
        {
            Console.WriteLine("    " + line);
        }
    }

    private static bool LooksLikeMissingGateway(OpenVpnProcess vpn)
        => vpn.RawLogLines.Any(l =>
            l.Contains("vpn_gateway", StringComparison.OrdinalIgnoreCase)
            && (l.Contains("error", StringComparison.OrdinalIgnoreCase)
                || l.Contains("fail", StringComparison.OrdinalIgnoreCase)
                || l.Contains("undef", StringComparison.OrdinalIgnoreCase)
                || l.Contains("cannot", StringComparison.OrdinalIgnoreCase)
                || l.Contains("unknown", StringComparison.OrdinalIgnoreCase)));

    private static string DescribeHttpsTcp(HttpsIpProbeResult probe)
    {
        if (probe.TcpConnected)
        {
            return "OK";
        }

        return probe.WinsockError is int code ? $"FAILED WSA {code}" : "FAILED";
    }

    private static void PrintDirect(string title, DirectProbeResult result)
    {
        Console.WriteLine("  " + title);
        Console.WriteLine("  Endpoint: " + result.Endpoint);
        if (result.Ok)
        {
            Console.WriteLine("  Public IP: " + result.PublicIp);
            Console.WriteLine("  Status: OK");
        }
        else
        {
            Console.WriteLine("  Public IP: (unavailable)");
            Console.WriteLine("  Status: FAIL");
            Console.WriteLine("  " + result.Error);
        }
    }

    private static void PrintBound(BoundSocketProbeResult bound, AdapterInfo vpnAdapter)
    {
        Console.WriteLine("  VPN-BOUND SOCKET");
        Console.WriteLine("  VPN interface index: " + bound.InterfaceIndex);
        Console.WriteLine("  VPN adapter: " + vpnAdapter.Name);
        Console.WriteLine("  Destination: " + bound.Destination);
        Console.WriteLine("  IP_UNICAST_IF: " + (bound.OptionSet ? "SET" : "FAILED"));
        if (bound.OptionReadBack is int readBack)
        {
            Console.WriteLine("  IP_UNICAST_IF read-back (host order): " + readBack);
        }

        if (bound.OptionError is not null)
        {
            Console.WriteLine("  " + bound.OptionError);
        }

        Console.WriteLine("  TCP connect: " + DescribeTcp(bound));
        Console.WriteLine("  TLS: " + (bound.TlsOk ? "OK" : "not completed"));
        Console.WriteLine("  Public IP: " + (bound.PublicIp ?? "(unavailable)"));
        if (bound.Error is not null)
        {
            Console.WriteLine("  Error: " + bound.Error);
        }
    }

    private static string DescribeTcp(BoundSocketProbeResult bound)
    {
        if (bound.TcpConnected)
        {
            return "OK";
        }

        if (bound.WinsockError is int code)
        {
            return $"FAILED WSA {code}";
        }

        return "FAILED";
    }

    private static void PrintRecentOpenVpnErrors(OpenVpnProcess vpn)
    {
        string[] interesting = vpn.RawLogLines
            .Select(OpenVpnLogRedactor.Redact)
            .Where(l =>
                l.Contains("ERROR", StringComparison.OrdinalIgnoreCase)
                || l.Contains("FATAL", StringComparison.OrdinalIgnoreCase)
                || l.Contains("AUTH_FAILED", StringComparison.OrdinalIgnoreCase)
                || l.Contains("Options error", StringComparison.OrdinalIgnoreCase)
                || l.Contains("exiting", StringComparison.OrdinalIgnoreCase))
            .TakeLast(12)
            .ToArray();
        if (interesting.Length == 0)
        {
            return;
        }

        Console.WriteLine("  OpenVPN error messages:");
        foreach (string line in interesting)
        {
            Console.WriteLine("    " + line);
        }
    }

    private static void PrintResult(Report report)
    {
        Console.WriteLine();
        Console.WriteLine("==================================================");
        Console.WriteLine("RESULT: " + report.Verdict + (report.Verdict == "PASS" ? " — V0.1" : ""));
        Console.WriteLine("VPN tunnel: " + (report.TunnelOk ? "OK" : "NO"));
        Console.WriteLine("Default route unchanged: " + YesNo(report.DefaultRouteUnchanged));
        Console.WriteLine("DNS unchanged: " + YesNo(report.DnsUnchanged));
        Console.WriteLine();
        Console.WriteLine("Controlled route:");
        Console.WriteLine("  " + (report.TestIp ?? "(none)") + "/32 → VPN");
        Console.WriteLine("  Status: " + (report.ControlledRouteFound ? "OK" : "NO"));
        Console.WriteLine();
        Console.WriteLine("Direct control:");
        Console.WriteLine("  Public IP: " + (report.DirectAfterIp ?? report.DirectBeforeIp ?? "(unavailable)"));
        Console.WriteLine("  Status: " + (report.DirectOk ? "DIRECT" : "NO"));
        Console.WriteLine();
        Console.WriteLine("VPN route probe:");
        Console.WriteLine("  Public IP: " + (report.VpnPublicIp ?? "(unavailable)"));
        Console.WriteLine("  Status: " + (report.VpnInternetOk ? "VPN" : "NO"));
        Console.WriteLine();
        Console.WriteLine("Route removed after disconnect: " + YesNo(report.RouteRemovedAfterCleanup));
        Console.WriteLine("Network restored: " + YesNo(report.DirectOk && report.DefaultRouteUnchanged && report.DnsUnchanged && report.RouteRemovedAfterCleanup));

        if (!string.IsNullOrEmpty(report.Conclusion))
        {
            Console.WriteLine();
            Console.WriteLine("Conclusion:");
            Console.WriteLine(report.Conclusion);
        }

        Console.WriteLine("==================================================");
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static string Fmt(int? value) => value?.ToString() ?? "-";

    private static string YesNo(bool value) => value ? "YES" : "NO";

    private sealed class Report
    {
        public string Verdict { get; private set; } = "INCONCLUSIVE";
        public string Conclusion { get; private set; } = string.Empty;
        public bool DirectOk { get; set; }
        public bool TunnelOk { get; set; }
        public bool DefaultRouteUnchanged { get; set; } = true;
        public bool DnsUnchanged { get; set; } = true;
        public bool UnicastIfApplied { get; set; }
        public bool VpnInternetOk { get; set; }
        public bool ControlledRouteFound { get; set; }
        public bool RouteRemovedAfterCleanup { get; set; } = true;
        public string? DirectBeforeIp { get; set; }
        public string? DirectAfterIp { get; set; }
        public string? VpnPublicIp { get; set; }
        public string? TestHost { get; set; }
        public string? TestIp { get; set; }
        public int? OpenVpnPid { get; set; }

        public int ExitCode => Verdict switch
        {
            "PASS" => ExitPass,
            "PARTIAL" => ExitPartial,
            "FAIL" => ExitFail,
            _ => ExitInconclusive,
        };

        public void Set(string verdict, string conclusion)
        {
            Verdict = verdict;
            Conclusion = conclusion;
        }
    }

    private sealed class CliOptions
    {
        public required string OpenVpnPath { get; init; }
        public required string ProfilePath { get; init; }
        public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

        public static bool TryParse(string[] args, out CliOptions? options, out string? error)
        {
            options = null;
            error = null;
            if (args.Length == 0 || args.Any(a => a is "-h" or "--help" or "/?"))
            {
                if (args.Length == 0)
                {
                    error = "Missing required arguments --openvpn and --profile.";
                }

                return false;
            }

            string? openvpn = null;
            string? profile = null;
            int timeout = 30;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg is "--openvpn" && i + 1 < args.Length)
                {
                    openvpn = args[++i];
                }
                else if (arg is "--profile" && i + 1 < args.Length)
                {
                    profile = args[++i];
                }
                else if (arg is "--timeout" && i + 1 < args.Length)
                {
                    if (!int.TryParse(args[++i], out timeout) || timeout <= 0)
                    {
                        error = "--timeout must be a positive number of seconds.";
                        return false;
                    }
                }
                else
                {
                    error = "Unknown argument: " + arg;
                    return false;
                }
            }

            if (string.IsNullOrWhiteSpace(openvpn) || string.IsNullOrWhiteSpace(profile))
            {
                error = "Required: --openvpn <path> and --profile <path>.";
                return false;
            }

            options = new CliOptions
            {
                OpenVpnPath = Path.GetFullPath(openvpn),
                ProfilePath = Path.GetFullPath(profile),
                Timeout = TimeSpan.FromSeconds(timeout),
            };
            return true;
        }

        public static void PrintHelp()
        {
            Console.WriteLine("Selective VPN Router V0.1 — controlled /32 route probe");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  dotnet run --project .\\src\\SelectiveVpn.V0\\SelectiveVpn.V0.csproj -- --openvpn <openvpn.exe> --profile <file.ovpn> [--timeout 30]");
            Console.WriteLine();
            Console.WriteLine("See docs/HOW_TO_RUN.md");
        }
    }
}
