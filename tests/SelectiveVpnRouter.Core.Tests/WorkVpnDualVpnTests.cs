using SelectiveVpnRouter.Core;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public class WorkVpnDualVpnTests
{
    [Theory]
    [InlineData(WorkVpnSessionState.Connected, true, 24, "10.1.2.3", true)]
    [InlineData(WorkVpnSessionState.WaitingForMfa, true, 24, "10.1.2.3", false)]
    [InlineData(WorkVpnSessionState.Connected, false, 24, "10.1.2.3", false)]
    [InlineData(WorkVpnSessionState.Connected, true, null, "10.1.2.3", false)]
    public void Work_ready_is_independent_from_selective(
        WorkVpnSessionState state,
        bool running,
        int? ifIndex,
        string? addr,
        bool expected)
    {
        Assert.Equal(expected, WorkVpnReadiness.IsReady(state, running, ifIndex, addr));
    }

    [Fact]
    public void Work_failure_does_not_imply_selective_not_ready()
    {
        bool selectiveReady = VpnRoutingReadiness.IsRoutingReady(
            openVpnConnected: true,
            openVpnRunning: true,
            hasVpnAdapter: true,
            hasProxyListener: true,
            hasOwnedRoutes: true,
            wfpSessionOpen: true);
        Assert.True(selectiveReady);
        Assert.False(WorkVpnReadiness.IsReady(WorkVpnSessionState.Failed, false, null, null));
    }

    [Fact]
    public void Process_ownership_only_allows_owned_pids()
    {
        Assert.True(OpenVpnProcessOwnership.MayServiceTerminate(100, selectivePid: 100, workPid: 200));
        Assert.True(OpenVpnProcessOwnership.MayServiceTerminate(200, selectivePid: 100, workPid: 200));
        Assert.False(OpenVpnProcessOwnership.MayServiceTerminate(999, selectivePid: 100, workPid: 200));
    }

    [Theory]
    [InlineData("AUTH_PENDING", true)]
    [InlineData("authentication pending", true)]
    [InlineData("Initialization Sequence Completed", false)]
    public void Mfa_pending_parser(string line, bool mfa)
    {
        Assert.Equal(mfa, OpenVpnStateParser.SuggestsWaitingForExternalMfa(line));
    }

    [Theory]
    [InlineData("10.20.0.0/16", true)]
    [InlineData("0.0.0.0/1", false)]
    public void Corporate_route_classifier(string prefix, bool corporate)
    {
        Assert.Equal(corporate, WorkVpnRouteClassification.IsLikelyCorporateSplitTunnelRoute(prefix));
    }

    [Theory]
    [InlineData("0.0.0.0/1", true)]
    [InlineData("10.0.0.0/8", false)]
    public void Full_tunnel_route_classifier(string prefix, bool full)
    {
        Assert.Equal(full, WorkVpnRouteClassification.IsFullTunnelDefaultRoute(prefix));
    }
}