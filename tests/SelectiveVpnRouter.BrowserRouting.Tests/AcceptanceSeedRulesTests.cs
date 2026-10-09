using SelectiveVpnRouter.Core.BrowserRouting;
using VpnRoute.BrowserRoutingAcceptanceSeed;
using Xunit;

namespace SelectiveVpnRouter.BrowserRouting.Tests;

public class AcceptanceSeedRulesTests
{
    [Fact]
    public void Slice8_acceptance_rules_validate_and_use_distinct_hosts()
    {
        var rules = AcceptanceSeedRules.Create();
        Assert.Equal(2, rules.Count);
        var issues = BrowserRoutingValidator.ValidateRuleSet(rules);
        Assert.Empty(issues);

        var ipify = rules.Single(r => r.Id == AcceptanceSeedRules.IpifyRuleId);
        var example = rules.Single(r => r.Id == AcceptanceSeedRules.ExampleRuleId);
        Assert.Equal("api.ipify.org", ipify.Host);
        Assert.Equal(BrowserRoutingContract.RouteVpn, ipify.RouteMode);
        Assert.Equal("example.com", example.Host);
        Assert.Equal(BrowserRoutingContract.RouteDirect, example.RouteMode);
        Assert.Contains("Slice 8", ipify.Notes!, StringComparison.Ordinal);
    }

    [Fact]
    public void Seed_via_store_preserves_generation_and_bumps_revision()
    {
        using var dir = new TempDir();
        var path = dir.File("state.json");
        var store = new BrowserRoutingStateStore(path);
        store.Load();
        var generation = store.Current!.StateGeneration;
        Assert.Equal(0, store.Current.Revision);

        var next = store.Update(0, BrowserRoutingContract.RouteDirect, AcceptanceSeedRules.Create());
        Assert.Equal(generation, next.StateGeneration);
        Assert.Equal(1, next.Revision);
        Assert.Equal(2, next.RuleCount);

        var reload = new BrowserRoutingStateStore(path);
        reload.Load();
        Assert.Equal(generation, reload.Current!.StateGeneration);
        Assert.Equal(1, reload.Current.Revision);
    }
}
