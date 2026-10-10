using SelectiveVpnRouter.Core.BrowserRouting;

namespace SelectiveVpnRouter.App;

public sealed class BrowserRoutingListRow
{
    public required BrowserRoutingRule Rule { get; init; }
    public required string MatchTypeLabel { get; init; }
    public required string RouteLabel { get; init; }
    public string Name => Rule.Name;
    public string Host => BrowserRoutingRulesPresentation.FormatHostsDisplay(Rule.Hosts);
    public bool Enabled => Rule.Enabled;
}
