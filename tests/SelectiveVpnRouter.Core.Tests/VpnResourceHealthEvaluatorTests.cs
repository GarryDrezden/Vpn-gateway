using SelectiveVpnRouter.Core;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public class VpnResourceHealthEvaluatorTests
{
    [Fact]
    public void Healthy_when_routing_ready_and_components_align()
    {
        var snap = new VpnResourceHealthSnapshot
        {
            VpnRoutingReady = true,
            OpenVpnTunnelConnected = true,
            OpenVpnRunning = true,
            HasVpnAdapter = true,
            HasProxyListener = true,
            HasOwnedTransportRoute = true,
            WfpSessionOpen = true,
            WfpPolicyHealthy = true,
            DriverLoaded = true,
            DriverExpected = true,
        };
        VpnResourceHealthReport report = VpnResourceHealthEvaluator.Evaluate(snap);
        Assert.True(report.Healthy);
        Assert.Equal("Healthy", report.Summary);
    }

    [Fact]
    public void Healthy_when_disconnected_and_no_leftover_resources()
    {
        var snap = new VpnResourceHealthSnapshot
        {
            VpnRoutingReady = false,
            OpenVpnTunnelConnected = false,
            OpenVpnRunning = false,
            HasVpnAdapter = false,
            HasProxyListener = false,
            HasOwnedTransportRoute = false,
            OwnedRouteCount = 0,
            WfpSessionOpen = true,
            WfpPolicyHealthy = true,
            TransparentRedirectActive = false,
        };
        VpnResourceHealthReport report = VpnResourceHealthEvaluator.Evaluate(snap);
        Assert.True(report.Healthy);
    }

    [Fact]
    public void Detects_stale_owned_route_when_disconnected()
    {
        var snap = new VpnResourceHealthSnapshot
        {
            VpnRoutingReady = false,
            OpenVpnTunnelConnected = false,
            OpenVpnRunning = false,
            HasOwnedTransportRoute = true,
        };
        VpnResourceHealthReport report = VpnResourceHealthEvaluator.Evaluate(snap);
        Assert.False(report.Healthy);
        Assert.Contains(report.Mismatches, m => m.Contains("stale owned route", StringComparison.OrdinalIgnoreCase));
    }
}