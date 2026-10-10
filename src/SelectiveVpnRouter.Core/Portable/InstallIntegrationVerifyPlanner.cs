namespace SelectiveVpnRouter.Core.Portable;

/// <summary>
/// Full-install integration verification intent from pre-update runtime snapshot (not live SCM guesses).
/// </summary>
public static class InstallIntegrationVerifyPlanner
{
    public static bool ShouldRequireLiveServiceIntegrationVerify(PublishRuntimeSnapshot snapshot) =>
        UpdateDesktopSmokeOrchestrationPlanner.ShouldRunRoutingFastSmoke(snapshot);

    public static bool ShouldSkipLiveServiceIntegrationVerify(PublishRuntimeSnapshot snapshot) =>
        snapshot.ProductInstalled && !snapshot.ProductWasRunning;
}
