using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public class TrayNavigationRegressionTests
{
    private static string ReadRepoFile(string relativePath)
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dir is not null; i++)
        {
            string candidate = Path.Combine(dir, relativePath);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new FileNotFoundException("Could not locate repo file: " + relativePath);
    }

    [Fact]
    public void Settings_tray_navigation_uses_named_settings_tab_not_magic_index()
    {
        string mainWindowCs = ReadRepoFile(Path.Combine("src", "SelectiveVpnRouter.App", "MainWindow.xaml.cs"));
        string mainWindowXaml = ReadRepoFile(Path.Combine("src", "SelectiveVpnRouter.App", "MainWindow.xaml"));

        Assert.Contains("NavigateToSettings", mainWindowCs, StringComparison.Ordinal);
        Assert.Contains("SettingsTab", mainWindowCs, StringComparison.Ordinal);
        Assert.Contains("SelectedItem = SettingsTab", mainWindowCs, StringComparison.Ordinal);
        Assert.Contains("OpenSettingsFromTray", mainWindowCs, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SettingsTab\"", mainWindowXaml, StringComparison.Ordinal);

        int settingsMethod = mainWindowCs.IndexOf("void NavigateToSettings", StringComparison.Ordinal);
        Assert.True(settingsMethod >= 0);
        int methodEnd = mainWindowCs.IndexOf("\n    }", settingsMethod, StringComparison.Ordinal);
        Assert.True(methodEnd > settingsMethod);
        string methodBody = mainWindowCs.Substring(settingsMethod, methodEnd - settingsMethod);
        Assert.DoesNotContain("SelectedIndex", methodBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Tray_controller_initializes_only_once()
    {
        string controller = ReadRepoFile(Path.Combine("src", "SelectiveVpnRouter.App", "Tray", "AppTrayController.cs"));
        Assert.Contains("if (_initialized || _disposed)", controller, StringComparison.Ordinal);
        Assert.Contains("_initialized = true", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void Minimize_to_tray_uses_shared_window_policy()
    {
        string mainWindowCs = ReadRepoFile(Path.Combine("src", "SelectiveVpnRouter.App", "MainWindow.xaml.cs"));
        Assert.Contains("TrayHostWindowPolicy.ShouldHideToTrayWhenMinimized", mainWindowCs, StringComparison.Ordinal);
        Assert.Contains("StateChanged += OnMainWindowStateChanged", mainWindowCs, StringComparison.Ordinal);
        Assert.Contains("Hide();", mainWindowCs, StringComparison.Ordinal);
    }

    [Fact]
    public void Restore_from_tray_uses_policy_and_keeps_activate_idempotent()
    {
        string mainWindowCs = ReadRepoFile(Path.Combine("src", "SelectiveVpnRouter.App", "MainWindow.xaml.cs"));
        Assert.Contains("TrayHostWindowPolicy.ShouldRestoreToNormal", mainWindowCs, StringComparison.Ordinal);

        int restoreMethod = mainWindowCs.IndexOf("void ShowMainWindowFromTray", StringComparison.Ordinal);
        Assert.True(restoreMethod >= 0);
        int methodEnd = mainWindowCs.IndexOf("\n    }", restoreMethod, StringComparison.Ordinal);
        Assert.True(methodEnd > restoreMethod);
        string methodBody = mainWindowCs.Substring(restoreMethod, methodEnd - restoreMethod);
        Assert.Contains("Activate();", methodBody, StringComparison.Ordinal);
        Assert.DoesNotContain("new MainWindow", methodBody, StringComparison.Ordinal);
    }
}
