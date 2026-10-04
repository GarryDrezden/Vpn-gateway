namespace SelectiveVpnRouter.Core;

public static class VpnApplicationPathCollector
{
    public static IReadOnlyList<string> Collect(
        IEnumerable<RoutingRule> rules,
        bool paused,
        IEnumerable<string>? extraVpnExePaths = null,
        IPackagedApplicationPathResolver? packagedResolver = null)
    {
        return PackagedRoutingTargetResolver
            .EnumerateVpnWfpExecutablePaths(rules, paused, packagedResolver, extraVpnExePaths)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}