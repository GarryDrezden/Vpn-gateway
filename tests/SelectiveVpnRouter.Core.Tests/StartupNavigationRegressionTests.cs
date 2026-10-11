using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public sealed class StartupNavigationRegressionTests
{
    [Fact]
    public void App_startup_supports_hidden_launch_without_always_showing_window()
    {
        string app = ReadRepoFile(Path.Combine("src", "SelectiveVpnRouter.App", "App.xaml.cs"));
        Assert.Contains("BeginHiddenStartup", app, StringComparison.Ordinal);
        Assert.Contains("RunInitialStartupAsync", app, StringComparison.Ordinal);
        Assert.Contains("StartupWindowPolicy.ShouldStartHidden", app, StringComparison.Ordinal);
        Assert.False(
            System.Text.RegularExpressions.Regex.IsMatch(
                app,
                @"window\.Show\(\);\s*window\.Hide\(\)",
                System.Text.RegularExpressions.RegexOptions.Singleline));
    }

    [Fact]
    public void MainWindow_wires_startup_settings_and_noninteractive_auto_connect()
    {
        string main = ReadRepoFile(Path.Combine("src", "SelectiveVpnRouter.App", "MainWindow.xaml.cs"));
        Assert.Contains("UiSettingsStore", main, StringComparison.Ordinal);
        Assert.Contains("ConnectVpnInternalAsync", main, StringComparison.Ordinal);
        Assert.Contains("ConnectVpnPresentation.StartupAutoConnect", main, StringComparison.Ordinal);
        Assert.Contains("EnsureMainWindowVisibleForUserAction", main, StringComparison.Ordinal);
        Assert.Contains("TrySaveUiSettingsAndAutostart", main, StringComparison.Ordinal);
        Assert.Contains("TryReconcileAutostartRegistrationOnce", main, StringComparison.Ordinal);
        Assert.DoesNotContain("MergeFromServiceConfig", main, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_tab_exposes_startup_behavior_controls()
    {
        string xaml = ReadRepoFile(Path.Combine("src", "SelectiveVpnRouter.App", "MainWindow.xaml"));
        Assert.Contains("Запуск и поведение", xaml, StringComparison.Ordinal);
        Assert.Contains("StartWithWindowsBox", xaml, StringComparison.Ordinal);
        Assert.Contains("StartMinimizedBox", xaml, StringComparison.Ordinal);
        Assert.Contains("AutoConnectVpnOnStartupBox", xaml, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath)
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            string candidate = Path.Combine(dir, relativePath);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException(relativePath + " not found");
    }
}
