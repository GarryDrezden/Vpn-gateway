using SelectiveVpnRouter.Core;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public class VpnRoutingReadinessTests
{
    [Fact]
    public void All_components_required()
    {
        Assert.False(VpnRoutingReadiness.IsRoutingReady(false, true, true, true, true, true));
        Assert.False(VpnRoutingReadiness.IsRoutingReady(true, false, true, true, true, true));
        Assert.False(VpnRoutingReadiness.IsRoutingReady(true, true, false, true, true, true));
        Assert.False(VpnRoutingReadiness.IsRoutingReady(true, true, true, false, true, true));
        Assert.False(VpnRoutingReadiness.IsRoutingReady(true, true, true, true, false, true));
        Assert.False(VpnRoutingReadiness.IsRoutingReady(true, true, true, true, true, false));
        Assert.True(VpnRoutingReadiness.IsRoutingReady(true, true, true, true, true, true));
    }
}