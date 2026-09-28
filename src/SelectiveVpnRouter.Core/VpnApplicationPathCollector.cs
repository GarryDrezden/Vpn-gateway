namespace SelectiveVpnRouter.Core;

public static class VpnApplicationPathCollector
{
    public static IReadOnlyList<string> Collect(IEnumerable<RoutingRule> rules, bool paused)
    {
        if (paused)
        {
            return [];
        }

        return RuleEvaluator.VpnApplicationRules(rules)
            .Select(r => Path.GetFullPath(r.Target))
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}