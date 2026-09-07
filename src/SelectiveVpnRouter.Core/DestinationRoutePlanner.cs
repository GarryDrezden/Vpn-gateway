using System.Net;

namespace SelectiveVpnRouter.Core;

public static class DestinationRoutePlanner
{
    public static IReadOnlyList<OwnedRoute> PlanVpnDestinations(
        IEnumerable<RoutingRule> rules,
        int vpnIfIndex,
        string gateway,
        IReadOnlyDictionary<string, IReadOnlyList<IPAddress>> resolved)
    {
        var desired = new List<OwnedRoute>();
        foreach (RoutingRule rule in RuleEvaluator.VpnDestinationRules(rules))
        {
            if (rule.Type == RuleType.Cidr && CidrMatcher.TryParse(rule.Target, out IPNetwork net)
                && net.BaseAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                desired.Add(RouteReconciler.HostRoute(
                    net.BaseAddress.ToString() + "/" + net.PrefixLength,
                    vpnIfIndex,
                    gateway,
                    "cidr:" + rule.Target));
                continue;
            }

            if (rule.Type == RuleType.Domain && resolved.TryGetValue(DomainWildcard.NormalizeHost(StripWildcard(rule.Target)), out IReadOnlyList<IPAddress>? addrs))
            {
                foreach (IPAddress ip in addrs.Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork))
                {
                    if (IsDirectDestination(rules, ip, rule.Target))
                    {
                        continue;
                    }

                    desired.Add(RouteReconciler.HostRoute(ip.ToString(), vpnIfIndex, gateway, "domain:" + rule.Target));
                }
            }
        }

        return desired
            .GroupBy(r => r.DestinationPrefix + "|" + r.InterfaceIndex + "|" + r.NextHop, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    public static string StripWildcard(string pattern)
    {
        string p = DomainWildcard.NormalizeHost(pattern);
        return p.StartsWith("*.", StringComparison.Ordinal) ? p[2..] : p;
    }

    private static bool IsDirectDestination(IEnumerable<RoutingRule> rules, IPAddress ip, string hostHint)
        => RuleEvaluator.MatchDestination(rules, hostHint, ip) is { Mode: RouteMode.Direct };
}
