using System.Text.Json;
using SelectiveVpnRouter.Core;
using SelectiveVpnRouter.Core.BrowserRouting;
using SelectiveVpnRouter.Service;
using Xunit;

namespace SelectiveVpnRouter.BrowserRouting.Tests;

public class BrowserIntegrationControlIpcTests
{
    [Fact]
    public void J_browserIntegration_section_serializes_on_service_snapshot()
    {
        using var dir = new TempDir();
        var store = new BrowserRoutingStateStore(dir.File("state.json"));
        store.Load();
        var provider = new BrowserIntegrationSnapshotProvider(
            store,
            ReadyProxy(20001),
            new FakeTunnel(33),
            new BrowserClientTracker(),
            new FakeServiceVersion("1.2.3"),
            new FakeNameLookup("tap0"));
        var engine = new RouterEngine(
            new Lazy<IBrowserIntegrationSnapshotProvider>(provider),
            new SelectiveVpnRouter.Network.VpnSessionDnsStore());
        ServiceSnapshot snap = engine.Snapshot();

        Assert.NotNull(snap.BrowserIntegration);
        string json = JsonSerializer.Serialize(snap, ConfigSerializer.JsonOptions);
        using var doc = JsonDocument.Parse(json);
        var bi = doc.RootElement.GetProperty("browserIntegration");
        Assert.Equal(1, bi.GetProperty("integrationApiVersion").GetInt32());
        Assert.Equal("1.2.3", bi.GetProperty("serviceVersion").GetString());
        Assert.Equal("NeverSeen", bi.GetProperty("browserClient").GetProperty("status").GetString());
        Assert.Equal("Ready", bi.GetProperty("browserProxy").GetProperty("status").GetString());
        Assert.Equal(20001, bi.GetProperty("browserProxy").GetProperty("endpoint").GetProperty("port").GetInt32());
        Assert.Equal("Ready", bi.GetProperty("vpnEgress").GetProperty("status").GetString());
        Assert.Equal(33, bi.GetProperty("vpnEgress").GetProperty("interfaceIndex").GetInt32());
        Assert.Equal("tap0", bi.GetProperty("vpnEgress").GetProperty("interfaceName").GetString());
    }

    [Fact]
    public void K_missing_browserIntegration_deserializes_without_throwing()
    {
        const string legacy = """{"serviceAlive":true,"routingPaused":false,"driverLoaded":false}""";
        ServiceSnapshot? snap = JsonSerializer.Deserialize<ServiceSnapshot>(legacy, ConfigSerializer.JsonOptions);
        Assert.NotNull(snap);
        Assert.Null(snap!.BrowserIntegration);
    }

    [Fact]
    public void L_app_project_does_not_reference_browser_routing_pipe()
    {
        string appDir = Path.Combine(Repo.Root, "src", "SelectiveVpnRouter.App");
        var files = Directory.GetFiles(appDir, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase));
        foreach (string file in files)
        {
            string text = File.ReadAllText(file);
            Assert.DoesNotContain(BrowserRoutingIpcProtocol.PipeName, text);
            Assert.DoesNotContain("BrowserRoutingPipeHost", text);
            Assert.DoesNotContain("BrowserRoutingIpcDispatcher", text);
        }
    }

    [Fact]
    public void M_service_snapshot_json_stays_within_ipc_limit()
    {
        var engine = new RouterEngine(
            new Lazy<IBrowserIntegrationSnapshotProvider>(new TestBrowserIntegrationSnapshotProvider()),
            new SelectiveVpnRouter.Network.VpnSessionDnsStore());
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(engine.Snapshot(), ConfigSerializer.JsonOptions);
        Assert.True(json.Length < 4_000_000);
    }

    private static RuntimeBrowserProxyReadiness ReadyProxy(int port)
    {
        var proxy = new RuntimeBrowserProxyReadiness();
        proxy.SetReady(port);
        return proxy;
    }

    private sealed class FakeTunnel(int ifIndex) : IVpnTunnelEgressReadiness
    {
        public bool TryGetTunnelInterfaceIndex(out int interfaceIndex)
        {
            interfaceIndex = ifIndex;
            return true;
        }
    }

    private sealed class FakeServiceVersion(string version) : IBrowserIntegrationServiceVersion
    {
        public string ServiceVersion { get; } = version;
    }

    private sealed class FakeNameLookup(string name) : IVpnInterfaceNameLookup
    {
        public string? TryGetInterfaceName(int interfaceIndex) => name;
    }
}
