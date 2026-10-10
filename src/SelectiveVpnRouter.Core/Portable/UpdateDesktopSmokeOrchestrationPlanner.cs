namespace SelectiveVpnRouter.Core.Portable;

/// <summary>
/// Decides post-update smoke steps from the pre-deploy runtime snapshot (intent), not live SCM guesses.
/// </summary>
public static class UpdateDesktopSmokeOrchestrationPlanner
{
    /// <summary>
    /// Routing-fast requires Service IPC; run only when the updater intended to restore a running product service.
    /// </summary>
    public static bool ShouldRunRoutingFastSmoke(PublishRuntimeSnapshot snapshot) =>
        PublishRuntimeLifecyclePlanner.PlanRestore(snapshot).StartProduct;

    public static bool ShouldWaitForServiceIpc(PublishRuntimeSnapshot snapshot) =>
        ShouldRunRoutingFastSmoke(snapshot);
}
