using SelectiveVpnRouter.Core.Startup;

namespace SelectiveVpnRouter.Core;

public sealed record UiSettingsSaveResult(UiSettings PersistedSettings, string? AutostartWarning);

public static class UiSettingsPersistence
{
    public static UiSettingsSaveResult SaveWithAutostartTransaction(
        UiSettings previouslyPersisted,
        UiSettings requestedFromUi,
        IWindowsStartupRunKey runKey,
        string executablePath,
        bool includeAutostartMarker = true,
        string? uiSettingsPath = null)
    {
        UiSettings partial = previouslyPersisted with
        {
            StartMinimized = requestedFromUi.StartMinimized,
            AutoConnectVpnOnStartup = requestedFromUi.AutoConnectVpnOnStartup,
        };

        bool effectiveStartWithWindows = previouslyPersisted.StartWithWindows;
        string? warning = null;
        try
        {
            string? registered = runKey.ReadCommand(WindowsAutostartManager.ValueName);
            WindowsAutostartPlan plan = WindowsAutostartManager.PlanRegistration(
                requestedFromUi.StartWithWindows,
                executablePath,
                registered,
                includeAutostartMarker);
            WindowsAutostartManager.ApplyPlan(runKey, plan);
            effectiveStartWithWindows = requestedFromUi.StartWithWindows;
        }
        catch (Exception ex)
        {
            warning = ex.Message;
        }

        UiSettings persisted = partial with { StartWithWindows = effectiveStartWithWindows };
        UiSettingsStore.Save(persisted, uiSettingsPath);
        return new UiSettingsSaveResult(persisted, warning);
    }

    /// <summary>
    /// One startup attempt: align HKCU Run value <c>VPN Route</c> with persisted StartWithWindows.
    /// </summary>
    public static string? TryReconcileAutostartRegistration(
        UiSettings uiSettings,
        IWindowsStartupRunKey runKey,
        string executablePath,
        bool includeAutostartMarker = true)
    {
        try
        {
            string? registered = runKey.ReadCommand(WindowsAutostartManager.ValueName);
            WindowsAutostartPlan plan = WindowsAutostartManager.PlanRegistration(
                uiSettings.StartWithWindows,
                executablePath,
                registered,
                includeAutostartMarker);
            if (plan.ShouldWrite || plan.ShouldDelete)
            {
                WindowsAutostartManager.ApplyPlan(runKey, plan);
            }

            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }
}
