using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public class DesktopShellRegressionTests
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
    public void Settings_tab_keeps_semantic_name_for_tray_navigation()
    {
        string xaml = ReadRepoFile(Path.Combine("src", "SelectiveVpnRouter.App", "MainWindow.xaml"));
        Assert.Contains("x:Name=\"SettingsTab\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void App_merges_vpn_route_theme_resources()
    {
        string appXaml = ReadRepoFile(Path.Combine("src", "SelectiveVpnRouter.App", "App.xaml"));
        Assert.Contains("Theme/VpnRouteColors.xaml", appXaml, StringComparison.Ordinal);
        Assert.Contains("Theme/VpnRouteControls.xaml", appXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Main_window_uses_shared_theme_brushes_not_inline_page_background()
    {
        string xaml = ReadRepoFile(Path.Combine("src", "SelectiveVpnRouter.App", "MainWindow.xaml"));
        Assert.Contains("Background=\"{StaticResource PageBackgroundBrush}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Application_and_browser_lists_use_dark_list_view_style()
    {
        string xaml = ReadRepoFile(Path.Combine("src", "SelectiveVpnRouter.App", "MainWindow.xaml"));
        Assert.Contains("x:Name=\"AppsList\" Style=\"{StaticResource DarkListView}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"BrowserRulesList\" Style=\"{StaticResource DarkListView}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void App_merges_list_theme_resources()
    {
        string appXaml = ReadRepoFile(Path.Combine("src", "SelectiveVpnRouter.App", "App.xaml"));
        Assert.Contains("Theme/VpnRouteLists.xaml", appXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void App_merges_diagnostics_theme_resources()
    {
        string appXaml = ReadRepoFile(Path.Combine("src", "SelectiveVpnRouter.App", "App.xaml"));
        Assert.Contains("Theme/VpnRouteDiagnostics.xaml", appXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void App_routing_row_uses_segmented_selector_status_badge_and_clip()
    {
        string xaml = ReadRepoFile(Path.Combine("src", "SelectiveVpnRouter.App", "MainWindow.xaml"));
        Assert.Contains("x:Name=\"AppsList\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ClipToBounds=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("RouteSegmentRadioButton", xaml, StringComparison.Ordinal);
        Assert.Contains("RouteRuntimeStatusBadge", xaml, StringComparison.Ordinal);
        Assert.Contains("Checked=\"OnAppRouteToggle\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Home_active_apps_list_uses_themed_row_text()
    {
        string xaml = ReadRepoFile(Path.Combine("src", "SelectiveVpnRouter.App", "MainWindow.xaml"));
        Assert.Contains("x:Name=\"HomeActiveAppsList\"", xaml, StringComparison.Ordinal);
        int homeIdx = xaml.IndexOf("HomeActiveAppsList", StringComparison.Ordinal);
        int nextTabIdx = xaml.IndexOf("<!-- APPLICATIONS -->", StringComparison.Ordinal);
        Assert.True(homeIdx >= 0 && nextTabIdx > homeIdx);
        string homeSection = xaml.Substring(homeIdx, nextTabIdx - homeIdx);
        Assert.Contains("RowPrimaryText", homeSection, StringComparison.Ordinal);
        Assert.Contains("RouteRuntimeStatusBadge", homeSection, StringComparison.Ordinal);
    }

    [Fact]
    public void Test_center_uses_diagnostic_tab_hierarchy_styles()
    {
        string xaml = ReadRepoFile(Path.Combine("src", "SelectiveVpnRouter.App", "MainWindow.xaml"));
        Assert.Contains("x:Name=\"DiagInnerTabs\"", xaml, StringComparison.Ordinal);
        Assert.Contains("DiagPrimaryTabControl", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"TestCenterCategoryTabs\"", xaml, StringComparison.Ordinal);
        Assert.Contains("DiagSecondaryTabControl", xaml, StringComparison.Ordinal);
    }
}
