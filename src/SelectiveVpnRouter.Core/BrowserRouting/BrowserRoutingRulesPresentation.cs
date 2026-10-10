namespace SelectiveVpnRouter.Core.BrowserRouting;

public enum BrowserRoutingListFilter
{
    All,
    Vpn,
    Direct,
    Disabled,
}

public static class BrowserRoutingRulesMessages
{
    public const string StateUnavailable =
        "Не удалось загрузить правила маршрутизации сайтов из службы VPN Route.";

    public const string Conflict =
        "Правила изменились. Список обновлён — проверьте изменения и сохраните ещё раз.";

    public const string Ambiguous =
        "Не удалось подтвердить результат. Обновляем состояние…";

    public const string NotFound =
        "Правило уже удалено. Список обновлён.";

    public const string Validation =
        "Проверьте поля правила и попробуйте снова.";
}

public static class BrowserRoutingRulesPresentation
{
    public sealed record RuleRow(
        BrowserRoutingRule Rule,
        string MatchTypeLabel,
        string RouteLabel);

    public static string LabelMatchType(string matchType) => matchType switch
    {
        BrowserRoutingContract.ExactHost => "Только этот домен",
        BrowserRoutingContract.DomainAndSubdomains => "Домен и поддомены",
        _ => matchType,
    };

    public static string FormatHostsDisplay(IReadOnlyList<string> hosts)
    {
        if (hosts.Count == 0)
            return "";
        if (hosts.Count == 1)
            return hosts[0];
        var extra = hosts.Count - 1;
        var suffix = extra == 1 ? "домен" : extra is >= 2 and <= 4 ? "домена" : "доменов";
        return hosts[0] + " + " + extra + " " + suffix;
    }

    public static string LabelRouteMode(string routeMode) => routeMode switch
    {
        BrowserRoutingContract.RouteVpn => "Через VPN",
        BrowserRoutingContract.RouteDirect => "Напрямую",
        BrowserRoutingContract.RouteDefault => "По умолчанию",
        _ => routeMode,
    };

    public static IReadOnlyList<RuleRow> FilterRules(
        IReadOnlyList<BrowserRoutingRule> rules,
        string? search,
        BrowserRoutingListFilter filter)
    {
        IEnumerable<BrowserRoutingRule> query = rules;
        string needle = (search ?? "").Trim();
        if (needle.Length > 0)
        {
            query = query.Where(r =>
                r.Name.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                r.Hosts.Any(h => h.Contains(needle, StringComparison.OrdinalIgnoreCase)));
        }

        query = filter switch
        {
            BrowserRoutingListFilter.Vpn => query.Where(r => r.Enabled && r.RouteMode == BrowserRoutingContract.RouteVpn),
            BrowserRoutingListFilter.Direct => query.Where(r => r.Enabled && r.RouteMode == BrowserRoutingContract.RouteDirect),
            BrowserRoutingListFilter.Disabled => query.Where(r => !r.Enabled),
            _ => query,
        };

        return query
            .OrderBy(r => r.Id, StringComparer.Ordinal)
            .Select(r => new RuleRow(r, LabelMatchType(r.MatchType), LabelRouteMode(r.RouteMode)))
            .ToList();
    }
}
