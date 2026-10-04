using System.Net;

namespace SelectiveVpnRouter.Core;

public sealed record ProcessMatch(RoutingRule? Rule, RouteMode EffectiveMode)
{
    public static ProcessMatch None { get; } = new(null, RouteMode.Direct);
}

public static class RuleEvaluator
{
    /// <summary>
    /// Application rules are true per-process: only the executable that creates
    /// the socket is matched. Parent/child is NOT inherited.
    /// Explicit DIRECT application rules beat VPN application rules.
    /// Domain/CIDR rules are destination-global and must not be mixed into this result.
    /// </summary>
    public static ProcessMatch MatchProcess(
        IEnumerable<RoutingRule> rules,
        string processPath,
        PackagedRoutingTargetIndex? packagedRoutingIndex = null)
    {
        string normalized = ExecutablePathNormalizer.Normalize(processPath);
        if (normalized.Length == 0)
        {
            return ProcessMatch.None;
        }

        List<RoutingRule> apps = rules
            .Where(r => r.Enabled && r.Type == RuleType.Application)
            .Where(r => AppTargetMatches(r.Target, normalized))
            .ToList();

        RoutingRule? direct = apps.FirstOrDefault(r => r.Mode == RouteMode.Direct);
        if (direct is not null)
        {
            return new ProcessMatch(direct, RouteMode.Direct);
        }

        RoutingRule? vpn = apps.FirstOrDefault(r => r.Mode == RouteMode.Vpn);
        if (vpn is not null)
        {
            return new ProcessMatch(vpn, RouteMode.Vpn);
        }

        if (packagedRoutingIndex?.TryGetLogicalRule(normalized, out RoutingRule? logicalRule, out _) == true
            && logicalRule is { Enabled: true, Type: RuleType.Application })
        {
            return new ProcessMatch(logicalRule, logicalRule.Mode);
        }

        return ProcessMatch.None;
    }

    public static bool AppTargetMatches(string ruleTarget, string processPath)
    {
        string target = ExecutablePathNormalizer.Normalize(ruleTarget);
        string process = ExecutablePathNormalizer.Normalize(processPath);
        if (target.Length == 0 || process.Length == 0)
        {
            return false;
        }

        if (target.Contains('\\'))
        {
            return string.Equals(target, process, StringComparison.Ordinal);
        }

        return string.Equals(Path.GetFileName(target), Path.GetFileName(process), StringComparison.Ordinal);
    }

    /// <summary>
    /// Destination rules apply to every process that connects to the IP/host.
    /// UI must label them as global-for-destination.
    /// </summary>
    public static RoutingRule? MatchDestination(IEnumerable<RoutingRule> rules, string? host, IPAddress? ip)
    {
        List<RoutingRule> dest = rules.Where(r => r.Enabled && r.Type is RuleType.Domain or RuleType.Cidr).ToList();

        if (!string.IsNullOrWhiteSpace(host))
        {
            RoutingRule? domainDirect = dest.FirstOrDefault(r => r.Type == RuleType.Domain && r.Mode == RouteMode.Direct && DomainWildcard.Matches(r.Target, host));
            if (domainDirect is not null)
            {
                return domainDirect;
            }

            RoutingRule? domainVpn = dest.FirstOrDefault(r => r.Type == RuleType.Domain && r.Mode == RouteMode.Vpn && DomainWildcard.Matches(r.Target, host));
            if (domainVpn is not null)
            {
                return domainVpn;
            }
        }

        if (ip is not null)
        {
            RoutingRule? cidrDirect = dest.FirstOrDefault(r => r.Type == RuleType.Cidr && r.Mode == RouteMode.Direct && CidrMatcher.Contains(r.Target, ip));
            if (cidrDirect is not null)
            {
                return cidrDirect;
            }

            RoutingRule? cidrVpn = dest.FirstOrDefault(r => r.Type == RuleType.Cidr && r.Mode == RouteMode.Vpn && CidrMatcher.Contains(r.Target, ip));
            if (cidrVpn is not null)
            {
                return cidrVpn;
            }
        }

        return null;
    }

    public static IReadOnlyList<RoutingRule> VpnApplicationRules(IEnumerable<RoutingRule> rules)
        => rules.Where(r => r.Enabled && r.Type == RuleType.Application && r.Mode == RouteMode.Vpn).ToList();

    public static IReadOnlyList<RoutingRule> VpnDestinationRules(IEnumerable<RoutingRule> rules)
        => rules.Where(r => r.Enabled && r.Type is RuleType.Domain or RuleType.Cidr && r.Mode == RouteMode.Vpn).ToList();
}
