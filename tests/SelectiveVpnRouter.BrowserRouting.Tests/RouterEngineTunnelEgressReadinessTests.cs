using System.Net.NetworkInformation;
using System.Reflection;
using SelectiveVpnRouter.Core;
using SelectiveVpnRouter.Core.BrowserRouting;
using SelectiveVpnRouter.Network;
using SelectiveVpnRouter.Service;
using Xunit;

namespace SelectiveVpnRouter.BrowserRouting.Tests;

public class RouterEngineTunnelEgressReadinessTests
{
    [Fact]
    public void RouterEngine_implements_tunnel_egress_readiness_default_false()
    {
        var engine = new RouterEngine(
            new Lazy<IBrowserIntegrationSnapshotProvider>(new TestBrowserIntegrationSnapshotProvider()),
            new VpnSessionDnsStore());
        Assert.IsAssignableFrom<IVpnTunnelEgressReadiness>(engine);
        Assert.False(engine.TryGetTunnelInterfaceIndex(out int ifIndex));
        Assert.Equal(0, ifIndex);
    }

    [Fact]
    public void RouterEngine_readiness_false_when_selection_without_connected_running_openvpn()
    {
        var engine = new RouterEngine(
            new Lazy<IBrowserIntegrationSnapshotProvider>(new TestBrowserIntegrationSnapshotProvider()),
            new VpnSessionDnsStore());
        SetPrivateField(engine, "_vpn", new OpenVpnController());
        SetPrivateField(
            engine,
            "_vpnAdapterSelection",
            new VpnAdapterSelection
            {
                AdapterId = "a",
                Name = "tap-test",
                Description = "tap",
                IfIndex = 77,
                SelectedAddress = new Ipv4TunnelAddress("10.9.0.2", 24, Ipv4DadState.Preferred),
                Gateway = "10.9.0.1",
                SelectionReason = "test",
            });
        SetPrivateField(
            engine,
            "_vpnAdapter",
            new AdapterView(
                Id: "tap-id",
                Name: "tap-test",
                Description: "tap",
                Status: OperationalStatus.Up,
                Ipv4Index: 77,
                Ipv6Index: null,
                Ipv4: [],
                Ipv6: [],
                Ipv4TunnelAddresses: []));

        Assert.False(engine.TryGetTunnelInterfaceIndex(out _));
    }

    private static void SetPrivateField(object target, string name, object? value)
    {
        FieldInfo? field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field.SetValue(target, value);
    }
}
