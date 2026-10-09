using SelectiveVpnRouter.Core;
using SelectiveVpnRouter.Core.BrowserRouting;
using Xunit;

namespace SelectiveVpnRouter.BrowserRouting.Tests;

public class TunnelEgressReadinessEvaluatorTests
{
    private static VpnAdapterSelection Selection(int ifIndex) =>
        new()
        {
            AdapterId = "id",
            Name = "tap0",
            Description = "tap",
            IfIndex = ifIndex,
            SelectedAddress = new Ipv4TunnelAddress("10.8.0.2", 24, Ipv4DadState.Preferred),
            Gateway = "10.8.0.1",
            SelectionReason = "test",
        };

    private static OpenVpnLiveStatus ConnectedLive() =>
        new() { Running = true, Connected = true };

    private static OpenVpnLiveStatus DisconnectedLive() =>
        new() { Running = false, Connected = false };

    [Fact]
    public void A_disconnected_vpn_readiness_false()
    {
        Assert.False(TunnelEgressReadinessEvaluator.TryGetTunnelInterfaceIndex(
            DisconnectedLive(), Selection(42), hasVpnAdapter: true, out int ifIndex));
        Assert.Equal(0, ifIndex);
    }

    [Fact]
    public void B_connected_with_valid_interface_readiness_true()
    {
        Assert.True(TunnelEgressReadinessEvaluator.TryGetTunnelInterfaceIndex(
            ConnectedLive(), Selection(42), hasVpnAdapter: true, out int ifIndex));
        Assert.Equal(42, ifIndex);
    }

    [Fact]
    public void C_connected_missing_adapter_or_selection_false()
    {
        Assert.False(TunnelEgressReadinessEvaluator.TryGetTunnelInterfaceIndex(
            ConnectedLive(), null, hasVpnAdapter: true, out _));
        Assert.False(TunnelEgressReadinessEvaluator.TryGetTunnelInterfaceIndex(
            ConnectedLive(), Selection(42), hasVpnAdapter: false, out _));
        Assert.False(TunnelEgressReadinessEvaluator.TryGetTunnelInterfaceIndex(
            ConnectedLive(), Selection(0), hasVpnAdapter: true, out _));
    }

    [Fact]
    public void D_disconnect_stopping_readiness_false()
    {
        Assert.False(TunnelEgressReadinessEvaluator.TryGetTunnelInterfaceIndex(
            new OpenVpnLiveStatus { Running = true, Connected = false },
            Selection(42),
            hasVpnAdapter: true,
            out _));
        Assert.False(TunnelEgressReadinessEvaluator.TryGetTunnelInterfaceIndex(
            new OpenVpnLiveStatus { Running = false, Connected = true },
            Selection(42),
            hasVpnAdapter: true,
            out _));
    }

    [Fact]
    public void E_reconnect_new_interface_index_not_cached()
    {
        Assert.True(TunnelEgressReadinessEvaluator.TryGetTunnelInterfaceIndex(
            ConnectedLive(), Selection(11), hasVpnAdapter: true, out int first));
        Assert.Equal(11, first);

        Assert.True(TunnelEgressReadinessEvaluator.TryGetTunnelInterfaceIndex(
            ConnectedLive(), Selection(22), hasVpnAdapter: true, out int second));
        Assert.Equal(22, second);
    }

    [Fact]
    public void F_connecting_or_faulted_not_ready()
    {
        Assert.False(TunnelEgressReadinessEvaluator.TryGetTunnelInterfaceIndex(
            new OpenVpnLiveStatus { Running = true, Connected = false },
            Selection(5),
            hasVpnAdapter: true,
            out _));
    }
}
