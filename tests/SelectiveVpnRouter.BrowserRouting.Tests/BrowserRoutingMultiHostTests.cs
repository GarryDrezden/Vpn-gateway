using SelectiveVpnRouter.Core.BrowserRouting;
using Xunit;

namespace SelectiveVpnRouter.BrowserRouting.Tests;

public class BrowserRoutingMultiHostTests
{
    [Fact]
    public void Legacy_host_json_migrates_to_hosts()
    {
        var issues = new List<BrowserRoutingIssue>();
        const string json = """
            {"id":"r1","name":"R","host":"example.com","matchType":"ExactHost","routeMode":"VPN","enabled":true,"source":"User","notes":null}
            """;
        var element = System.Text.Json.JsonDocument.Parse(json).RootElement;
        var rule = BrowserRoutingValidator.TryParseRule(element, "/rule", issues);
        Assert.NotNull(rule);
        Assert.Equal(["example.com"], rule!.Hosts);
    }

    [Fact]
    public void Hosts_array_round_trips_and_dedupes_on_write()
    {
        var issues = new List<BrowserRoutingIssue>();
        const string json = """
            {"id":"r1","name":"R","hosts":["a.com","a.com","b.com"],"matchType":"DomainAndSubdomains","routeMode":"VPN","enabled":true,"source":"User","notes":null}
            """;
        var element = System.Text.Json.JsonDocument.Parse(json).RootElement;
        var rule = BrowserRoutingValidator.TryParseRule(element, "/rule", issues);
        Assert.NotNull(rule);
        Assert.Equal(["a.com", "b.com"], rule!.Hosts);
    }

    [Fact]
    public void Enabled_rules_conflict_on_any_shared_host()
    {
        var rules = new[]
        {
            new BrowserRoutingRule("a", "A", ["x.com", "y.com"], BrowserRoutingContract.ExactHost,
                BrowserRoutingContract.RouteVpn, true, BrowserRoutingContract.SourceUser, null),
            new BrowserRoutingRule("b", "B", ["y.com"], BrowserRoutingContract.ExactHost,
                BrowserRoutingContract.RouteDirect, true, BrowserRoutingContract.SourceUser, null),
        };
        var issues = BrowserRoutingValidator.ValidateRuleSet(rules);
        Assert.Equal(BrowserRoutingValidator.Codes.ConflictingRules, Assert.Single(issues).Code);
    }

    [Fact]
    public void Format_hosts_display_shows_primary_plus_count()
    {
        Assert.Equal("youtube.com", BrowserRoutingRulesPresentation.FormatHostsDisplay(["youtube.com"]));
        Assert.Equal("youtube.com + 3 домена",
            BrowserRoutingRulesPresentation.FormatHostsDisplay(["youtube.com", "a.com", "b.com", "c.com"]));
    }

    [Fact]
    public void User_builder_accepts_multiline_domains()
    {
        var built = BrowserRoutingUserRuleBuilder.TryBuild(
            null,
            "Test",
            "Example.COM\r\nb.com\r\nb.com",
            BrowserRoutingContract.DomainAndSubdomains,
            BrowserRoutingContract.RouteVpn,
            true,
            null);
        Assert.True(built.Ok);
        Assert.Equal(["example.com", "b.com"], built.Rule!.Hosts);
    }
}
