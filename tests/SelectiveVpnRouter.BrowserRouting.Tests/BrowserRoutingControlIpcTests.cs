using SelectiveVpnRouter.Core.BrowserRouting;
using Xunit;

namespace SelectiveVpnRouter.BrowserRouting.Tests;

public class BrowserRoutingControlIpcTests
{
    private static BrowserRoutingRule Sample => new(
        "rule-abc1234567890ab", "Sample", "example.com", BrowserRoutingContract.ExactHost,
        BrowserRoutingContract.RouteVpn, true, BrowserRoutingContract.SourceUser, null);

    [Fact]
    public void X_control_ipc_upsert_and_snapshot()
    {
        using var dir = new TempDir();
        var store = new BrowserRoutingStateStore(dir.File("state.json"));
        store.Load();
        var summary = BrowserRoutingControlIpc.Upsert(store, new UpsertBrowserRuleRequest
        {
            ExpectedRevision = 0,
            Rule = Sample,
        });
        Assert.Equal(1, summary.Revision);
        var snap = BrowserRoutingControlIpc.ReadSnapshot(store);
        Assert.True(snap.Available);
        Assert.Single(snap.Rules);
    }

    [Fact]
    public void Y_control_ipc_revision_conflict_code()
    {
        using var dir = new TempDir();
        var store = new BrowserRoutingStateStore(dir.File("state.json"));
        store.Load();
        BrowserRoutingControlIpc.Upsert(store, new UpsertBrowserRuleRequest { ExpectedRevision = 0, Rule = Sample });
        var ex = Assert.Throws<BrowserRoutingControlException>(() =>
            BrowserRoutingControlIpc.Upsert(store, new UpsertBrowserRuleRequest
            {
                ExpectedRevision = 0,
                Rule = Sample with { Name = "Other" },
            }));
        Assert.Equal(BrowserRoutingIpcProtocol.Errors.RevisionConflict, ex.Code);
    }
}
