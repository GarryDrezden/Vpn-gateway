namespace SelectiveVpnRouter.Core.Startup;

/// <summary>
/// Documents which production startup steps are skipped in --layout-debug visual preview.
/// </summary>
public static class LayoutDebugStartupPolicy
{
    public static bool SkipsPortableBootstrapGate(bool layoutDebugEnabled) => layoutDebugEnabled;

    public static bool SkipsStartupAutostartReconciliation(bool layoutDebugEnabled) => layoutDebugEnabled;

    public static bool SkipsStartupAutoConnect(bool layoutDebugEnabled) => layoutDebugEnabled;

    public static bool SkipsUiSettingsMigrationWrite(bool layoutDebugEnabled) => layoutDebugEnabled;

    public static bool SkipsServiceRefreshLoop(bool layoutDebugEnabled) => layoutDebugEnabled;

    public static bool SkipsTrayIconInitialization(bool layoutDebugEnabled) => layoutDebugEnabled;

    public static bool CloseExitsApplication(bool layoutDebugEnabled) => layoutDebugEnabled;

    public static bool BlocksPersistence(bool layoutDebugEnabled) => layoutDebugEnabled;
}
