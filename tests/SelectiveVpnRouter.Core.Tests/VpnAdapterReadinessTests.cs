using System.Net.NetworkInformation;
using SelectiveVpnRouter.Core;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public class VpnAdapterReadinessTests
{
    private static VpnAdapterReadinessCandidate Cand(
        int ifIndex,
        string name,
        OperationalStatus status,
        bool changed,
        bool looksVpn,
        params (string ip, Ipv4DadState dad, int prefix)[] ips)
    {
        return new VpnAdapterReadinessCandidate(
            "id-" + ifIndex,
            name,
            name,
            status,
            ifIndex,
            ips.Select(i => new Ipv4TunnelAddress(i.ip, i.prefix, i.dad)).ToArray(),
            changed,
            changed,
            looksVpn);
    }

    [Fact]
    public void A_apipa_only_tap_is_not_ready()
    {
        var candidate = Cand(8, "TAP #2", OperationalStatus.Up, true, true, ("169.254.32.155", Ipv4DadState.Preferred, 16));
        bool ok = VpnAdapterReadiness.TrySelectBest([candidate], "10.28.0.1", "10.28.0.7", [], out VpnAdapterSelection? sel, out _);
        Assert.False(ok);
        Assert.Null(sel);
    }

    [Fact]
    public void B_tentative_dco_is_not_ready()
    {
        var candidate = Cand(9, "OpenVPN DCO", OperationalStatus.Up, true, true, ("10.28.0.2", Ipv4DadState.Tentative, 22));
        bool ok = VpnAdapterReadiness.TrySelectBest([candidate], "10.28.0.1", "10.28.0.2", [], out _, out _);
        Assert.False(ok);
    }

    [Fact]
    public void C_usable_tap_with_gateway_is_selected()
    {
        var candidate = Cand(8, "TAP", OperationalStatus.Up, true, true, ("10.28.0.7", Ipv4DadState.Preferred, 22));
        bool ok = VpnAdapterReadiness.TrySelectBest([candidate], "10.28.0.1", "10.28.0.7", [], out VpnAdapterSelection? sel, out _);
        Assert.True(ok);
        Assert.NotNull(sel);
        Assert.Equal(8, sel!.IfIndex);
        Assert.Equal("10.28.0.7", sel.SelectedAddress.Address);
    }

    [Fact]
    public void D_usable_dco_selected_over_apipa_tap()
    {
        var tap = Cand(8, "TAP #2", OperationalStatus.Up, true, true, ("169.254.32.155", Ipv4DadState.Preferred, 16));
        var dco = Cand(9, "OpenVPN DCO", OperationalStatus.Up, true, true, ("10.28.0.7", Ipv4DadState.Preferred, 22));
        bool ok = VpnAdapterReadiness.TrySelectBest([tap, dco], "10.28.0.1", "10.28.0.7", [], out VpnAdapterSelection? sel, out _);
        Assert.True(ok);
        Assert.Equal(9, sel!.IfIndex);
    }

    [Fact]
    public void E_usable_tap_selected_over_tentative_dco()
    {
        var tap = Cand(8, "TAP", OperationalStatus.Up, true, true, ("10.28.0.7", Ipv4DadState.Preferred, 22));
        var dco = Cand(9, "OpenVPN DCO", OperationalStatus.Up, false, true, ("10.28.0.2", Ipv4DadState.Tentative, 22));
        bool ok = VpnAdapterReadiness.TrySelectBest([tap, dco], "10.28.0.1", "10.28.0.7", [], out VpnAdapterSelection? sel, out _);
        Assert.True(ok);
        Assert.Equal(8, sel!.IfIndex);
    }

    [Fact]
    public void F_no_usable_tunnel_ipv4_is_not_selected()
    {
        var tap = Cand(8, "TAP #2", OperationalStatus.Up, true, true, ("169.254.32.155", Ipv4DadState.Preferred, 16));
        var dco = Cand(9, "OpenVPN DCO", OperationalStatus.Down, false, true, ("10.28.0.2", Ipv4DadState.Tentative, 22));
        bool ok = VpnAdapterReadiness.TrySelectBest([tap, dco], "10.28.0.1", "10.28.0.7", [], out _, out _);
        Assert.False(ok);
    }

    [Fact]
    public void G_route_creation_rejected_for_apipa_selection()
    {
        var selection = new VpnAdapterSelection
        {
            AdapterId = "id-8",
            Name = "TAP",
            Description = "TAP",
            IfIndex = 8,
            SelectedAddress = new Ipv4TunnelAddress("169.254.32.155", 16, Ipv4DadState.Preferred),
            Gateway = "10.28.0.1",
            SelectionReason = "test",
        };
        Assert.Throws<InvalidOperationException>(() => VpnAdapterReadiness.ValidateOwnedRouteOrThrow(selection));
    }

    [Fact]
    public void H_milestone_usable_tunnel_with_openvpn_signals_passes()
    {
        var tap = Cand(8, "OpenVPN TAP-Windows6", OperationalStatus.Up, true, true, ("10.28.0.7", Ipv4DadState.Preferred, 22));
        bool ok = VpnAdapterReadiness.TrySelectBest([tap], "10.28.0.1", "10.28.0.7", [], out VpnAdapterSelection? sel, out _);
        Assert.True(ok);
        Assert.True(VpnAdapterReadiness.TryValidateOwnedRoute(sel!, out string? err));
        Assert.Null(err);
    }
}