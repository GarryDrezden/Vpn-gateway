namespace SelectiveVpnRouter.Core.Startup;

public static class StartupAutoConnectPolicy
{
    public static bool ShouldAttemptAutoConnect(
        bool autoConnectEnabled,
        bool vpnRoutingReady,
        bool connectInProgress,
        bool startupAutoConnectAlreadyAttempted)
    {
        if (!autoConnectEnabled || vpnRoutingReady || connectInProgress || startupAutoConnectAlreadyAttempted)
        {
            return false;
        }

        return true;
    }
}
