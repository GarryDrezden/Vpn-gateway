namespace SelectiveVpnRouter.Core.Portable;

public enum CalloutDriverRegistrationAction
{
    Skip,
    FailPublishedSysMissing,
    KeepCanonical,
    KeepRegisteredOutsidePublishTree,
    RegisterAtPublish,
    RepairToPublish,
}

public static class PublishRuntimeCalloutRegistrationPlanner
{
    public static bool ShouldEnsureCalloutRegistrationBeforeProductStart(PublishRuntimeSnapshot snapshot) =>
        snapshot.ProductInstalled && snapshot.ProductWasRunning;

    public static CalloutDriverRegistrationAction PlanRegistration(
        PublishRuntimeSnapshot snapshot,
        string publishRoot,
        bool publishedSysExists,
        bool driverServiceInstalled,
        string? registeredImagePath,
        string canonicalPublishedSysPath)
    {
        if (!ShouldEnsureCalloutRegistrationBeforeProductStart(snapshot))
        {
            return CalloutDriverRegistrationAction.Skip;
        }

        if (!publishedSysExists)
        {
            return CalloutDriverRegistrationAction.FailPublishedSysMissing;
        }

        if (!PublishRuntimeLifecyclePlanner.ShouldRepairDriverImagePathToPublishLayout(snapshot, publishRoot))
        {
            return driverServiceInstalled
                ? CalloutDriverRegistrationAction.KeepRegisteredOutsidePublishTree
                : CalloutDriverRegistrationAction.RegisterAtPublish;
        }

        if (!driverServiceInstalled)
        {
            return CalloutDriverRegistrationAction.RegisterAtPublish;
        }

        string? current = PublishRuntimeLifecyclePlanner.TryNormalizeDriverImagePath(registeredImagePath);
        string? expected = PublishRuntimeLifecyclePlanner.TryNormalizeDriverImagePath(canonicalPublishedSysPath);
        if (current is not null
            && expected is not null
            && current.Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            return CalloutDriverRegistrationAction.KeepCanonical;
        }

        return CalloutDriverRegistrationAction.RepairToPublish;
    }

    public static bool RestoreMustEnsureCalloutBeforeProductStart(PublishRuntimeRestorePlan plan) =>
        plan.EnsureCalloutRegistrationBeforeProductStart;
}
