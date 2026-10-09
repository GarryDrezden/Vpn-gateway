using SelectiveVpnRouter.Core;
using SelectiveVpnRouter.Core.BrowserRouting;
using SelectiveVpnRouter.Service;
using Xunit;

namespace SelectiveVpnRouter.BrowserRouting.Tests;

public class BrowserIntegrationSnapshotProviderTests
{
    [Fact]
    public void A_initial_snapshot_reflects_never_seen_proxy_and_rule_count()
    {
        using var dir = new TempDir();
        var store = CreateStore(dir, Rules.Many(0));
        var provider = CreateProvider(store, proxyPort: null, tunnelIfIndex: null, client: null);

        BrowserIntegrationSnapshot snap = provider.Create();

        Assert.Equal(BrowserIntegrationContract.IntegrationApiVersion, snap.IntegrationApiVersion);
        Assert.Equal(BrowserIntegrationContract.BrowserClientStatus.NeverSeen, snap.BrowserClient.Status);
        Assert.Null(snap.BrowserClient.LastSeenUtc);
        Assert.Equal(BrowserProxyStatus.Unavailable, snap.BrowserProxy.Status);
        Assert.Null(snap.BrowserProxy.Endpoint);
        Assert.Equal(BrowserIntegrationContract.VpnEgressStatus.Unavailable, snap.VpnEgress.Status);
        Assert.Equal(0, snap.RuleCount);
    }

    [Fact]
    public void B_heartbeat_marks_client_recently_seen()
    {
        var tracker = new BrowserClientTracker(new MutableFakeTimeProvider(DateTimeOffset.UtcNow));
        tracker.Touch("0.1.0", "0.5.0");
        using var dir = new TempDir();
        var provider = CreateProvider(CreateStore(dir, Rules.Many(1)), proxyPort: 19001, tunnelIfIndex: null, client: tracker);

        BrowserIntegrationSnapshot snap = provider.Create();

        Assert.Equal(BrowserIntegrationContract.BrowserClientStatus.RecentlySeen, snap.BrowserClient.Status);
        Assert.NotNull(snap.BrowserClient.LastSeenUtc);
    }

    [Fact]
    public void C_stale_client_after_ttl()
    {
        var clock = new MutableFakeTimeProvider(new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero));
        var tracker = new BrowserClientTracker(clock);
        tracker.Touch("0.1.0", "0.5.0");
        clock.UtcNow = clock.UtcNow.Add(BrowserClientRecentlySeenTtl.Value + TimeSpan.FromSeconds(1));
        using var dir = new TempDir();
        var provider = CreateProvider(CreateStore(dir, Rules.Many(1)), proxyPort: 19001, tunnelIfIndex: null, client: tracker);

        Assert.Equal(BrowserIntegrationContract.BrowserClientStatus.Stale, provider.Create().BrowserClient.Status);
    }

    [Fact]
    public void D_proxy_ready_endpoint_A()
    {
        using var dir = new TempDir();
        var provider = CreateProvider(CreateStore(dir, Rules.Many(2)), proxyPort: 18080, tunnelIfIndex: null, client: null);
        var endpoint = provider.Create().BrowserProxy.Endpoint;
        Assert.NotNull(endpoint);
        Assert.Equal("127.0.0.1", endpoint.Host);
        Assert.Equal(18080, endpoint.Port);
    }

    [Fact]
    public void E_proxy_restart_endpoint_B()
    {
        var proxy = new RuntimeBrowserProxyReadiness();
        proxy.SetReady(18080);
        using var dir = new TempDir();
        var provider = CreateProvider(CreateStore(dir, Rules.Many(2)), proxy, tunnelIfIndex: null, client: null);
        Assert.Equal(18080, provider.Create().BrowserProxy.Endpoint!.Port);
        proxy.SetReady(19042);
        Assert.Equal(19042, provider.Create().BrowserProxy.Endpoint!.Port);
    }

    [Fact]
    public void F_vpn_egress_unavailable_when_tunnel_absent()
    {
        using var dir = new TempDir();
        var provider = CreateProvider(CreateStore(dir, Rules.Many(0)), proxyPort: 19001, tunnelIfIndex: null, client: null);
        var egress = provider.Create().VpnEgress;
        Assert.Equal(BrowserIntegrationContract.VpnEgressStatus.Unavailable, egress.Status);
        Assert.Null(egress.InterfaceIndex);
    }

    [Fact]
    public void G_vpn_egress_ready_with_interface()
    {
        using var dir = new TempDir();
        var provider = CreateProvider(CreateStore(dir, Rules.Many(0)), proxyPort: 19001, tunnelIfIndex: 55, client: null,
            interfaceName: "OpenVPN TAP");
        var egress = provider.Create().VpnEgress;
        Assert.Equal(BrowserIntegrationContract.VpnEgressStatus.Ready, egress.Status);
        Assert.Equal(55, egress.InterfaceIndex);
        Assert.Equal("OpenVPN TAP", egress.InterfaceName);
    }

    [Fact]
    public void H_rule_count_follows_store()
    {
        using var dir = new TempDir();
        var store = CreateStore(dir, Rules.Many(7));
        var provider = CreateProvider(store, proxyPort: 19001, tunnelIfIndex: null, client: null);
        Assert.Equal(7, provider.Create().RuleCount);
    }

    [Fact]
    public void I_runtime_snapshot_does_not_embed_routing_revision()
    {
        using var dir = new TempDir();
        var store = CreateStore(dir, Rules.Many(3), revision: 99);
        var provider = CreateProvider(store, proxyPort: 19001, tunnelIfIndex: null, client: null);
        var json = System.Text.Json.JsonSerializer.Serialize(provider.Create(), ConfigSerializer.JsonOptions);
        Assert.DoesNotContain("stateGeneration", json);
        Assert.DoesNotContain("revision", json);
    }

    private static BrowserRoutingStateStore CreateStore(TempDir dir, IReadOnlyList<BrowserRoutingRule> rules, long revision = 1)
    {
        var store = new BrowserRoutingStateStore(dir.File("state.json"));
        store.Load();
        if (rules.Count > 0 || revision > 0)
            store.Update(0, BrowserRoutingContract.RouteDirect, rules);
        return store;
    }

    private static BrowserIntegrationSnapshotProvider CreateProvider(
        BrowserRoutingStateStore store,
        int? proxyPort,
        int? tunnelIfIndex,
        BrowserClientTracker? client,
        string? interfaceName = null)
    {
        var proxy = new RuntimeBrowserProxyReadiness();
        if (proxyPort is int port)
            proxy.SetReady(port);
        return CreateProvider(store, proxy, tunnelIfIndex, client, interfaceName);
    }

    private static BrowserIntegrationSnapshotProvider CreateProvider(
        BrowserRoutingStateStore store,
        RuntimeBrowserProxyReadiness proxy,
        int? tunnelIfIndex,
        BrowserClientTracker? client,
        string? interfaceName = null) =>
        new(
            store,
            proxy,
            new FakeTunnelEgress(tunnelIfIndex),
            client ?? new BrowserClientTracker(new MutableFakeTimeProvider(DateTimeOffset.UtcNow)),
            new FakeServiceVersion("9.8.7-test"),
            new FakeInterfaceLookup(interfaceName));

    private sealed class MutableFakeTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class FakeTunnelEgress(int? ifIndex) : IVpnTunnelEgressReadiness
    {
        public bool TryGetTunnelInterfaceIndex(out int interfaceIndex)
        {
            if (ifIndex is int value)
            {
                interfaceIndex = value;
                return true;
            }

            interfaceIndex = 0;
            return false;
        }
    }

    private sealed class FakeServiceVersion(string version) : IBrowserIntegrationServiceVersion
    {
        public string ServiceVersion { get; } = version;
    }

    private sealed class FakeInterfaceLookup(string? name) : IVpnInterfaceNameLookup
    {
        public string? TryGetInterfaceName(int interfaceIndex) => name;
    }
}

internal sealed class TestBrowserIntegrationSnapshotProvider : IBrowserIntegrationSnapshotProvider
{
    public BrowserIntegrationSnapshot Create() => new()
    {
        IntegrationApiVersion = BrowserIntegrationContract.IntegrationApiVersion,
        BrowserClient = new BrowserIntegrationClientSnapshot(BrowserIntegrationContract.BrowserClientStatus.NeverSeen, null),
        BrowserProxy = new BrowserIntegrationProxySnapshot(BrowserProxyStatus.Unavailable, null),
        VpnEgress = new BrowserIntegrationVpnEgressSnapshot(BrowserIntegrationContract.VpnEgressStatus.Unavailable, null, null),
        RuleCount = 0,
    };
}
