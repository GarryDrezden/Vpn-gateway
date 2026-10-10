using System.Text.Json;
using SelectiveVpnRouter.Core.BrowserRouting;

namespace SelectiveVpnRouter.BrowserRouting.Tests;

internal static class Repo
{
    public static string Root { get; } = FindRoot();

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SelectiveVpnRouter.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    public static string VectorsPath => Path.Combine(Root, "tests", "contracts", "browser-routing-v1", "golden-vectors.json");
}

internal sealed class TempDir : IDisposable
{
    public TempDir() => Directory.CreateDirectory(Path);

    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "vpnroute-br-" + Guid.NewGuid().ToString("N"));
    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

internal static class Rules
{
    public static BrowserRoutingRule Make(int i, string? host = null, string matchType = BrowserRoutingContract.DomainAndSubdomains) =>
        new($"r{i:D5}", $"Rule {i}", [host ?? $"host{i}.example.com"], matchType, BrowserRoutingContract.RouteVpn, true,
            BrowserRoutingContract.SourceUser, null);

    public static List<BrowserRoutingRule> Many(int count) => Enumerable.Range(0, count).Select(i => Make(i)).ToList();

    /// <summary>The largest rule the contract allows: every text field at its limit and fully \u-escaped.</summary>
    public static BrowserRoutingRule Worst(int i)
    {
        var label = new string('a', 63);
        var host = $"{label}.{label}.{label}.h{i:D5}{new string('b', 55)}";
        return new BrowserRoutingRule(
            ("w" + i.ToString("D5")).PadRight(64, 'x'),
            new string('\u0416', 120),
            [host],
            BrowserRoutingContract.DomainAndSubdomains,
            BrowserRoutingContract.RouteDirect,
            true,
            BrowserRoutingContract.SourceSystem,
            new string('\u0416', 1000));
    }

    public static BrowserRoutingSnapshot Snapshot(IReadOnlyList<BrowserRoutingRule> rules, long revision = 42, string? generation = null) =>
        BrowserRoutingSnapshot.Create(new BrowserRoutingState(generation ?? StateGenerationFormat.New(), revision,
            BrowserRoutingContract.RouteDirect, rules));
}

internal static class Ipc
{
    public static byte[] Request(object value) => JsonSerializer.SerializeToUtf8Bytes(value);

    public static byte[] Manifest(string id = "req-1") => Request(new { version = 1, id, method = "getManifest" });

    public static byte[] Page(string generation, long revision, int startIndex, string id = "req-2") =>
        Request(new { version = 1, id, method = "getPage", @params = new { stateGeneration = generation, revision, startIndex } });

    public static JsonElement Parse(byte[] response) => JsonDocument.Parse(response).RootElement.Clone();

    public static string? ErrorCode(byte[] response)
    {
        var root = Parse(response);
        return root.GetProperty("ok").GetBoolean() ? null : root.GetProperty("error").GetProperty("code").GetString();
    }

    public static byte[] ManifestHeartbeat(string extensionVersion, string nativeHostVersion, string id = "req-hb") =>
        Request(new
        {
            version = 1,
            id,
            method = "getManifest",
            @params = new
            {
                client = new { extensionVersion, nativeHostVersion },
            },
        });

    public static byte[] UpsertRule(long expectedRevision, object rule, string id = "w-upsert") =>
        Request(new { version = 1, id, method = "upsertRule", @params = new { expectedRevision, rule } });

    public static byte[] DeleteRule(long expectedRevision, string ruleId, string id = "w-delete") =>
        Request(new { version = 1, id, method = "deleteRule", @params = new { expectedRevision, id = ruleId } });

    public static byte[] ResetRules(long expectedRevision, string id = "w-reset") =>
        Request(new { version = 1, id, method = "resetRules", @params = new { expectedRevision } });

    public static byte[] SubscribeEvents(string id = "sub-1") =>
        Request(new { version = 1, id, method = "subscribeEvents" });
}

internal sealed class TestServiceVersion(string value) : IBrowserIntegrationServiceVersion
{
    public string ServiceVersion { get; } = value;
}

internal sealed class FakeVpnTunnelReadiness : IVpnTunnelEgressReadiness
{
    private bool _ready;
    private int _ifIndex;

    public void SetReady(int ifIndex)
    {
        _ready = true;
        _ifIndex = ifIndex;
    }

    public void SetUnavailable()
    {
        _ready = false;
        _ifIndex = 0;
    }

    public bool TryGetTunnelInterfaceIndex(out int interfaceIndex)
    {
        interfaceIndex = _ifIndex;
        return _ready;
    }
}

internal sealed class FakeInterfaceNameLookup : IVpnInterfaceNameLookup
{
    public string? Name { get; set; }

    public string? TryGetInterfaceName(int interfaceIndex) => Name;
}

internal static class DispatcherTestFactory
{
    public static BrowserRoutingIpcDispatcher Create(
        BrowserRoutingSnapshot? snapshot,
        BrowserProxyStatus? proxy = null,
        IBrowserProxyReadiness? proxyReadiness = null,
        FakeVpnTunnelReadiness? tunnel = null,
        BrowserClientTracker? tracker = null,
        string serviceVersion = "1.2.3.4",
        FakeInterfaceNameLookup? names = null) =>
        Create(() => snapshot, proxy, proxyReadiness, tunnel, tracker, serviceVersion, names);

    public static BrowserRoutingIpcDispatcher Create(
        Func<BrowserRoutingSnapshot?> snapshotProvider,
        BrowserProxyStatus? proxy = null,
        IBrowserProxyReadiness? proxyReadiness = null,
        FakeVpnTunnelReadiness? tunnel = null,
        BrowserClientTracker? tracker = null,
        string serviceVersion = "1.2.3.4",
        FakeInterfaceNameLookup? names = null,
        BrowserRoutingStateStore? writeStore = null)
    {
        tunnel ??= new FakeVpnTunnelReadiness();
        tracker ??= new BrowserClientTracker(new FakeTimeProvider(DateTimeOffset.UtcNow));
        names ??= new FakeInterfaceNameLookup();
        proxyReadiness ??= new StubProxyReadiness(proxy ?? BrowserProxyStatus.NotAvailable);
        return new BrowserRoutingIpcDispatcher(
            snapshotProvider,
            writeStore,
            proxyReadiness,
            tunnel,
            tracker,
            new TestServiceVersion(serviceVersion),
            names);
    }

    private sealed class StubProxyReadiness(BrowserProxyStatus status) : IBrowserProxyReadiness
    {
        public BrowserProxyStatus GetStatus() => status;
    }
}

internal sealed class FakeTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    private DateTimeOffset _utcNow = utcNow;

    public void SetUtcNow(DateTimeOffset value) => _utcNow = value;

    public override DateTimeOffset GetUtcNow() => _utcNow;
}
