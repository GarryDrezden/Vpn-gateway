using SelectiveVpnRouter.Core;
using SelectiveVpnRouter.Network;

namespace SelectiveVpnRouter.Service;

internal static partial class DriverAndIsolationTests
{
    private static DiagnosticResult RunDualVpnCoexistence(RouterEngine engine)
    {
        ServiceSnapshot snap = engine.Snapshot();
        if (!FeatureFlags.WorkVpn)
        {
            return Warn("dual-vpn-coexistence", "Work VPN is experimental and hidden (feature flag off).");
        }

        if (!snap.WorkVpn.FeatureEnabled)
        {
            return Warn("dual-vpn-coexistence", "Work VPN feature disabled in settings.");
        }

        if (!snap.WorkVpn.WorkVpnReady)
        {
            return Fail("dual-vpn-coexistence", "WorkVpnReady=false. Connect Work VPN manually first (MFA). State=" + snap.WorkVpn.State);
        }

        if (!snap.VpnRoutingReady)
        {
            return Fail("dual-vpn-coexistence", "VpnRoutingReady=false. Connect selective App VPN first.");
        }

        int? workPid = snap.WorkVpn.ProcessId;
        int? selectivePid = snap.Vpn.Pid;
        if (workPid is null || selectivePid is null)
        {
            return Fail("dual-vpn-coexistence", "Missing owned process id work=" + workPid + " selective=" + selectivePid);
        }

        bool sameProcess = workPid == selectivePid;
        int? workIf = snap.WorkVpn.InterfaceIndex;
        int? selIf = snap.VpnAdapter?.Ipv4Index;
        bool sameAdapter = workIf is not null && workIf == selIf;

        var routes = RouteTable.IPv4().ToArray();
        bool workCorporate = routes.Any(r =>
            r.InterfaceIndex == workIf
            && WorkVpnRouteClassification.IsLikelyCorporateSplitTunnelRoute(r.Destination.ToString()));
        bool workDefault = routes.Any(r =>
            r.InterfaceIndex == workIf
            && WorkVpnRouteClassification.IsFullTunnelDefaultRoute(r.Destination.ToString()));
        bool selectiveProxyAlive = snap.ProxyPort is > 0;

        if (sameProcess)
        {
            return Fail("dual-vpn-coexistence", "Work and selective VPN share the same OpenVPN PID.");
        }

        if (sameAdapter)
        {
            return Fail("dual-vpn-coexistence", "Adapter identity collision workIf=" + workIf + " selectiveIf=" + selIf);
        }

        if (workDefault)
        {
            return Fail("dual-vpn-coexistence", "Work VPN adapter has full-tunnel default route (unexpected split tunnel).");
        }

        if (!workCorporate)
        {
            return Warn("dual-vpn-coexistence", "No corporate-looking routes on Work adapter; verify server push.");
        }

        if (!selectiveProxyAlive)
        {
            return Fail("dual-vpn-coexistence", "Selective transparent proxy is not running.");
        }

        string msg =
            "workPid=" + workPid
            + " selectivePid=" + selectivePid
            + " workIfIndex=" + workIf
            + " selectiveIfIndex=" + selIf
            + " sameProcess=False"
            + " sameAdapter=False"
            + " workHasCorporateRoutes=" + workCorporate
            + " workHasDefaultRoute=" + workDefault
            + " selectiveProxyAlive=" + selectiveProxyAlive;
        return Pass("dual-vpn-coexistence", msg);
    }
}