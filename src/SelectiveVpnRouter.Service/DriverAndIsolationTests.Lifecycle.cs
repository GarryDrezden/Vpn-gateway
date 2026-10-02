using SelectiveVpnRouter.Core;
using SelectiveVpnRouter.Network;

namespace SelectiveVpnRouter.Service;

internal static partial class DriverAndIsolationTests
{
    private static DiagnosticResult RunVpnResourceHealth(RouterEngine engine)
    {
        const string name = "vpn-resource-health";
        ServiceSnapshot snap = engine.Snapshot();
        VpnResourceHealthSnapshot input = ToResourceHealthSnapshot(engine, snap);
        VpnResourceHealthReport report = VpnResourceHealthEvaluator.Evaluate(input);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("healthy=" + report.Healthy);
        sb.AppendLine("summary=" + report.Summary);
        sb.AppendLine("vpnRoutingReady=" + snap.VpnRoutingReady);
        sb.AppendLine("openVpnRunning=" + snap.Vpn.Running);
        sb.AppendLine("openVpnTunnelConnected=" + snap.Vpn.Connected);
        sb.AppendLine("openVpnPid=" + snap.Vpn.Pid);
        sb.AppendLine("proxyPort=" + snap.ProxyPort);
        sb.AppendLine("ownedRouteCount=" + snap.OwnedRoutes.Count);
        sb.AppendLine("driverLoaded=" + snap.DriverLoaded);
        sb.AppendLine("wfpPolicyHealthy=" + snap.WfpPolicy.PolicyHealthy);
        sb.AppendLine("installedAppFilters=" + snap.WfpPolicy.InstalledAppFilters);
        sb.AppendLine("transparentRedirectActive=" + snap.TransparentRedirectActive);

        return report.Healthy
            ? Pass(name, Bilingual("Resource health OK.", "Resursy OK.") + "\n" + sb)
            : Fail(name, Bilingual("Resource health mismatch.", "Nesoglasovanost resursov.") + "\n" + sb);
    }

    private static DiagnosticResult RunVpnLifecycleCleanupCheck(RouterEngine engine)
    {
        const string name = "vpn-lifecycle-cleanup-check";
        ServiceSnapshot snap = engine.Snapshot();
        if (snap.VpnRoutingReady)
        {
            return Pass(name, Bilingual(
                "Skipped detailed disconnected cleanup while routing is active (connect first, then disconnect, then re-run).",
                "Propushcheno: marshrutizaciya aktivna.") + "\nvpnRoutingReady=true");
        }

        var problems = new List<string>();
        if (snap.Vpn.Running)
        {
            problems.Add("openvpn still running pid=" + snap.Vpn.Pid);
        }

        if (snap.ProxyPort is not null)
        {
            problems.Add("proxyPort still set=" + snap.ProxyPort);
        }

        if (snap.OwnedRoutes.Count > 0)
        {
            problems.Add("ownedRoutes=" + snap.OwnedRoutes.Count);
        }

        if (snap.TransparentRedirectActive)
        {
            problems.Add("transparentRedirectActive=true");
        }

        string body = "vpnRoutingReady=false openVpnRunning=" + snap.Vpn.Running
            + " proxyPort=" + (snap.ProxyPort?.ToString() ?? "(null)")
            + " ownedRoutes=" + snap.OwnedRoutes.Count;
        return problems.Count == 0
            ? Pass(name, Bilingual("Disconnected cleanup looks clean.", "Cleanup OK.") + "\n" + body)
            : Fail(name, Bilingual("Disconnected state has leftovers.", "Ostatki resursov.") + "\n" + body + "\n" + string.Join("; ", problems));
    }

    private static async Task<DiagnosticResult> RunVpnLifecycleReconnectStress(RouterEngine engine, bool confirm, CancellationToken ct)
    {
        const string name = "vpn-lifecycle-reconnect-stress";
        if (!confirm)
        {
            return Warn(name, Bilingual(
                "Refused without confirm=true (offline runner only).",
                "Trebuetsya confirm=true."));
        }

        string exe = engine.Config.Vpn.OpenVpnPath;
        string profile = engine.Config.Vpn.ProfilePath;
        if (!File.Exists(exe) || !File.Exists(profile))
        {
            return Fail(name, Bilingual("OpenVPN exe or profile missing.", "Net openvpn/profile."));
        }

        var sb = new System.Text.StringBuilder();
        try
        {
            await engine.DisconnectAsync().ConfigureAwait(false);
            sb.AppendLine("phase=initial-disconnect ok");

            await engine.ConnectAsync(null, ct).ConfigureAwait(false);
            ServiceSnapshot afterConnect = engine.Snapshot();
            if (!afterConnect.VpnRoutingReady)
            {
                return Fail(name, "First connect did not reach VpnRoutingReady.\n" + sb + FormatSnapshot(afterConnect));
            }

            sb.AppendLine("phase=connect-1 vpnRoutingReady=true proxyPort=" + afterConnect.ProxyPort);

            await engine.DisconnectAsync().ConfigureAwait(false);
            DiagnosticResult cleanup = RunVpnLifecycleCleanupCheck(engine);
            if (cleanup.Outcome != DiagnosticOutcomes.Pass)
            {
                return Fail(name, "Cleanup after first disconnect failed.\n" + sb + cleanup.Message);
            }

            sb.AppendLine("phase=disconnect-1 cleanup=pass");

            await engine.ConnectAsync(null, ct).ConfigureAwait(false);
            ServiceSnapshot afterReconnect = engine.Snapshot();
            if (!afterReconnect.VpnRoutingReady)
            {
                return Fail(name, "Second connect did not reach VpnRoutingReady.\n" + sb + FormatSnapshot(afterReconnect));
            }

            sb.AppendLine("phase=connect-2 vpnRoutingReady=true");

            DiagnosticResult reconnectRoutingSmoke = await RunVpnConnectionRoutingSmoke(engine, ct).ConfigureAwait(false);
            if (reconnectRoutingSmoke.Outcome != DiagnosticOutcomes.Pass)
            {
                sb.AppendLine("phase=connect-2-routing-smoke fail");
                return Fail(name, Bilingual("Post-reconnect routing smoke failed.", "Smoke posle reconnect ne proshol.") + "\n" + sb + reconnectRoutingSmoke.Message);
            }

            sb.AppendLine("phase=connect-2-routing-smoke pass");

            await engine.DisconnectAsync().ConfigureAwait(false);
            cleanup = RunVpnLifecycleCleanupCheck(engine);
            if (cleanup.Outcome != DiagnosticOutcomes.Pass)
            {
                return Fail(name, "Cleanup after second disconnect failed.\n" + sb + cleanup.Message);
            }

            sb.AppendLine("phase=disconnect-2 cleanup=pass");
            return Pass(name, Bilingual("Reconnect stress PASS.", "Reconnect stress PASS.") + "\n" + sb);
        }
        catch (Exception ex)
        {
            try { await engine.DisconnectAsync().ConfigureAwait(false); } catch { }
            return Fail(name, Bilingual("Reconnect stress exception.", "Isklyuchenie.") + "\n" + sb + "\n---\n" + ex);
        }
    }

    private static async Task<DiagnosticResult> RunVpnConnectionRoutingSmoke(RouterEngine engine, CancellationToken ct)
    {
        const string name = "vpn-connection-routing-smoke";
        ServiceSnapshot snap = engine.Snapshot();
        if (!snap.VpnRoutingReady)
        {
            return Warn(name, Bilingual("Requires VpnRoutingReady (connect VPN Route first).", "Snachala podklyuchite VPN Route.") + "\n" + FormatSnapshot(snap));
        }

        DiagnosticResult transparent = await TransparentRouting(engine, ct).ConfigureAwait(false);
        return new DiagnosticResult
        {
            Name = name,
            Outcome = transparent.Outcome,
            Message = transparent.Message,
            Time = transparent.Time,
        };
    }

    private static VpnResourceHealthSnapshot ToResourceHealthSnapshot(RouterEngine engine, ServiceSnapshot snap)
    {
        bool driverExpected = engine.Config.Rules.Any(r => r.Enabled && r.Type == RuleType.Application && r.Mode == RouteMode.Vpn);
        return new VpnResourceHealthSnapshot
        {
            VpnRoutingReady = snap.VpnRoutingReady,
            OpenVpnTunnelConnected = snap.Vpn.Connected,
            OpenVpnRunning = snap.Vpn.Running,
            OpenVpnPid = snap.Vpn.Pid,
            HasVpnAdapter = snap.VpnAdapter is not null,
            HasProxyListener = snap.ProxyPort is not null,
            ProxyPort = snap.ProxyPort,
            HasOwnedTransportRoute = snap.OwnedTransportDefault is not null || snap.OwnedRoutes.Count > 0,
            OwnedRouteCount = snap.OwnedRoutes.Count,
            WfpSessionOpen = snap.WfpPolicy.SessionOpen,
            WfpPolicyHealthy = snap.WfpPolicy.PolicyHealthy,
            ConfiguredAppFilterCount = snap.WfpPolicy.InstalledAppFilters,
            DriverLoaded = snap.DriverLoaded,
            DriverExpected = driverExpected,
            TransparentRedirectActive = snap.TransparentRedirectActive,
        };
    }

    private static string FormatSnapshot(ServiceSnapshot snap)
        => "vpnRoutingReady=" + snap.VpnRoutingReady
           + " vpn.Connected=" + snap.Vpn.Connected
           + " vpn.Running=" + snap.Vpn.Running
           + " proxyPort=" + snap.ProxyPort
           + " ownedRoutes=" + snap.OwnedRoutes.Count;
}