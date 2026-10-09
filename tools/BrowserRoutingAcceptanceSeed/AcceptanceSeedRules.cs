using SelectiveVpnRouter.Core.BrowserRouting;

namespace VpnRoute.BrowserRoutingAcceptanceSeed;

/// <summary>Deterministic Slice 8 acceptance rules — ids/names/notes are clearly non-production.</summary>
public static class AcceptanceSeedRules
{
    public const string IpifyRuleId = "slice8-acceptance-ipify-vpn";
    public const string ExampleRuleId = "slice8-acceptance-example-direct";
    private const string Notes =
        "Slice 8 manual acceptance seed (vpn-gateway tools/BrowserRoutingAcceptanceSeed). Not a user rule.";

    public static IReadOnlyList<BrowserRoutingRule> Create() =>
    [
        new(
            IpifyRuleId,
            "Acceptance: api.ipify.org VPN",
            "api.ipify.org",
            BrowserRoutingContract.ExactHost,
            BrowserRoutingContract.RouteVpn,
            true,
            BrowserRoutingContract.SourceUser,
            Notes),
        new(
            ExampleRuleId,
            "Acceptance: example.com Direct",
            "example.com",
            BrowserRoutingContract.ExactHost,
            BrowserRoutingContract.RouteDirect,
            true,
            BrowserRoutingContract.SourceUser,
            Notes)
    ];
}
