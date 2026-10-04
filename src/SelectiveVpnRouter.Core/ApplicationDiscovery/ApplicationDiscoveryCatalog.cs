namespace SelectiveVpnRouter.Core.ApplicationDiscovery;

public static class ApplicationDiscoveryCatalog
{
    public static IReadOnlyList<DiscoveredApplication> BuildInstalledCatalog(
        IEnumerable<DiscoveredApplicationCandidate> candidates,
        IEnumerable<RoutingRule> existingRules,
        string? searchTerm = null,
        ApplicationDiscoveryViewOptions? options = null)
    {
        IReadOnlyList<DiscoveredApplication> merged = ApplicationDiscoveryMerger.Merge(candidates);
        IReadOnlyList<DiscoveredApplication> filtered = ApplicationDiscoveryFilter.FilterInstalled(merged, options);
        IReadOnlyList<DiscoveredApplication> enriched = AttachExistingRouteModes(filtered, existingRules);
        return ApplicationDiscoverySearch.Filter(enriched, searchTerm);
    }

    public static IReadOnlyList<DiscoveredApplication> BuildRunningCatalog(
        IEnumerable<DiscoveredApplicationCandidate> candidates,
        IEnumerable<RoutingRule> existingRules,
        string? searchTerm = null,
        ApplicationDiscoveryViewOptions? options = null)
    {
        IReadOnlyList<DiscoveredApplication> merged = ApplicationDiscoveryMerger.Merge(candidates);
        IReadOnlyList<DiscoveredApplication> enriched = AttachExistingRouteModes(merged, existingRules);
        IReadOnlyList<DiscoveredApplication> filtered = ApplicationDiscoveryFilter.FilterRunning(enriched, options);
        return ApplicationDiscoverySearch.Filter(filtered, searchTerm);
    }

    public static IReadOnlyList<DiscoveredApplication> AttachExistingRouteModes(
        IEnumerable<DiscoveredApplication> applications,
        IEnumerable<RoutingRule> existingRules)
    {
        List<RoutingRule> permanentRules = existingRules
            .Where(ApplicationRulesHelper.IsPersistentUserApplicationRule)
            .ToList();

        return applications
            .Select(app =>
            {
                RoutingRule? rule = ApplicationRulesHelper.FindApplicationRuleForDiscoveredApp(permanentRules, app);
                return rule is null
                    ? app
                    : app with { ExistingRouteMode = rule.Mode };
            })
            .ToList();
    }

    public static ApplicationDiscoveryBatchAddResult AddSelectedRules(
        AppConfiguration config,
        IEnumerable<DiscoveredApplication> selected,
        RouteMode mode)
    {
        AppConfiguration current = config;
        int added = 0;
        int skippedDuplicate = 0;
        int skippedAlreadyConfigured = 0;
        List<RoutingRule> newRules = [];

        foreach (DiscoveredApplication app in selected)
        {
            if (app.IsAlreadyConfigured)
            {
                skippedAlreadyConfigured++;
                continue;
            }

            ApplicationRuleAddResult addResult = ApplicationRulesHelper.TryAddApplicationRuleFromDiscovery(current, app, mode);
            if (addResult.IsDuplicate)
            {
                skippedDuplicate++;
                continue;
            }

            if (!addResult.Ok || addResult.Config is null)
            {
                continue;
            }

            current = addResult.Config;
            added++;
            if (addResult.Rule is not null)
            {
                newRules.Add(addResult.Rule);
            }
        }

        return new ApplicationDiscoveryBatchAddResult
        {
            Config = current,
            AddedCount = added,
            SkippedDuplicateCount = skippedDuplicate,
            SkippedAlreadyConfiguredCount = skippedAlreadyConfigured,
            AddedRules = newRules,
        };
    }
}