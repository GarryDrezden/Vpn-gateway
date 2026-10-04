namespace SelectiveVpnRouter.Core;

public sealed record PackagedApplicationRuleRebindResult
{
    public required AppConfiguration Config { get; init; }
    public bool Changed { get; init; }
    public int ReboundRuleCount { get; init; }
}

public static class PackagedApplicationRuleRebinder
{
    public static PackagedApplicationRuleRebindResult TryRebind(
        AppConfiguration config,
        IPackagedApplicationPathResolver resolver,
        Action<string>? log = null)
    {
        List<RoutingRule> rules = config.Rules.ToList();
        bool changed = false;
        int reboundCount = 0;

        for (int i = 0; i < rules.Count; i++)
        {
            RoutingRule rule = rules[i];
            if (rule.Type != RuleType.Application || rule.PackagedBinding is null)
            {
                continue;
            }

            PackagedApplicationPathResolveResult resolved = resolver.Resolve(rule.PackagedBinding);
            if (!resolved.Found || string.IsNullOrWhiteSpace(resolved.ResolvedExecutablePath))
            {
                continue;
            }

            string normalizedTarget;
            string normalizedResolved;
            try
            {
                normalizedTarget = ApplicationRulesHelper.NormalizeExePath(rule.Target);
                normalizedResolved = ApplicationRulesHelper.NormalizeExePath(resolved.ResolvedExecutablePath);
            }
            catch (Exception)
            {
                continue;
            }

            PackagedApplicationBinding updatedBinding = rule.PackagedBinding with
            {
                RelativeExecutablePath = resolved.RelativeExecutablePath ?? rule.PackagedBinding.RelativeExecutablePath,
                ResolvedPackageFullName = resolved.PackageFullName ?? rule.PackagedBinding.ResolvedPackageFullName,
            };

            if (string.Equals(normalizedTarget, normalizedResolved, StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    rule.PackagedBinding.ResolvedPackageFullName,
                    updatedBinding.ResolvedPackageFullName,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            rules[i] = rule with
            {
                Target = normalizedResolved,
                PackagedBinding = updatedBinding,
            };
            changed = true;
            reboundCount++;
            log?.Invoke(
                FormattableString.Invariant(
                    $"Packaged app rule rebound: {normalizedTarget} -> {normalizedResolved} ({rule.Name})"));
        }

        return new PackagedApplicationRuleRebindResult
        {
            Config = changed ? config with { Rules = rules } : config,
            Changed = changed,
            ReboundRuleCount = reboundCount,
        };
    }
}
