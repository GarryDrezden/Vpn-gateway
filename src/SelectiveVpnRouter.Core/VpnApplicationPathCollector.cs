namespace SelectiveVpnRouter.Core;

public static class VpnApplicationPathCollector
{
    public static IReadOnlyList<string> Collect(IEnumerable<RoutingRule> rules, bool paused, IEnumerable<string>? extraVpnExePaths = null)
    {
        if (paused)
        {
            return [];
        }

        IEnumerable<string> fromRules = RuleEvaluator.VpnApplicationRules(rules)
            .Select(r => Path.GetFullPath(r.Target));

        IEnumerable<string> extras = (extraVpnExePaths ?? [])
            .Where(static p => !string.IsNullOrWhiteSpace(p))
            .Select(Path.GetFullPath);

        return fromRules
            .Concat(extras)
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}