using System.Text;
using System.Text.Json;
using SelectiveVpnRouter.Core.BrowserRouting;
using Xunit;

namespace SelectiveVpnRouter.BrowserRouting.Tests;

public class IntegrationManifestDispatcherTests
{
    [Fact]
    public void A_integration_api_version_is_one()
    {
        JsonElement result = ManifestResult(Rules.Snapshot([]));
        Assert.Equal(1, result.GetProperty("integrationApiVersion").GetInt32());
    }

    [Fact]
    public void B_service_version_present()
    {
        JsonElement result = ManifestResult(Rules.Snapshot([]), serviceVersion: "9.8.7.6");
        Assert.Equal("9.8.7.6", result.GetProperty("serviceVersion").GetString());
    }

    [Fact]
    public void C_capabilities_exact_deterministic_set()
    {
        JsonElement result = ManifestResult(Rules.Snapshot([]));
        var caps = result.GetProperty("capabilities").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Equal(BrowserIntegrationContract.Capabilities, caps);
        Assert.DoesNotContain(caps, c => c == "browserRuleWrite");
    }

    [Fact]
    public void E_vpn_egress_unavailable_when_tunnel_down()
    {
        var tunnel = new FakeVpnTunnelReadiness();
        JsonElement vpn = ManifestResult(Rules.Snapshot([]), tunnel: tunnel).GetProperty("vpnEgress");
        Assert.Equal("Unavailable", vpn.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, vpn.GetProperty("interfaceIndex").ValueKind);
        Assert.Equal(JsonValueKind.Null, vpn.GetProperty("interfaceName").ValueKind);
    }

    [Fact]
    public void F_vpn_egress_ready_with_interface_index()
    {
        var tunnel = new FakeVpnTunnelReadiness();
        tunnel.SetReady(8);
        JsonElement vpn = ManifestResult(Rules.Snapshot([]), tunnel: tunnel).GetProperty("vpnEgress");
        Assert.Equal("Ready", vpn.GetProperty("status").GetString());
        Assert.Equal(8, vpn.GetProperty("interfaceIndex").GetInt32());
    }

    [Fact]
    public void G_vpn_egress_includes_interface_name_when_lookup_succeeds()
    {
        var tunnel = new FakeVpnTunnelReadiness();
        tunnel.SetReady(3);
        var names = new FakeInterfaceNameLookup { Name = "tap0" };
        JsonElement vpn = ManifestResult(Rules.Snapshot([]), tunnel: tunnel, names: names).GetProperty("vpnEgress");
        Assert.Equal("tap0", vpn.GetProperty("interfaceName").GetString());
    }

    [Fact]
    public void H_vpn_egress_ready_without_name_when_lookup_fails()
    {
        var tunnel = new FakeVpnTunnelReadiness();
        tunnel.SetReady(3);
        JsonElement vpn = ManifestResult(Rules.Snapshot([]), tunnel: tunnel).GetProperty("vpnEgress");
        Assert.Equal("Ready", vpn.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, vpn.GetProperty("interfaceName").ValueKind);
    }

    [Fact]
    public void I_vpn_egress_reflects_latest_tunnel_index_without_cache()
    {
        var tunnel = new FakeVpnTunnelReadiness();
        tunnel.SetReady(8);
        var dispatcher = DispatcherTestFactory.Create(Rules.Snapshot([]), tunnel: tunnel);
        Assert.Equal(8, ManifestFrom(dispatcher).GetProperty("vpnEgress").GetProperty("interfaceIndex").GetInt32());
        tunnel.SetReady(15);
        Assert.Equal(15, ManifestFrom(dispatcher).GetProperty("vpnEgress").GetProperty("interfaceIndex").GetInt32());
    }

    [Fact]
    public void J_client_tracker_initial_never_seen()
    {
        JsonElement client = ManifestResult(Rules.Snapshot([])).GetProperty("browserClient");
        Assert.Equal("NeverSeen", client.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, client.GetProperty("lastSeenUtc").ValueKind);
    }

    [Fact]
    public void K_valid_heartbeat_marks_recently_seen()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));
        var tracker = new BrowserClientTracker(time);
        var dispatcher = DispatcherTestFactory.Create(Rules.Snapshot([]), tracker: tracker);
        Assert.Null(Ipc.ErrorCode(dispatcher.Dispatch(Ipc.ManifestHeartbeat("1.0", "2.0")).Response));
        JsonElement client = ManifestFrom(dispatcher).GetProperty("browserClient");
        Assert.Equal("RecentlySeen", client.GetProperty("status").GetString());
        Assert.Equal("2026-01-02T03:04:05.0000000+00:00", client.GetProperty("lastSeenUtc").GetString());
    }

    [Fact]
    public void L_client_exactly_120_seconds_is_recently_seen()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 2, 0, 2, 0, TimeSpan.Zero));
        var tracker = new BrowserClientTracker(time);
        tracker.Touch("1", "2");
        time.SetUtcNow(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero).Add(BrowserClientRecentlySeenTtl.Value));
        BrowserClientSnapshot snap = tracker.GetSnapshot();
        Assert.Equal("RecentlySeen", snap.Status);
    }

    [Fact]
    public void M_client_stale_after_ttl_plus_epsilon()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero));
        var tracker = new BrowserClientTracker(time);
        tracker.Touch("1", "2");
        time.SetUtcNow(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero).Add(BrowserClientRecentlySeenTtl.Value).AddMilliseconds(1));
        Assert.Equal("Stale", tracker.GetSnapshot().Status);
    }

    [Fact]
    public void N_new_heartbeat_after_stale_becomes_recently_seen()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero));
        var tracker = new BrowserClientTracker(time);
        tracker.Touch("1", "2");
        time.SetUtcNow(time.GetUtcNow().AddMinutes(5));
        Assert.Equal("Stale", tracker.GetSnapshot().Status);
        tracker.Touch("1.1", "2.1");
        Assert.Equal("RecentlySeen", tracker.GetSnapshot().Status);
    }

    [Fact]
    public void O_clock_moving_backwards_is_not_stale()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 2, 0, 5, 0, TimeSpan.Zero));
        var tracker = new BrowserClientTracker(time);
        tracker.Touch("1", "2");
        time.SetUtcNow(new DateTimeOffset(2026, 1, 2, 0, 4, 0, TimeSpan.Zero));
        Assert.Equal("RecentlySeen", tracker.GetSnapshot().Status);
    }

    [Fact]
    public void P_concurrent_touch_and_snapshot_safe()
    {
        var tracker = new BrowserClientTracker(new FakeTimeProvider(DateTimeOffset.UtcNow));
        Parallel.For(0, 100, i => tracker.Touch("e" + i, "n" + i));
        _ = tracker.GetSnapshot();
    }

    [Fact]
    public void Q_manifest_without_params_does_not_touch_tracker()
    {
        var tracker = new BrowserClientTracker(new FakeTimeProvider(DateTimeOffset.UtcNow));
        var dispatcher = DispatcherTestFactory.Create(Rules.Snapshot([]), tracker: tracker);
        Assert.Null(Ipc.ErrorCode(dispatcher.Dispatch(Ipc.Manifest()).Response));
        Assert.Equal("NeverSeen", ManifestFrom(dispatcher).GetProperty("browserClient").GetProperty("status").GetString());
    }

    [Fact]
    public void R_manifest_with_valid_client_params_touches_tracker()
    {
        var tracker = new BrowserClientTracker(new FakeTimeProvider(DateTimeOffset.UtcNow));
        var dispatcher = DispatcherTestFactory.Create(Rules.Snapshot([]), tracker: tracker);
        Assert.Null(Ipc.ErrorCode(dispatcher.Dispatch(Ipc.ManifestHeartbeat("ext", "host")).Response));
        Assert.Equal("RecentlySeen", ManifestFrom(dispatcher).GetProperty("browserClient").GetProperty("status").GetString());
    }

    [Fact]
    public void S_malformed_client_params_invalid_request()
    {
        var tracker = new BrowserClientTracker(new FakeTimeProvider(DateTimeOffset.UtcNow));
        var dispatcher = DispatcherTestFactory.Create(Rules.Snapshot([]), tracker: tracker);
        byte[] req = Encoding.UTF8.GetBytes("""{"version":1,"id":"x","method":"getManifest","params":{"client":{"extensionVersion":"1"}}}""");
        Assert.Equal("invalid_request", Ipc.ErrorCode(dispatcher.Dispatch(req).Response));
        Assert.Equal("NeverSeen", ManifestFrom(dispatcher).GetProperty("browserClient").GetProperty("status").GetString());
    }

    [Theory]
    [InlineData("""{"version":1,"id":"x","method":"getManifest","params":{}}""")]
    [InlineData("""{"version":1,"id":"x","method":"getManifest","params":{"client":{"extensionVersion":"1","nativeHostVersion":"2","extra":1}}}""")]
    [InlineData("""{"version":1,"id":"x","method":"getManifest","params":{"client":{"extensionVersion":"","nativeHostVersion":"2"}}}""")]
    public void T_unexpected_params_shape_rejected(string json)
    {
        var dispatcher = DispatcherTestFactory.Create(Rules.Snapshot([]));
        Assert.Equal("invalid_request", Ipc.ErrorCode(dispatcher.Dispatch(Encoding.UTF8.GetBytes(json)).Response));
    }

    [Fact]
    public void U_oversized_client_version_rejected()
    {
        var dispatcher = DispatcherTestFactory.Create(Rules.Snapshot([]));
        string big = new('a', BrowserIntegrationContract.ClientVersionMaxLength + 1);
        byte[] req = Ipc.ManifestHeartbeat(big, "2");
        Assert.Equal("invalid_request", Ipc.ErrorCode(dispatcher.Dispatch(req).Response));
    }

    [Fact]
    public void V_legacy_phase5_get_manifest_still_succeeds()
    {
        var dispatcher = DispatcherTestFactory.Create(Rules.Snapshot(Rules.Many(2)));
        Assert.Null(Ipc.ErrorCode(dispatcher.Dispatch(Ipc.Manifest()).Response));
    }

    [Fact]
    public void W_heartbeat_does_not_change_revision_or_generation()
    {
        var snapshot = Rules.Snapshot(Rules.Many(1), revision: 55);
        var tracker = new BrowserClientTracker(new FakeTimeProvider(DateTimeOffset.UtcNow));
        var dispatcher = DispatcherTestFactory.Create(snapshot, tracker: tracker);
        Assert.Null(Ipc.ErrorCode(dispatcher.Dispatch(Ipc.ManifestHeartbeat("1", "2")).Response));
        JsonElement result = ManifestFrom(dispatcher);
        Assert.Equal(55, result.GetProperty("revision").GetInt64());
        Assert.Equal(snapshot.StateGeneration, result.GetProperty("stateGeneration").GetString());
        Assert.Equal("RecentlySeen", result.GetProperty("browserClient").GetProperty("status").GetString());
    }

    [Fact]
    public void X_vpn_egress_transition_does_not_change_revision()
    {
        var snapshot = Rules.Snapshot([]);
        var tunnel = new FakeVpnTunnelReadiness();
        var dispatcher = DispatcherTestFactory.Create(snapshot, tunnel: tunnel);
        long revision = ManifestFrom(dispatcher).GetProperty("revision").GetInt64();
        tunnel.SetReady(4);
        JsonElement after = ManifestFrom(dispatcher);
        Assert.Equal(revision, after.GetProperty("revision").GetInt64());
        Assert.Equal("Ready", after.GetProperty("vpnEgress").GetProperty("status").GetString());
    }

    [Fact]
    public void Y_browser_proxy_endpoint_change_keeps_revision()
    {
        var snapshot = Rules.Snapshot([], revision: 12);
        var dispatcher = DispatcherTestFactory.Create(snapshot, new BrowserProxyStatus(BrowserProxyStatus.Ready, "127.0.0.1", 10001));
        long revision = ManifestFrom(dispatcher).GetProperty("revision").GetInt64();
        var dispatcher2 = DispatcherTestFactory.Create(snapshot, new BrowserProxyStatus(BrowserProxyStatus.Ready, "127.0.0.1", 10002));
        JsonElement after = ManifestFrom(dispatcher2);
        Assert.Equal(revision, after.GetProperty("revision").GetInt64());
        Assert.Equal(10002, after.GetProperty("browserProxy").GetProperty("endpoint").GetProperty("port").GetInt32());
    }

    private static JsonElement ManifestResult(
        BrowserRoutingSnapshot snapshot,
        FakeVpnTunnelReadiness? tunnel = null,
        FakeInterfaceNameLookup? names = null,
        string serviceVersion = "1.0.0.0") =>
        ManifestFrom(DispatcherTestFactory.Create(snapshot, tunnel: tunnel, names: names, serviceVersion: serviceVersion));

    private static JsonElement ManifestFrom(BrowserRoutingIpcDispatcher dispatcher) =>
        Ipc.Parse(dispatcher.Dispatch(Ipc.Manifest()).Response).GetProperty("result");
}
