using SelectiveVpnRouter.Core.BrowserRouting;
using Xunit;

namespace SelectiveVpnRouter.BrowserRouting.Tests;

public class BrowserRoutingRulesPresentationTests
{
    private static BrowserRoutingRule Rule(string id, string name, string host, string route, bool enabled = true) =>
        new(id, name, host, BrowserRoutingContract.ExactHost, route, enabled, BrowserRoutingContract.SourceUser, null);

    [Fact]
    public void U_match_and_route_labels()
    {
        Assert.Equal("Только этот домен",
            BrowserRoutingRulesPresentation.LabelMatchType(BrowserRoutingContract.ExactHost));
        Assert.Equal("Домен и поддомены",
            BrowserRoutingRulesPresentation.LabelMatchType(BrowserRoutingContract.DomainAndSubdomains));
        Assert.Equal("Через VPN", BrowserRoutingRulesPresentation.LabelRouteMode(BrowserRoutingContract.RouteVpn));
        Assert.Equal("Напрямую", BrowserRoutingRulesPresentation.LabelRouteMode(BrowserRoutingContract.RouteDirect));
    }

    [Fact]
    public void V_search_filters_name_and_host()
    {
        var rules = new[]
        {
            Rule("a", "GitHub", "github.com", BrowserRoutingContract.RouteVpn),
            Rule("b", "Example", "example.org", BrowserRoutingContract.RouteDirect),
        };
        var filtered = BrowserRoutingRulesPresentation.FilterRules(rules, "git", BrowserRoutingListFilter.All);
        Assert.Single(filtered);
        Assert.Equal("a", filtered[0].Rule.Id);
    }

    [Fact]
    public void W_route_and_disabled_filters()
    {
        var rules = new[]
        {
            Rule("a", "A", "a.com", BrowserRoutingContract.RouteVpn),
            Rule("b", "B", "b.com", BrowserRoutingContract.RouteDirect),
            Rule("c", "C", "c.com", BrowserRoutingContract.RouteVpn, enabled: false),
        };
        Assert.Single(BrowserRoutingRulesPresentation.FilterRules(rules, null, BrowserRoutingListFilter.Vpn));
        Assert.Single(BrowserRoutingRulesPresentation.FilterRules(rules, null, BrowserRoutingListFilter.Direct));
        Assert.Single(BrowserRoutingRulesPresentation.FilterRules(rules, null, BrowserRoutingListFilter.Disabled));
    }
}
