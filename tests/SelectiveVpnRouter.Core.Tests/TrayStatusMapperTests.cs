using SelectiveVpnRouter.Core;
using SelectiveVpnRouter.Core.Tray;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public class TrayStatusMapperTests
{
    private static ServiceSnapshot HealthyDisconnected() => new()
    {
        DriverLoaded = true,
        DriverExpected = true,
        VpnRoutingReady = false,
        Vpn = new OpenVpnLiveStatus { Running = false, Connected = false },
        Callout = new CalloutArmStatus { DeviceOpen = true },
        WfpPolicy = WfpPolicyDiagnostics.Empty with { PolicyHealthy = true },
    };

    [Fact]
    public void Service_unavailable_maps_to_error()
    {
        TrayStatusPresentation p = TrayStatusMapper.Map(null, serviceAvailable: false, connectUiInProgress: false);
        Assert.Equal(TrayVisualState.Error, p.VisualState);
        Assert.Contains("служба", p.MenuStatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Disconnected_healthy_maps_to_ready_not_error()
    {
        TrayStatusPresentation p = TrayStatusMapper.Map(HealthyDisconnected(), serviceAvailable: true, connectUiInProgress: false);
        Assert.Equal(TrayVisualState.Ready, p.VisualState);
        Assert.True(p.ConnectEnabled);
        Assert.False(p.DisconnectEnabled);
    }

    [Fact]
    public void Vpn_routing_ready_maps_to_active()
    {
        ServiceSnapshot snap = HealthyDisconnected() with
        {
            VpnRoutingReady = true,
            TransparentRedirectActive = true,
            Vpn = new OpenVpnLiveStatus { Running = true, Connected = true },
            VpnAdapter = new AdapterLiveStatus { Name = "tun0" },
            ProxyPort = 18001,
            OwnedTransportDefault = new OwnedRoute
            {
                Id = Guid.NewGuid(),
                DestinationPrefix = "0.0.0.0/0",
                InterfaceIndex = 12,
                NextHop = "10.8.0.1",
                Metric = 25,
                Reason = "test",
            },
        };
        TrayStatusPresentation p = TrayStatusMapper.Map(snap, serviceAvailable: true, connectUiInProgress: false);
        Assert.Equal(TrayVisualState.Active, p.VisualState);
        Assert.False(p.ConnectEnabled);
        Assert.True(p.DisconnectEnabled);
    }

    [Fact]
    public void Driver_load_error_has_priority_over_ready()
    {
        ServiceSnapshot snap = HealthyDisconnected() with
        {
            DriverLoaded = false,
            DriverLoadError = "Callout driver failed to load",
        };
        TrayStatusPresentation p = TrayStatusMapper.Map(snap, serviceAvailable: true, connectUiInProgress: false);
        Assert.Equal(TrayVisualState.Error, p.VisualState);
    }

    [Fact]
    public void Connecting_while_openvpn_running_maps_to_connecting()
    {
        ServiceSnapshot snap = HealthyDisconnected() with
        {
            Vpn = new OpenVpnLiveStatus { Running = true, Connected = false },
        };
        TrayStatusPresentation p = TrayStatusMapper.Map(snap, serviceAvailable: true, connectUiInProgress: false);
        Assert.Equal(TrayVisualState.Connecting, p.VisualState);
        Assert.True(p.DisconnectEnabled);
    }

    [Fact]
    public void Stale_owned_route_while_disconnected_maps_to_error()
    {
        ServiceSnapshot snap = HealthyDisconnected() with
        {
            OwnedTransportDefault = new OwnedRoute
            {
                Id = Guid.NewGuid(),
                DestinationPrefix = "0.0.0.0/0",
                InterfaceIndex = 12,
                NextHop = "10.8.0.1",
                Metric = 25,
                Reason = "test",
            },
        };
        TrayStatusPresentation p = TrayStatusMapper.Map(snap, serviceAvailable: true, connectUiInProgress: false);
        Assert.Equal(TrayVisualState.Error, p.VisualState);
    }

    [Fact]
    public void Connect_ui_in_progress_disables_connect_action()
    {
        TrayStatusPresentation p = TrayStatusMapper.Map(HealthyDisconnected(), serviceAvailable: true, connectUiInProgress: true);
        Assert.Equal(TrayVisualState.Connecting, p.VisualState);
        Assert.False(p.ConnectEnabled);
    }
}
