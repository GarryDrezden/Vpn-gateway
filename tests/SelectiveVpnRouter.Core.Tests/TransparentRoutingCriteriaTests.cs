using SelectiveVpnRouter.Core;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public class TransparentRoutingCriteriaTests
{
    private static TransparentRoutingCriteria.Input AllTrue(bool directViaProxy = false) =>
        new(
            CalloutMatched: true,
            ApplyModified: true,
            ProxyAccepted: true,
            RedirectContextRecovered: true,
            ProxyFlowObserved: true,
            VpnOk: true,
            DirectOk: true,
            DirectViaProxy: directViaProxy,
            LocalsDiffer: true,
            VpnConnected: true);

    [Fact]
    public void Pass_requires_all_mandatory_signals()
    {
        Assert.True(TransparentRoutingCriteria.IsPass(AllTrue()));
    }

    [Theory]
    [InlineData(false, true, true, true, true, true, true, false, true, true)]
    [InlineData(true, false, true, true, true, true, true, false, true, true)]
    [InlineData(true, true, false, true, true, true, true, false, true, true)]
    [InlineData(true, true, true, false, true, true, true, false, true, true)]
    [InlineData(true, true, true, true, false, true, true, false, true, true)]
    [InlineData(true, true, true, true, true, false, true, false, true, true)]
    [InlineData(true, true, true, true, true, true, false, false, true, true)]
    [InlineData(true, true, true, true, true, true, true, true, true, true)]
    [InlineData(true, true, true, true, true, true, true, false, false, true)]
    [InlineData(true, true, true, true, true, true, true, false, true, false)]
    public void Pass_fails_when_any_required_signal_is_missing(
        bool calloutMatched,
        bool applyModified,
        bool proxyAccepted,
        bool redirectContextRecovered,
        bool proxyFlowObserved,
        bool vpnOk,
        bool directOk,
        bool directViaProxy,
        bool localsDiffer,
        bool vpnConnected)
    {
        var input = new TransparentRoutingCriteria.Input(
            calloutMatched,
            applyModified,
            proxyAccepted,
            redirectContextRecovered,
            proxyFlowObserved,
            vpnOk,
            directOk,
            directViaProxy,
            localsDiffer,
            vpnConnected);

        Assert.False(TransparentRoutingCriteria.IsPass(input));
    }
}