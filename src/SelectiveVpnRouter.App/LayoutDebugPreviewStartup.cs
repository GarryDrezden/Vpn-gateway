using SelectiveVpnRouter.Core;

namespace SelectiveVpnRouter.App;

internal static class LayoutDebugPreviewStartup
{
    internal const string PreviewSaveBlockedMessage = "Режим предпросмотра: изменения не сохраняются.";

    internal static (AppConfiguration Config, UiSettings UiSettings) LoadReadOnlyPreviewState()
    {
        AppConfiguration config = ConfigSerializer.LoadOrDefault(AppPaths.ConfigFile);
        UiSettings ui = UiSettingsStore.Exists()
            ? UiSettingsStore.LoadOrDefault()
            : new UiSettings();
        return (config, ui);
    }
}
