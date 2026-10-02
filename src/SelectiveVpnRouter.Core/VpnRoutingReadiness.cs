namespace SelectiveVpnRouter.Core;

public static class VpnRoutingReadiness
{
    public static bool IsRoutingReady(
        bool openVpnConnected,
        bool openVpnRunning,
        bool hasVpnAdapter,
        bool hasProxyListener,
        bool hasOwnedRoutes,
        bool wfpSessionOpen)
        => openVpnConnected
           && openVpnRunning
           && hasVpnAdapter
           && hasProxyListener
           && hasOwnedRoutes
           && wfpSessionOpen;
}

public sealed record VpnResourceHealthReport(
    bool Healthy,
    IReadOnlyList<string> Mismatches,
    string Summary);

public static class VpnResourceHealthEvaluator
{
    public static VpnResourceHealthReport Evaluate(VpnResourceHealthSnapshot snapshot)
    {
        var mismatches = new List<string>();

        if (snapshot.VpnRoutingReady && !snapshot.OpenVpnRunning)
        {
            mismatches.Add("VpnRoutingReady=true but OpenVPN process not running");
        }

        if (snapshot.VpnRoutingReady && !snapshot.OpenVpnTunnelConnected)
        {
            mismatches.Add("VpnRoutingReady=true but OpenVPN tunnel not connected");
        }

        if (snapshot.OpenVpnTunnelConnected && !snapshot.OpenVpnRunning)
        {
            mismatches.Add("OpenVPN connected flag set but process not running");
        }

        if (snapshot.VpnRoutingReady && !snapshot.HasVpnAdapter)
        {
            mismatches.Add("VpnRoutingReady=true but VPN adapter missing");
        }

        if (snapshot.VpnRoutingReady && !snapshot.HasProxyListener)
        {
            mismatches.Add("VpnRoutingReady=true but transparent proxy listener missing");
        }

        if (snapshot.HasProxyListener && !snapshot.VpnRoutingReady && snapshot.OpenVpnTunnelConnected)
        {
            mismatches.Add("OpenVPN tunnel connected but proxy/listener not ready (partial connect?)");
        }

        if (snapshot.HasOwnedTransportRoute && !snapshot.OpenVpnRunning)
        {
            mismatches.Add("owned transport route present but OpenVPN not running");
        }

        if (!snapshot.VpnRoutingReady && snapshot.HasOwnedTransportRoute && !snapshot.OpenVpnTunnelConnected)
        {
            mismatches.Add("owned route present while disconnected (stale owned route?)");
        }

        if (snapshot.VpnRoutingReady && !snapshot.WfpSessionOpen)
        {
            mismatches.Add("VpnRoutingReady=true but WFP session not open");
        }

        if (snapshot.VpnRoutingReady && snapshot.ConfiguredAppFilterCount > 0 && !snapshot.WfpPolicyHealthy)
        {
            mismatches.Add("VpnRoutingReady=true but WFP app policy unhealthy");
        }

        if (snapshot.VpnRoutingReady && snapshot.DriverExpected && !snapshot.DriverLoaded)
        {
            mismatches.Add("VpnRoutingReady=true but callout driver not loaded");
        }

        if (!snapshot.VpnRoutingReady && snapshot.TransparentRedirectActive)
        {
            mismatches.Add("TransparentRedirectActive=true while routing not ready");
        }

        string summary = mismatches.Count == 0 ? "Healthy" : string.Join("; ", mismatches);
        return new VpnResourceHealthReport(mismatches.Count == 0, mismatches, summary);
    }
}

public sealed record VpnResourceHealthSnapshot
{
    public bool VpnRoutingReady { get; init; }
    public bool OpenVpnTunnelConnected { get; init; }
    public bool OpenVpnRunning { get; init; }
    public int? OpenVpnPid { get; init; }
    public bool HasVpnAdapter { get; init; }
    public bool HasProxyListener { get; init; }
    public int? ProxyPort { get; init; }
    public bool HasOwnedTransportRoute { get; init; }
    public int OwnedRouteCount { get; init; }
    public bool WfpSessionOpen { get; init; }
    public bool WfpPolicyHealthy { get; init; }
    public int ConfiguredAppFilterCount { get; init; }
    public bool DriverLoaded { get; init; }
    public bool DriverExpected { get; init; }
    public bool TransparentRedirectActive { get; init; }
}