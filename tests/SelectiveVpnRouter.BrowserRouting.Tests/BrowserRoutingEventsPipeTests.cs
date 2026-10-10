using System.Buffers.Binary;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;
using SelectiveVpnRouter.Core.BrowserRouting;
using Xunit;

namespace SelectiveVpnRouter.BrowserRouting.Tests;

public sealed class BrowserRoutingEventsPipeTests : IAsyncLifetime
{
    private readonly TempDir _dir = new();
    private readonly string _pipeName = "SelectiveVpnRouter.BrowserRouting.Events.Test." + Guid.NewGuid().ToString("N");
    private readonly CancellationTokenSource _stop = new();
    private readonly BrowserRoutingChangeNotifier _notifier = new();
    private BrowserRoutingStateStore _store = null!;
    private Task? _server;

    public Task InitializeAsync()
    {
        _store = new BrowserRoutingStateStore(_dir.File("state.json"), changeNotifier: _notifier);
        _store.Load();
        var server = new BrowserRoutingEventsPipeServer(_pipeName, () => _store.Current, _notifier, _ => { });
        _server = Task.Run(() => server.RunAsync(_stop.Token));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _stop.CancelAsync();
        try { await _server!; } catch (OperationCanceledException) { }
        _stop.Dispose();
    }

    private async Task<NamedPipeClientStream> ConnectAsync()
    {
        var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous,
            TokenImpersonationLevel.Identification);
        await pipe.ConnectAsync(5000);
        return pipe;
    }

    private static async Task WriteFrameAsync(Stream pipe, byte[] body)
    {
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, body.Length);
        await pipe.WriteAsync(header);
        await pipe.WriteAsync(body);
        await pipe.FlushAsync();
    }

    private static async Task<byte[]> ReadFrameAsync(Stream pipe)
    {
        var header = new byte[4];
        await pipe.ReadExactlyAsync(header);
        var body = new byte[BinaryPrimitives.ReadInt32LittleEndian(header)];
        await pipe.ReadExactlyAsync(body);
        return body;
    }

    [Fact]
    public async Task Commit_emits_browserRoutingChanged_notification()
    {
        using var dir = new TempDir();
        var notifier = new BrowserRoutingChangeNotifier();
        var store = new BrowserRoutingStateStore(dir.File("state.json"), changeNotifier: notifier);
        var events = new List<BrowserRoutingChangeNotifier.ChangeEvent>();
        using var _ = notifier.Subscribe(events.Add);
        store.Load();
        store.UpsertRule(0, Rules.Make(1));
        await Task.Delay(100);
        var evt = Assert.Single(events);
        Assert.Equal(BrowserRoutingChangeNotifier.BrowserRoutingChanged, evt.Type);
        Assert.Equal(store.Current!.StateGeneration, evt.StateGeneration);
        Assert.Equal(1, evt.Revision);
    }

    [Fact]
    public void Validation_failure_does_not_emit_notification()
    {
        using var dir = new TempDir();
        var notifier = new BrowserRoutingChangeNotifier();
        var store = new BrowserRoutingStateStore(dir.File("state.json"), changeNotifier: notifier);
        var dispatcher = DispatcherTestFactory.Create(() => store.Current, writeStore: store);
        var events = new List<BrowserRoutingChangeNotifier.ChangeEvent>();
        using var _ = notifier.Subscribe(events.Add);
        store.Load();
        var bad = new { id = "x", name = "X", host = "bad host", matchType = "ExactHost", routeMode = "VPN", enabled = true, source = "User", notes = (string?)null };
        dispatcher.Dispatch(Ipc.UpsertRule(0, bad));
        Assert.Empty(events);
    }

    [Fact]
    public async Task Persistence_failure_does_not_emit_notification()
    {
        using var dir = new TempDir();
        var notifier = new BrowserRoutingChangeNotifier();
        var store = new BrowserRoutingStateStore(dir.File("state.json"), changeNotifier: notifier)
        {
            BeforeCommitForTests = _ => throw new IOException("simulated")
        };
        var dispatcher = DispatcherTestFactory.Create(() => store.Current, writeStore: store);
        var events = new List<BrowserRoutingChangeNotifier.ChangeEvent>();
        using var _ = notifier.Subscribe(events.Add);
        store.Load();
        var code = Ipc.ErrorCode(dispatcher.Dispatch(Ipc.UpsertRule(0, RuleJson(Rules.Make(1)))).Response);
        Assert.True(code is BrowserRoutingIpcProtocol.Errors.PersistenceFailed or BrowserRoutingIpcProtocol.Errors.BrowserStateUnavailable);
        await Task.Delay(100);
        Assert.Empty(events);
    }

    [Fact]
    public async Task Conflict_does_not_emit_notification()
    {
        using var dir = new TempDir();
        var notifier = new BrowserRoutingChangeNotifier();
        var store = new BrowserRoutingStateStore(dir.File("state.json"), changeNotifier: notifier);
        var dispatcher = DispatcherTestFactory.Create(() => store.Current, writeStore: store);
        var events = new List<BrowserRoutingChangeNotifier.ChangeEvent>();
        using var _ = notifier.Subscribe(events.Add);
        store.Load();
        dispatcher.Dispatch(Ipc.UpsertRule(0, RuleJson(Rules.Make(1))));
        await Task.Delay(100);
        events.Clear();
        dispatcher.Dispatch(Ipc.UpsertRule(0, RuleJson(Rules.Make(2))));
        await Task.Delay(100);
        Assert.Empty(events);
    }

    [Fact]
    public void Reset_no_op_does_not_emit_notification()
    {
        using var dir = new TempDir();
        var notifier = new BrowserRoutingChangeNotifier();
        var store = new BrowserRoutingStateStore(dir.File("state.json"), changeNotifier: notifier);
        var dispatcher = DispatcherTestFactory.Create(() => store.Current, writeStore: store);
        var events = new List<BrowserRoutingChangeNotifier.ChangeEvent>();
        using var _ = notifier.Subscribe(events.Add);
        store.Load();
        dispatcher.Dispatch(Ipc.ResetRules(0));
        Assert.Empty(events);
    }

    [Fact]
    public async Task Commits_emit_monotonic_revisions()
    {
        using var dir = new TempDir();
        var notifier = new BrowserRoutingChangeNotifier();
        var store = new BrowserRoutingStateStore(dir.File("state.json"), changeNotifier: notifier);
        var events = new List<BrowserRoutingChangeNotifier.ChangeEvent>();
        using var _ = notifier.Subscribe(events.Add);
        store.Load();
        store.UpsertRule(0, Rules.Make(1));
        store.UpsertRule(1, Rules.Make(2));
        store.DeleteRule(2, store.Current!.State.Rules[0].Id);
        await Task.Delay(100);
        Assert.Equal([1L, 2L, 3L], events.Select(e => e.Revision).ToArray());
    }

    [Fact]
    public async Task Subscribe_receives_serviceAvailable_and_push_events()
    {
        await using var pipe = await ConnectAsync();
        await WriteFrameAsync(pipe, Ipc.SubscribeEvents("sub-1"));
        var ack = JsonDocument.Parse(await ReadFrameAsync(pipe)).RootElement;
        Assert.True(ack.GetProperty("ok").GetBoolean());
        Assert.Equal("sub-1", ack.GetProperty("id").GetString());

        var available = JsonDocument.Parse(await ReadFrameAsync(pipe)).RootElement;
        Assert.Equal(BrowserRoutingEventsIpcProtocol.EventTypes.ServiceAvailable, available.GetProperty("type").GetString());

        _store.UpsertRule(_store.Current!.Revision, Rules.Make(9));
        await Task.Delay(200);
        var changed = JsonDocument.Parse(await ReadFrameAsync(pipe)).RootElement;
        Assert.Equal(BrowserRoutingEventsIpcProtocol.EventTypes.BrowserRoutingChanged, changed.GetProperty("type").GetString());
        Assert.Equal(_store.Current.Revision, changed.GetProperty("revision").GetInt64());
    }

    private static object RuleJson(BrowserRoutingRule rule) => new
    {
        id = rule.Id,
        name = rule.Name,
        hosts = rule.Hosts,
        matchType = rule.MatchType,
        routeMode = rule.RouteMode,
        enabled = rule.Enabled,
        source = rule.Source,
        notes = rule.Notes
    };
}
