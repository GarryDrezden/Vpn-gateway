namespace SelectiveVpnRouter.Core.Portable;

public sealed record CalloutDriverDeploySnapshot(
    bool CalloutInstalled,
    bool CalloutWasRunning,
    bool ProductInstalled,
    bool ProductWasRunning,
    string? RegisteredImagePath,
    string? LiveRuntimeSysPath,
    string? LiveRuntimeHash,
    string? StagedHash);

public sealed record CalloutDriverDeployPlan(
    bool BuildBeforeStop,
    bool StopServicesForDeploy,
    bool CopyStagedToLiveRuntime,
    bool RegisterLiveRuntimePath,
    bool IdempotentAlreadyDeployed);

public sealed record CalloutDriverRollbackPlan(
    bool RestoreLiveBinaryFromRollback,
    bool RestoreRegistrationFromSnapshot,
    bool RestartCalloutIfWasRunning,
    bool RestartProductIfWasRunning);

public static class CalloutDriverDeployPlanner
{
    public static bool ShouldBuildWithoutStoppingServices(bool skipBuild) => !skipBuild;

    public static bool ShouldStopServicesForDeploy(CalloutDriverDeployPlan plan) =>
        plan.StopServicesForDeploy && !plan.IdempotentAlreadyDeployed;

    public static CalloutDriverDeployPlan PlanDeploy(
        CalloutDriverDeploySnapshot snapshot,
        bool skipBuild,
        bool calloutRunning,
        bool productRunning,
        string publishRuntimeSysPath)
    {
        string runtimeFull = Path.GetFullPath(publishRuntimeSysPath);
        string? registeredFull = PublishRuntimeLifecyclePlanner.TryNormalizeDriverImagePath(snapshot.RegisteredImagePath);
        bool registeredOnRuntime = registeredFull is not null
            && string.Equals(registeredFull, runtimeFull, StringComparison.OrdinalIgnoreCase);

        bool stagedMatchesLive = HashesEqual(snapshot.StagedHash, snapshot.LiveRuntimeHash);
        if (skipBuild
            && calloutRunning
            && productRunning
            && registeredOnRuntime
            && stagedMatchesLive
            && !string.IsNullOrWhiteSpace(snapshot.LiveRuntimeHash))
        {
            return new CalloutDriverDeployPlan(
                BuildBeforeStop: false,
                StopServicesForDeploy: false,
                CopyStagedToLiveRuntime: false,
                RegisterLiveRuntimePath: false,
                IdempotentAlreadyDeployed: true);
        }

        return new CalloutDriverDeployPlan(
            BuildBeforeStop: !skipBuild,
            StopServicesForDeploy: true,
            CopyStagedToLiveRuntime: !stagedMatchesLive || !registeredOnRuntime,
            RegisterLiveRuntimePath: true,
            IdempotentAlreadyDeployed: false);
    }

    public static CalloutDriverRollbackPlan PlanRollback(CalloutDriverDeploySnapshot snapshot) =>
        new(
            RestoreLiveBinaryFromRollback: true,
            RestoreRegistrationFromSnapshot: snapshot.CalloutInstalled && !string.IsNullOrWhiteSpace(snapshot.RegisteredImagePath),
            RestartCalloutIfWasRunning: snapshot.CalloutInstalled && snapshot.CalloutWasRunning,
            RestartProductIfWasRunning: snapshot.ProductInstalled && snapshot.ProductWasRunning);

    public static bool RollbackArtifactMustNotAliasStagedBuild(string rollbackSysPath, string stagedSysPath)
    {
        string rollbackFull = Path.GetFullPath(rollbackSysPath);
        string stagedFull = Path.GetFullPath(stagedSysPath);
        return !string.Equals(rollbackFull, stagedFull, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HashesEqual(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a)
        && !string.IsNullOrWhiteSpace(b)
        && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}