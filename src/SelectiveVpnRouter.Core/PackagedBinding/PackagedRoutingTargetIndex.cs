namespace SelectiveVpnRouter.Core;

public sealed class PackagedRoutingTargetIndex
{
    private readonly Dictionary<string, (RoutingRule Rule, string PrimaryExecutablePath)> _pathToLogicalRule;

    private PackagedRoutingTargetIndex(Dictionary<string, (RoutingRule, string)> pathToLogicalRule)
    {
        _pathToLogicalRule = pathToLogicalRule;
    }

    public static PackagedRoutingTargetIndex Build(
        IEnumerable<RoutingRule> rules,
        IPackagedApplicationPathResolver? packagedResolver)
    {
        var map = new Dictionary<string, (RoutingRule, string)>(StringComparer.OrdinalIgnoreCase);
        foreach (PackagedRoutingTargetsForRule plan in PackagedRoutingTargetResolver.ResolveAllVpnApplicationRules(rules, packagedResolver))
        {
            foreach (PackagedRoutingTarget target in plan.Targets.Where(t => t.Resolved && !string.IsNullOrWhiteSpace(t.ExecutablePath)))
            {
                map[target.ExecutablePath] = (plan.Rule, plan.PrimaryExecutablePath);
            }
        }

        return new PackagedRoutingTargetIndex(map);
    }

    public bool TryGetLogicalRule(string processPath, out RoutingRule? rule, out string? primaryExecutablePath)
    {
        rule = null;
        primaryExecutablePath = null;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            return false;
        }

        string normalized;
        try
        {
            normalized = ApplicationRulesHelper.NormalizeExePath(processPath);
        }
        catch (Exception)
        {
            return false;
        }

        if (!_pathToLogicalRule.TryGetValue(normalized, out (RoutingRule Rule, string PrimaryExecutablePath) hit))
        {
            return false;
        }

        rule = hit.Rule;
        primaryExecutablePath = hit.PrimaryExecutablePath;
        return true;
    }

    public string NormalizeConnectionsGroupKey(string processPath)
    {
        if (TryGetLogicalRule(processPath, out _, out string? primary) && !string.IsNullOrWhiteSpace(primary))
        {
            return primary;
        }

        try
        {
            return ApplicationRulesHelper.NormalizeExePath(processPath);
        }
        catch (Exception)
        {
            return processPath;
        }
    }
}
