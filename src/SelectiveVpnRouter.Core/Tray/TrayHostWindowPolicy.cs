namespace SelectiveVpnRouter.Core.Tray;

public static class TrayHostWindowPolicy
{
    public static bool ShouldHideToTrayWhenMinimized(bool isMinimized, bool isShuttingDown)
        => isMinimized && !isShuttingDown;

    public static bool ShouldRestoreToNormal(bool isHidden, bool isMinimized)
        => isHidden || isMinimized;
}
