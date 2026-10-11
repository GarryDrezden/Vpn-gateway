using SelectiveVpnRouter.Core.Startup;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public sealed class LayoutDebugStartupRegressionTests
{
    [Fact]
    public void LayoutDebugStartupPolicy_skips_mutating_startup_steps()
    {
        Assert.True(LayoutDebugStartupPolicy.SkipsPortableBootstrapGate(true));
        Assert.True(LayoutDebugStartupPolicy.SkipsStartupAutostartReconciliation(true));
        Assert.True(LayoutDebugStartupPolicy.SkipsStartupAutoConnect(true));
        Assert.True(LayoutDebugStartupPolicy.SkipsUiSettingsMigrationWrite(true));
        Assert.True(LayoutDebugStartupPolicy.SkipsServiceRefreshLoop(true));
        Assert.True(LayoutDebugStartupPolicy.SkipsTrayIconInitialization(true));
        Assert.True(LayoutDebugStartupPolicy.CloseExitsApplication(true));
        Assert.True(LayoutDebugStartupPolicy.BlocksPersistence(true));
    }

    [Fact]
    public void LayoutDebugStartupPolicy_does_not_skip_production_steps_when_disabled()
    {
        Assert.False(LayoutDebugStartupPolicy.SkipsPortableBootstrapGate(false));
        Assert.False(LayoutDebugStartupPolicy.SkipsStartupAutostartReconciliation(false));
        Assert.False(LayoutDebugStartupPolicy.SkipsStartupAutoConnect(false));
    }

    [Fact]
    public void MainWindow_layout_debug_preview_does_not_run_bootstrap_or_mutating_startup()
    {
        string main = ReadRepoFile(Path.Combine("src", "SelectiveVpnRouter.App", "MainWindow.xaml.cs"));
        Assert.Contains("StartLayoutDebugPreviewAsync", main, StringComparison.Ordinal);
        string preview = ExtractMethodBody(main, "private async Task StartLayoutDebugPreviewAsync()");
        Assert.DoesNotContain("EnsurePortableBootstrapReadyAsync", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("TryReconcileAutostartRegistrationOnce", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("TryStartupAutoConnectAsync", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureInitialFileFromLegacyServiceUi", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshLoop", preview, StringComparison.Ordinal);
        Assert.Contains("LayoutDebugPreviewStartup.LoadReadOnlyPreviewState", preview, StringComparison.Ordinal);
        Assert.Contains("NavigateToSettings", preview, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_layout_debug_skips_tray_init_and_closes_on_x()
    {
        string main = ReadRepoFile(Path.Combine("src", "SelectiveVpnRouter.App", "MainWindow.xaml.cs"));
        Assert.Contains("if (!LayoutDebugOptions.Enabled)", main, StringComparison.Ordinal);
        Assert.Contains("_trayController.EnsureInitialized()", main, StringComparison.Ordinal);
        string onClosing = ExtractMethodBody(main, "private void OnClosing(object sender, CancelEventArgs e)");
        Assert.Contains("LayoutDebugOptions.Enabled", onClosing, StringComparison.Ordinal);
        Assert.DoesNotContain("e.Cancel = true", ExtractMethodBody(main, "private async Task StartLayoutDebugPreviewAsync()"), StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_layout_debug_save_is_non_mutating()
    {
        string main = ReadRepoFile(Path.Combine("src", "SelectiveVpnRouter.App", "MainWindow.xaml.cs"));
        string onSaveSetup = ExtractMethodBody(main, "private async void OnSaveSetup(object sender, RoutedEventArgs e)");
        Assert.Contains("LayoutDebugPreviewStartup.PreviewSaveBlockedMessage", onSaveSetup, StringComparison.Ordinal);
        string saveConfig = ExtractMethodBody(main, "private async Task SaveConfigAsync(AppConfiguration? config = null)");
        Assert.Contains("LayoutDebugOptions.Enabled", saveConfig, StringComparison.Ordinal);
    }

    [Fact]
    public void App_layout_debug_shows_window_without_bootstrap_gate()
    {
        string app = ReadRepoFile(Path.Combine("src", "SelectiveVpnRouter.App", "App.xaml.cs"));
        int layoutBranch = app.IndexOf("if (LayoutDebugOptions.Enabled)", StringComparison.Ordinal);
        int bootstrapRead = app.IndexOf("PortableBootstrapUi.ReadStatus", StringComparison.Ordinal);
        Assert.True(layoutBranch >= 0);
        Assert.True(bootstrapRead > layoutBranch);
    }

    [Fact]
    public void App_layout_debug_still_disables_single_instance()
    {
        string app = ReadRepoFile(Path.Combine("src", "SelectiveVpnRouter.App", "App.xaml.cs"));
        Assert.Contains("bool singleInstanceEnabled = !LayoutDebugOptions.Enabled", app, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_startup_still_runs_accepted_pipeline()
    {
        string main = ReadRepoFile(Path.Combine("src", "SelectiveVpnRouter.App", "MainWindow.xaml.cs"));
        string continueStartup = ExtractMethodBody(main, "private async Task ContinueStartupAsync()");
        Assert.Contains("EnsureInitialFileFromLegacyServiceUi", continueStartup, StringComparison.Ordinal);
        Assert.Contains("TryReconcileAutostartRegistrationOnce", continueStartup, StringComparison.Ordinal);
        Assert.Contains("TryStartupAutoConnectAsync", continueStartup, StringComparison.Ordinal);
        Assert.Contains("EnsurePortableBootstrapReadyAsync", main, StringComparison.Ordinal);
    }

    private static string ExtractMethodBody(string source, string methodSignaturePrefix)
    {
        int start = source.IndexOf(methodSignaturePrefix, StringComparison.Ordinal);
        Assert.True(start >= 0);
        int brace = source.IndexOf('{', start + methodSignaturePrefix.Length);
        Assert.True(brace >= 0);
        int depth = 0;
        for (int i = brace; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source[brace..(i + 1)];
                }
            }
        }

        throw new InvalidOperationException("Method body not found: " + methodSignaturePrefix);
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
