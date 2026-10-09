using SelectiveVpnRouter.Core;
using SelectiveVpnRouter.Core.BrowserRouting;
using Xunit;

namespace SelectiveVpnRouter.BrowserRouting.Tests;

public class BrowserRoutingAppSessionTests
{
    private static readonly BrowserRoutingRule Rule = new(
        "rule-abc1234567890ab", "Sample", "example.com", BrowserRoutingContract.ExactHost,
        BrowserRoutingContract.RouteVpn, true, BrowserRoutingContract.SourceUser, null);

    [Fact]
    public async Task Z_load_maps_available_snapshot()
    {
        var transport = new FakeBrowserRoutingTransport();
        transport.EnqueueOk(IpcMethods.GetBrowserRoutingSnapshot, new BrowserRoutingControlSnapshot
        {
            Available = true,
            StateGeneration = "75f5c435-6ac5-45a5-876b-042a6376635e",
            Revision = 0,
            DefaultRoute = BrowserRoutingContract.RouteDirect,
            Rules = [],
        });
        var session = new BrowserRoutingAppSession(transport);
        BrowserRoutingAppLoadResult load = await session.LoadAsync(CancellationToken.None);
        Assert.True(load.Ok);
        Assert.NotNull(load.Snapshot);
    }

    [Fact]
    public async Task AA_conflict_reloads_without_retry()
    {
        var transport = new FakeBrowserRoutingTransport();
        transport.EnqueueFail(IpcMethods.UpsertBrowserRule, BrowserRoutingIpcProtocol.Errors.RevisionConflict);
        transport.EnqueueOk(IpcMethods.GetBrowserRoutingSnapshot, Snapshot(revision: 2, rules: [Rule with { Name = "Changed" }]));

        var session = new BrowserRoutingAppSession(transport);
        BrowserRoutingAppMutationResult result = await session.UpsertAsync(Rule, expectedRevision: 0, CancellationToken.None);
        Assert.False(result.Ok);
        Assert.True(result.Conflict);
        Assert.Equal(BrowserRoutingRulesMessages.Conflict, result.UserMessage);
        Assert.Equal(2, result.Snapshot!.Revision);
        Assert.Single(transport.Calls.Where(c => c.Method == IpcMethods.UpsertBrowserRule));
    }

    [Fact]
    public async Task AB_success_reloads_authoritative_snapshot()
    {
        var transport = new FakeBrowserRoutingTransport();
        transport.EnqueueOk(IpcMethods.UpsertBrowserRule, new BrowserRoutingMutationSummary
        {
            StateGeneration = "75f5c435-6ac5-45a5-876b-042a6376635e",
            Revision = 1,
            DefaultRoute = BrowserRoutingContract.RouteDirect,
            RuleCount = 1,
        });
        transport.EnqueueOk(IpcMethods.GetBrowserRoutingSnapshot, Snapshot(revision: 1, rules: [Rule]));

        var session = new BrowserRoutingAppSession(transport);
        BrowserRoutingAppMutationResult result = await session.UpsertAsync(Rule, 0, CancellationToken.None);
        Assert.True(result.Ok);
        Assert.Single(result.Snapshot!.Rules);
        Assert.Single(transport.Calls.Where(c => c.Method == IpcMethods.GetBrowserRoutingSnapshot));
    }

    [Fact]
    public async Task AC_timeout_on_write_is_ambiguous_and_reloads()
    {
        var transport = new FakeBrowserRoutingTransport();
        transport.EnqueueThrow(IpcMethods.UpsertBrowserRule, new IpcTimeoutException(IpcMethods.UpsertBrowserRule, 1000));
        transport.EnqueueOk(IpcMethods.GetBrowserRoutingSnapshot, Snapshot(revision: 3, rules: []));

        var session = new BrowserRoutingAppSession(transport);
        BrowserRoutingAppMutationResult result = await session.UpsertAsync(Rule, 2, CancellationToken.None);
        Assert.True(result.Ambiguous);
        Assert.Equal(BrowserRoutingRulesMessages.Ambiguous, result.UserMessage);
        Assert.Equal(3, result.Snapshot!.Revision);
    }

    private static BrowserRoutingControlSnapshot Snapshot(long revision, BrowserRoutingRule[] rules) =>
        new()
        {
            Available = true,
            StateGeneration = "75f5c435-6ac5-45a5-876b-042a6376635e",
            Revision = revision,
            DefaultRoute = BrowserRoutingContract.RouteDirect,
            Rules = rules,
        };

    private sealed class FakeBrowserRoutingTransport : IBrowserRoutingControlTransport
    {
        private readonly Queue<Func<string, IpcResponse>> _responses = new();
        public List<(string Method, object? Payload)> Calls { get; } = [];

        public void EnqueueOk(string method, object payload) =>
            _responses.Enqueue(m => new IpcResponse
            {
                Id = "1",
                Ok = true,
                PayloadJson = System.Text.Json.JsonSerializer.Serialize(payload, ConfigSerializer.JsonOptions),
            });

        public void EnqueueFail(string method, string code) =>
            _responses.Enqueue(m => new IpcResponse { Id = "1", Ok = false, Error = code });

        public void EnqueueThrow(string method, Exception ex) =>
            _responses.Enqueue(_ => throw ex);

        public Task<IpcResponse> InvokeAsync(string method, object? payload, CancellationToken cancellationToken)
        {
            Calls.Add((method, payload));
            if (_responses.Count == 0)
                throw new InvalidOperationException("No fake response for " + method);
            return Task.FromResult(_responses.Dequeue()(method));
        }
    }
}
