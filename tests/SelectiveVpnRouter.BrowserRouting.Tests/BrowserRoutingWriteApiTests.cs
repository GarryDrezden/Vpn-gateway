using System.Text.Json;
using SelectiveVpnRouter.Core.BrowserRouting;
using Xunit;

namespace SelectiveVpnRouter.BrowserRouting.Tests;

public class BrowserRoutingWriteApiTests
{
    private static BrowserRoutingRule IpifyRule => new(
        "ipify", "IPify", "api.ipify.org", BrowserRoutingContract.ExactHost,
        BrowserRoutingContract.RouteVpn, true, BrowserRoutingContract.SourceUser, null);

    private static BrowserRoutingRule ExampleDirect => new(
        "ex", "Example", "example.com", BrowserRoutingContract.ExactHost,
        BrowserRoutingContract.RouteDirect, true, BrowserRoutingContract.SourceUser, null);

    private static (BrowserRoutingStateStore Store, BrowserRoutingIpcDispatcher Dispatcher) Live()
    {
        var dir = new TempDir();
        var store = new BrowserRoutingStateStore(dir.File("state.json"));
        store.Load();
        var dispatcher = DispatcherTestFactory.Create(() => store.Current, writeStore: store);
        return (store, dispatcher);
    }

    [Fact]
    public void Upsert_new_rule_increments_revision_and_preserves_generation()
    {
        var (store, dispatcher) = Live();
        var generation = store.Current!.StateGeneration;
        var response = Json(dispatcher.Dispatch(Ipc.UpsertRule(0, RuleJson(IpifyRule))).Response);
        Assert.True(response.GetProperty("ok").GetBoolean());
        var result = response.GetProperty("result");
        Assert.Equal(generation, result.GetProperty("stateGeneration").GetString());
        Assert.Equal(1, result.GetProperty("revision").GetInt64());
        Assert.Equal(1, result.GetProperty("ruleCount").GetInt32());
    }

    [Fact]
    public void Upsert_update_existing_rule_increments_revision()
    {
        var (store, dispatcher) = Live();
        dispatcher.Dispatch(Ipc.UpsertRule(0, RuleJson(IpifyRule)));
        var updated = IpifyRule with { RouteMode = BrowserRoutingContract.RouteDirect };
        var response = Json(dispatcher.Dispatch(Ipc.UpsertRule(1, RuleJson(updated))).Response);
        Assert.Equal(2, response.GetProperty("result").GetProperty("revision").GetInt64());
    }

    [Fact]
    public void Stale_expectedRevision_returns_conflict_without_mutation()
    {
        var (store, dispatcher) = Live();
        dispatcher.Dispatch(Ipc.UpsertRule(0, RuleJson(IpifyRule)));
        var response = Json(dispatcher.Dispatch(Ipc.UpsertRule(0, RuleJson(ExampleDirect))).Response);
        Assert.False(response.GetProperty("ok").GetBoolean());
        Assert.Equal(BrowserRoutingIpcProtocol.Errors.RevisionConflict,
            response.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(1, response.GetProperty("error").GetProperty("currentRevision").GetInt64());
        Assert.Equal(1, store.Current!.Revision);
    }

    [Fact]
    public void Delete_existing_rule_increments_revision()
    {
        var (store, dispatcher) = Live();
        dispatcher.Dispatch(Ipc.UpsertRule(0, RuleJson(IpifyRule)));
        var response = Json(dispatcher.Dispatch(Ipc.DeleteRule(1, "ipify")).Response);
        Assert.True(response.GetProperty("ok").GetBoolean());
        Assert.Equal(2, response.GetProperty("result").GetProperty("revision").GetInt64());
        Assert.Equal(0, store.Current!.RuleCount);
    }

    [Fact]
    public void Delete_missing_rule_is_not_found_without_revision_bump()
    {
        var (store, dispatcher) = Live();
        var bytes = dispatcher.Dispatch(Ipc.DeleteRule(0, "missing")).Response;
        Assert.Equal(BrowserRoutingIpcProtocol.Errors.NotFound, Ipc.ErrorCode(bytes));
        Assert.Equal(0, store.Current!.Revision);
    }

    [Fact]
    public void ResetRules_clears_rules_and_increments_revision()
    {
        var (store, dispatcher) = Live();
        dispatcher.Dispatch(Ipc.UpsertRule(0, RuleJson(IpifyRule)));
        var response = Json(dispatcher.Dispatch(Ipc.ResetRules(1)).Response);
        Assert.Equal(2, response.GetProperty("result").GetProperty("revision").GetInt64());
        Assert.Equal(0, store.Current!.RuleCount);
        Assert.Equal(BrowserRoutingContract.RouteDirect, store.Current.State.DefaultRoute);
    }

    [Fact]
    public void ResetRules_on_empty_state_is_idempotent_no_revision_bump()
    {
        var (store, dispatcher) = Live();
        var response = Json(dispatcher.Dispatch(Ipc.ResetRules(0)).Response);
        Assert.True(response.GetProperty("ok").GetBoolean());
        Assert.Equal(0, response.GetProperty("result").GetProperty("revision").GetInt64());
        Assert.Equal(0, store.Current!.Revision);
    }

    [Fact]
    public void Restart_preserves_written_state()
    {
        using var dir = new TempDir();
        var path = dir.File("state.json");
        var store = new BrowserRoutingStateStore(path);
        store.Load();
        var dispatcher = DispatcherTestFactory.Create(() => store.Current, writeStore: store);
        dispatcher.Dispatch(Ipc.UpsertRule(0, RuleJson(IpifyRule)));
        var generation = store.Current!.StateGeneration;

        var reloaded = new BrowserRoutingStateStore(path);
        reloaded.Load();
        Assert.Equal(generation, reloaded.Current!.StateGeneration);
        Assert.Equal(1, reloaded.Current.Revision);
        Assert.Equal(1, reloaded.Current.RuleCount);
    }

    [Fact]
    public void Persistence_failure_does_not_publish_new_revision()
    {
        using var dir = new TempDir();
        var store = new BrowserRoutingStateStore(dir.File("state.json"));
        store.Load();
        store.BeforeCommitForTests = _ => throw new IOException("disk full");
        var dispatcher = DispatcherTestFactory.Create(() => store.Current, writeStore: store);
        var code = Ipc.ErrorCode(dispatcher.Dispatch(Ipc.UpsertRule(0, RuleJson(IpifyRule))).Response);
        Assert.Equal(BrowserRoutingIpcProtocol.Errors.PersistenceFailed, code);
        Assert.Equal(0, store.Current!.Revision);
    }

    [Fact]
    public void Concurrent_writers_same_revision_exactly_one_succeeds()
    {
        var (store, dispatcher) = Live();
        var t1 = Task.Run(() => dispatcher.Dispatch(Ipc.UpsertRule(0, RuleJson(IpifyRule))));
        var t2 = Task.Run(() => dispatcher.Dispatch(Ipc.UpsertRule(0, RuleJson(ExampleDirect))));
        Task.WaitAll(t1, t2);
        var codes = new[] { Ipc.ErrorCode(t1.Result.Response), Ipc.ErrorCode(t2.Result.Response) };
        Assert.Equal(1, codes.Count(c => c is null));
        Assert.Equal(1, codes.Count(c => c == BrowserRoutingIpcProtocol.Errors.RevisionConflict));
        Assert.Equal(1, store.Current!.Revision);
    }

    [Fact]
    public void Read_manifest_sees_new_revision_after_write()
    {
        var (store, dispatcher) = Live();
        dispatcher.Dispatch(Ipc.UpsertRule(0, RuleJson(IpifyRule)));
        var manifest = Json(dispatcher.Dispatch(Ipc.Manifest()).Response);
        Assert.Equal(1, manifest.GetProperty("result").GetProperty("revision").GetInt64());
        Assert.Equal(1, manifest.GetProperty("result").GetProperty("ruleCount").GetInt32());
    }

    [Theory]
    [InlineData(BrowserRoutingContract.ExactHost, BrowserRoutingContract.RouteVpn)]
    [InlineData(BrowserRoutingContract.DomainAndSubdomains, BrowserRoutingContract.RouteDirect)]
    [InlineData(BrowserRoutingContract.ExactHost, BrowserRoutingContract.RouteDefault)]
    public void Upsert_round_trips_match_and_route_modes(string matchType, string routeMode)
    {
        var (store, dispatcher) = Live();
        var rule = new BrowserRoutingRule("r1", "R1", "host.example.com", matchType, routeMode, false,
            BrowserRoutingContract.SourceUser, "note");
        Assert.Null(Ipc.ErrorCode(dispatcher.Dispatch(Ipc.UpsertRule(0, RuleJson(rule))).Response));
        var saved = store.Current!.State.Rules.Single();
        Assert.Equal(matchType, saved.MatchType);
        Assert.Equal(routeMode, saved.RouteMode);
        Assert.False(saved.Enabled);
    }

    [Fact]
    public void Invalid_host_returns_validation_failed()
    {
        var (store, dispatcher) = Live();
        var bad = new
        {
            id = "bad",
            name = "Bad",
            host = "NOT A HOST!!!",
            matchType = BrowserRoutingContract.ExactHost,
            routeMode = BrowserRoutingContract.RouteVpn,
            enabled = true,
            source = BrowserRoutingContract.SourceUser,
            notes = (string?)null,
        };
        Assert.Equal(BrowserRoutingIpcProtocol.Errors.ValidationFailed,
            Ipc.ErrorCode(dispatcher.Dispatch(Ipc.UpsertRule(0, bad)).Response));
    }

    [Fact]
    public void Manifest_advertises_browserRoutingWrite_capability()
    {
        var (store, dispatcher) = Live();
        var caps = Json(dispatcher.Dispatch(Ipc.Manifest()).Response).GetProperty("result").GetProperty("capabilities")
            .EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains("browserRoutingWrite", caps);
    }

    private static JsonElement Json(byte[] response) => Ipc.Parse(response);

    private static object RuleJson(BrowserRoutingRule rule) => new
    {
        id = rule.Id,
        name = rule.Name,
        host = rule.Host,
        matchType = rule.MatchType,
        routeMode = rule.RouteMode,
        enabled = rule.Enabled,
        source = rule.Source,
        notes = rule.Notes,
    };
}
