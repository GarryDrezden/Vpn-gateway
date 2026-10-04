using System.Text;
using System.Text.Json;
using SelectiveVpnRouter.Core.BrowserRouting;
using Xunit;

namespace SelectiveVpnRouter.BrowserRouting.Tests;

public class DispatcherTests
{
    private sealed class Readiness(BrowserProxyStatus status) : IBrowserProxyReadiness
    {
        public BrowserProxyStatus GetStatus() => status;
    }

    private static BrowserRoutingIpcDispatcher For(BrowserRoutingSnapshot? snapshot, BrowserProxyStatus? proxy = null) =>
        new(() => snapshot, new Readiness(proxy ?? BrowserProxyStatus.NotAvailable));

    private static List<JsonElement> ReadAll(BrowserRoutingIpcDispatcher dispatcher, out List<int> sizes)
    {
        var manifest = Ipc.Parse(dispatcher.Dispatch(Ipc.Manifest()).Response).GetProperty("result");
        var generation = manifest.GetProperty("stateGeneration").GetString()!;
        var revision = manifest.GetProperty("revision").GetInt64();
        var count = manifest.GetProperty("ruleCount").GetInt32();
        var rules = new List<JsonElement>();
        sizes = [];
        var next = 0;
        while (next < count)
        {
            var bytes = dispatcher.Dispatch(Ipc.Page(generation, revision, next)).Response;
            sizes.Add(bytes.Length);
            var page = Ipc.Parse(bytes).GetProperty("result");
            Assert.Equal(next, page.GetProperty("startIndex").GetInt32());
            rules.AddRange(page.GetProperty("rules").EnumerateArray());
            var nextIndex = page.GetProperty("nextIndex");
            next = nextIndex.ValueKind == JsonValueKind.Null ? count : nextIndex.GetInt32();
            Assert.True(sizes.Count <= BrowserRoutingIpcProtocol.MaxPagesPerSnapshot);
        }
        Assert.Equal(count, rules.Count);
        return rules;
    }

    [Fact]
    public void Manifest_carries_identity_counts_and_unavailable_proxy()
    {
        var snapshot = Rules.Snapshot(Rules.Many(3), revision: 42);
        var response = Ipc.Parse(For(snapshot).Dispatch(Ipc.Manifest("abc-1")).Response);

        Assert.Equal(1, response.GetProperty("version").GetInt32());
        Assert.Equal("abc-1", response.GetProperty("id").GetString());
        Assert.True(response.GetProperty("ok").GetBoolean());
        var result = response.GetProperty("result");
        Assert.Equal(1, result.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(snapshot.StateGeneration, result.GetProperty("stateGeneration").GetString());
        Assert.Equal(42, result.GetProperty("revision").GetInt64());
        Assert.Equal("Direct", result.GetProperty("defaultRoute").GetString());
        Assert.Equal(3, result.GetProperty("ruleCount").GetInt32());
        Assert.Equal(BrowserRoutingIpcProtocol.PageRulesBudgetBytes, result.GetProperty("pageBudgetBytes").GetInt32());
        Assert.Equal("Unavailable", result.GetProperty("browserProxy").GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("browserProxy").GetProperty("endpoint").ValueKind);
        Assert.Equal(7, result.EnumerateObject().Count());
    }

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("192.168.1.10")]
    [InlineData("localhost")]
    [InlineData("127.000.0.1")]
    public void Ready_proxy_with_a_non_loopback_endpoint_is_reported_unavailable(string host)
    {
        var dispatcher = For(Rules.Snapshot([]), new BrowserProxyStatus(BrowserProxyStatus.Ready, host, 1080));
        var proxy = Ipc.Parse(dispatcher.Dispatch(Ipc.Manifest()).Response).GetProperty("result").GetProperty("browserProxy");
        Assert.Equal("Unavailable", proxy.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, proxy.GetProperty("endpoint").ValueKind);
    }

    [Fact]
    public void Ready_loopback_proxy_is_reported_with_endpoint()
    {
        var dispatcher = For(Rules.Snapshot([]), new BrowserProxyStatus(BrowserProxyStatus.Ready, "127.0.0.1", 18080));
        var proxy = Ipc.Parse(dispatcher.Dispatch(Ipc.Manifest()).Response).GetProperty("result").GetProperty("browserProxy");
        Assert.Equal("Ready", proxy.GetProperty("status").GetString());
        Assert.Equal("127.0.0.1", proxy.GetProperty("endpoint").GetProperty("host").GetString());
        Assert.Equal(18080, proxy.GetProperty("endpoint").GetProperty("port").GetInt32());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3000)]
    [InlineData(10000)]
    public void All_rules_arrive_in_order_in_bounded_pages(int count)
    {
        var snapshot = Rules.Snapshot(Rules.Many(count));
        var rules = ReadAll(For(snapshot), out var sizes);

        Assert.Equal(snapshot.State.Rules.Select(r => r.Id), rules.Select(r => r.GetProperty("id").GetString()));
        Assert.All(sizes, size => Assert.True(size <= BrowserRoutingIpcProtocol.MaxResponseBytes, size + " bytes"));
        if (count == 10000)
            Assert.True(sizes.Count > 1);
    }

    [Fact]
    public void Worst_case_10000_rules_stay_within_page_and_count_bounds()
    {
        var snapshot = Rules.Snapshot(Enumerable.Range(0, 10000).Select(Rules.Worst).ToList());
        var rules = ReadAll(For(snapshot), out var sizes);
        Assert.Equal(10000, rules.Count);
        Assert.All(sizes, size => Assert.True(size <= BrowserRoutingIpcProtocol.MaxResponseBytes));
        Assert.True(sizes.Count <= BrowserRoutingIpcProtocol.MaxPagesPerSnapshot, sizes.Count + " pages");
        Assert.True(sizes.Count > 100);
    }

    [Fact]
    public void Zero_rules_need_no_page_and_any_page_is_an_invalid_cursor()
    {
        var snapshot = Rules.Snapshot([]);
        var dispatcher = For(snapshot);
        Assert.Equal(0, Ipc.Parse(dispatcher.Dispatch(Ipc.Manifest()).Response).GetProperty("result").GetProperty("ruleCount").GetInt32());
        Assert.Equal("invalid_cursor", Ipc.ErrorCode(dispatcher.Dispatch(Ipc.Page(snapshot.StateGeneration, 42, 0)).Response));
    }

    [Fact]
    public void Page_boundary_exactly_at_budget_is_full_and_one_byte_less_splits()
    {
        var snapshot = Rules.Snapshot(Enumerable.Range(0, 200).Select(Rules.Worst).ToList());
        var end = snapshot.PageEnd(0, BrowserRoutingIpcProtocol.PageRulesBudgetBytes);
        long exact = 2;
        for (var i = 0; i < end; i++)
            exact += snapshot.RuleJson(i).Length + (i > 0 ? 1 : 0);

        Assert.Equal(end, snapshot.PageEnd(0, (int)exact));
        Assert.Equal(end - 1, snapshot.PageEnd(0, (int)exact - 1));
        Assert.True(exact <= BrowserRoutingIpcProtocol.PageRulesBudgetBytes);
        Assert.Equal(1, snapshot.PageEnd(0, 10));
    }

    [Fact]
    public void Same_identity_produces_identical_bytes()
    {
        var snapshot = Rules.Snapshot(Rules.Many(5000));
        var dispatcher = For(snapshot);
        Assert.Equal(dispatcher.Dispatch(Ipc.Page(snapshot.StateGeneration, 42, 0)).Response,
            dispatcher.Dispatch(Ipc.Page(snapshot.StateGeneration, 42, 0)).Response);
    }

    [Fact]
    public void Revision_or_generation_change_between_pages_is_snapshot_changed()
    {
        using var dir = new TempDir();
        var store = new BrowserRoutingStateStore(dir.File("state.json"));
        store.Load();
        store.Update(0, "Direct", Rules.Many(4000));
        var dispatcher = new BrowserRoutingIpcDispatcher(() => store.Current, new UnavailableBrowserProxyReadiness());
        var generation = store.Current!.StateGeneration;

        Assert.Null(Ipc.ErrorCode(dispatcher.Dispatch(Ipc.Page(generation, 1, 0)).Response));
        store.Update(1, "Direct", Rules.Many(4000));
        Assert.Equal("snapshot_changed", Ipc.ErrorCode(dispatcher.Dispatch(Ipc.Page(generation, 1, 100)).Response));

        store.Reset();
        Assert.Equal("snapshot_changed", Ipc.ErrorCode(dispatcher.Dispatch(Ipc.Page(generation, 2, 0)).Response));
    }

    [Fact]
    public void Unavailable_state_is_reported_not_replaced()
    {
        var dispatcher = For(null);
        Assert.Equal("browser_state_unavailable", Ipc.ErrorCode(dispatcher.Dispatch(Ipc.Manifest()).Response));
        Assert.Equal("browser_state_unavailable",
            Ipc.ErrorCode(dispatcher.Dispatch(Ipc.Page(StateGenerationFormat.New(), 0, 0)).Response));
    }

    [Theory]
    [InlineData(-1, "invalid_request")]
    [InlineData(10001, "invalid_request")]
    [InlineData(10, "invalid_cursor")]
    [InlineData(11, "invalid_cursor")]
    public void Cursor_is_a_bounded_integer_index(int startIndex, string code)
    {
        var snapshot = Rules.Snapshot(Rules.Many(10));
        Assert.Equal(code, Ipc.ErrorCode(For(snapshot).Dispatch(Ipc.Page(snapshot.StateGeneration, 42, startIndex)).Response));
    }

    [Theory]
    [InlineData("{\"version\":1,\"id\":\"x\",\"method\":\"getPage\",\"params\":{\"stateGeneration\":\"G\",\"revision\":42,\"startIndex\":1.5}}", "invalid_request")]
    [InlineData("{\"version\":1,\"id\":\"x\",\"method\":\"getPage\",\"params\":{\"stateGeneration\":\"G\",\"revision\":42,\"startIndex\":\"0\"}}", "invalid_request")]
    [InlineData("{\"version\":1,\"id\":\"x\",\"method\":\"getPage\",\"params\":{\"stateGeneration\":\"G\",\"revision\":42,\"startIndex\":0,\"path\":\"..\\\\x\"}}", "invalid_request")]
    [InlineData("{\"version\":1,\"id\":\"x\",\"method\":\"getPage\",\"params\":{\"stateGeneration\":\"G\",\"revision\":42}}", "invalid_request")]
    [InlineData("{\"version\":1,\"id\":\"x\",\"method\":\"getPage\",\"params\":{\"stateGeneration\":\"not-a-guid\",\"revision\":42,\"startIndex\":0}}", "invalid_request")]
    [InlineData("{\"version\":1,\"id\":\"x\",\"method\":\"getPage\",\"params\":{\"stateGeneration\":\"G\",\"revision\":-1,\"startIndex\":0}}", "invalid_request")]
    [InlineData("{\"version\":1,\"id\":\"x\",\"method\":\"getManifest\",\"params\":{}}", "invalid_request")]
    [InlineData("{\"version\":1,\"id\":\"x\",\"method\":\"getManifest\",\"extra\":1}", "invalid_request")]
    [InlineData("{\"version\":1,\"id\":\"x\",\"method\":\"getManifest\",\"method\":\"getManifest\"}", "invalid_request")]
    [InlineData("{\"version\":2,\"id\":\"x\",\"method\":\"getManifest\"}", "unsupported_version")]
    [InlineData("{\"version\":1,\"id\":\"x\",\"method\":\"SetConfig\"}", "unknown_method")]
    [InlineData("{\"version\":1,\"id\":\"x\",\"method\":\"ConnectVpn\"}", "unknown_method")]
    [InlineData("{\"version\":1,\"id\":\"x\",\"method\":\"GetStatus\"}", "unknown_method")]
    [InlineData("{\"version\":1,\"id\":\"x\",\"method\":\"relay\",\"params\":{\"command\":\"EmergencyRestore\"}}", "unknown_method")]
    [InlineData("{\"version\":1,\"id\":\"x y\",\"method\":\"getManifest\"}", "invalid_request")]
    [InlineData("{\"version\":1,\"method\":\"getManifest\"}", "invalid_request")]
    [InlineData("[1]", "invalid_request")]
    [InlineData("{", "invalid_request")]
    public void Malformed_or_foreign_requests_are_rejected(string json, string code)
    {
        var snapshot = Rules.Snapshot(Rules.Many(3));
        var bytes = Encoding.UTF8.GetBytes(json.Replace("\"G\"", "\"" + snapshot.StateGeneration + "\""));
        Assert.Equal(code, Ipc.ErrorCode(For(snapshot).Dispatch(bytes).Response));
    }

    [Fact]
    public void Oversized_or_invalid_utf8_request_is_rejected()
    {
        var dispatcher = For(Rules.Snapshot([]));
        Assert.Equal("invalid_request", Ipc.ErrorCode(dispatcher.Dispatch(new byte[BrowserRoutingIpcProtocol.MaxRequestBytes + 1]).Response));
        Assert.Equal("invalid_request", Ipc.ErrorCode(dispatcher.Dispatch(new byte[] { 0x7b, 0xff, 0x7d }).Response));
        Assert.Equal("invalid_request", Ipc.ErrorCode(dispatcher.Dispatch(ReadOnlySpan<byte>.Empty).Response));
    }

    [Fact]
    public void Error_response_has_only_a_code_and_never_echoes_request_data()
    {
        var response = Ipc.Parse(For(null).Dispatch(Ipc.Manifest("secret-id")).Response);
        Assert.Equal("secret-id", response.GetProperty("id").GetString());
        Assert.Equal(["code"], response.GetProperty("error").EnumerateObject().Select(p => p.Name));
        var foreign = Encoding.UTF8.GetString(For(null).Dispatch(Encoding.UTF8.GetBytes("{\"version\":1,\"id\":\"x\",\"method\":\"evil.example.com\"}")).Response);
        Assert.DoesNotContain("evil", foreign);
    }
}
