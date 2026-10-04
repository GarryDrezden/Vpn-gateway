using SelectiveVpnRouter.Core;
using SelectiveVpnRouter.Core.ApplicationDiscovery;
using SelectiveVpnRouter.Core.ApplicationDiscovery.Windows;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public class ApplicationDiscoveryTests
{
    [Fact]
    public void Same_exe_path_different_case_merges_to_one_item()
    {
        var candidates = new[]
        {
            Candidate(@"C:\Apps\Game\game.exe", "Game", DiscoverySource.AppPaths),
            Candidate(@"c:\apps\game\GAME.exe", "Game", DiscoverySource.StartMenu),
        };

        Assert.Single(ApplicationDiscoveryMerger.Merge(candidates));
    }

    [Fact]
    public void Interactive_window_candidate_included_in_default_running_catalog()
    {
        string exe = WriteFakeExe(@"Apps\Chat\ChatGPT.exe");
        var candidates = new[]
        {
            Candidate(exe, "ChatGPT", DiscoverySource.InteractiveWindow, isRunning: true, confidence: ApplicationDiscoveryLaunchConfidence.High),
        };

        IReadOnlyList<DiscoveredApplication> catalog = ApplicationDiscoveryCatalog.BuildRunningCatalog(candidates, []);
        Assert.Single(catalog);
    }

    [Fact]
    public void Background_process_excluded_from_default_running_catalog()
    {
        string exe = WriteFakeExe(@"Apps\Vendor\CefSharp.BrowserSubprocess.exe");
        var candidates = new[]
        {
            Candidate(exe, "CefSharp.BrowserSubprocess", DiscoverySource.RunningProcess, isRunning: true, confidence: ApplicationDiscoveryLaunchConfidence.Low),
        };

        Assert.Empty(ApplicationDiscoveryCatalog.BuildRunningCatalog(candidates, []));
    }

    [Fact]
    public void Background_process_included_when_advanced_running_mode_enabled()
    {
        string exe = WriteFakeExe(@"Tools\nginx.exe");
        var candidates = new[]
        {
            Candidate(exe, "nginx", DiscoverySource.RunningProcess, isRunning: true, confidence: ApplicationDiscoveryLaunchConfidence.Low),
        };

        var options = new ApplicationDiscoveryViewOptions { ShowBackgroundProcesses = true };
        Assert.Single(ApplicationDiscoveryCatalog.BuildRunningCatalog(candidates, [], options: options));
    }

    [Fact]
    public void Multiple_windows_same_exe_are_deduplicated()
    {
        string exe = WriteFakeExe(@"Apps\Telegram\Telegram.exe");
        var candidates = new[]
        {
            Candidate(exe, "Telegram (1)", DiscoverySource.InteractiveWindow, isRunning: true),
            Candidate(exe, "Telegram (2)", DiscoverySource.InteractiveWindow, isRunning: true),
        };

        Assert.Single(ApplicationDiscoveryCatalog.BuildRunningCatalog(candidates, []));
    }

    [Fact]
    public void Configured_running_app_is_kept_even_without_interactive_window_source()
    {
        string exe = WriteFakeExe(@"Apps\Configured\app.exe");
        var candidates = new[]
        {
            Candidate(exe, "Configured", DiscoverySource.RunningProcess, isRunning: true, confidence: ApplicationDiscoveryLaunchConfidence.Low),
        };
        RoutingRule rule = RoutingRule.Create(RuleType.Application, "Configured", exe, RouteMode.Vpn);

        IReadOnlyList<DiscoveredApplication> catalog = ApplicationDiscoveryCatalog.BuildRunningCatalog(candidates, [rule]);
        DiscoveredApplication app = Assert.Single(catalog);
        Assert.True(app.IsAlreadyConfigured);
    }

    [Fact]
    public void Low_confidence_uninstall_entry_hidden_by_default()
    {
        string exe = WriteFakeExe(@"WindowsApps\WebView2\msedgewebview2.exe");
        var candidates = new[]
        {
            Candidate(exe, "Microsoft Edge WebView2 Runtime", DiscoverySource.Uninstall,
                confidence: ApplicationDiscoveryLaunchConfidence.Low,
                installLocation: @"C:\Program Files\WindowsApps\WebView2"),
        };

        Assert.Empty(ApplicationDiscoveryCatalog.BuildInstalledCatalog(candidates, []));
    }

    [Fact]
    public void High_confidence_start_menu_entry_retained()
    {
        string exe = WriteFakeExe(@"Program Files\Vendor\App\client.exe");
        var candidates = new[]
        {
            Candidate(exe, "Vendor App", DiscoverySource.StartMenu, confidence: ApplicationDiscoveryLaunchConfidence.High),
        };

        Assert.Single(ApplicationDiscoveryCatalog.BuildInstalledCatalog(candidates, []));
    }

    [Fact]
    public void Show_system_mode_includes_low_confidence_installed_entry()
    {
        string exe = WriteFakeExe(@"WindowsApps\WebView2\msedgewebview2.exe");
        var candidates = new[]
        {
            Candidate(exe, "Microsoft Edge WebView2 Runtime", DiscoverySource.Uninstall,
                confidence: ApplicationDiscoveryLaunchConfidence.Low,
                installLocation: @"C:\Program Files\WindowsApps\WebView2"),
        };

        var options = new ApplicationDiscoveryViewOptions { ShowSystemAndServiceEntries = true };
        Assert.Single(ApplicationDiscoveryCatalog.BuildInstalledCatalog(candidates, [], options: options));
    }

    [Fact]
    public void Packaged_app_metadata_is_merged_and_retained()
    {
        string exe = WriteFakeExe(@"WindowsApps\OpenAI.ChatGPT_1.0.0.0_x64__abc123\ChatGPT.exe");
        var identity = new PackagedApplicationIdentity
        {
            PackageFamilyName = "OpenAI.ChatGPT_abc123",
            PackageFullName = "OpenAI.ChatGPT_1.0.0.0_x64__abc123",
            ApplicationId = "App",
            RelativeExecutablePath = "ChatGPT.exe",
        };
        var candidates = new[]
        {
            new DiscoveredApplicationCandidate
            {
                ExecutablePath = exe,
                DisplayName = "ChatGPT",
                Publisher = "OpenAI",
                Source = DiscoverySource.PackagedApp,
                LaunchConfidence = ApplicationDiscoveryLaunchConfidence.High,
                PackageIdentity = identity,
            },
        };

        DiscoveredApplication merged = Assert.Single(ApplicationDiscoveryMerger.Merge(candidates));
        Assert.Equal("OpenAI", merged.Publisher);
        Assert.NotNull(merged.PackageIdentity);
        Assert.Equal("OpenAI.ChatGPT_abc123", merged.PackageIdentity!.PackageFamilyName);
        Assert.True(merged.PackageIdentity.HasStablePackageIdentity);
    }

    [Fact]
    public void Search_finds_packaged_app_by_display_name_and_publisher()
    {
        string exe = WriteFakeExe(@"WindowsApps\OpenAI.ChatGPT_1.0.0.0_x64__abc123\ChatGPT.exe");
        var candidates = new[]
        {
            Candidate(exe, "ChatGPT", DiscoverySource.PackagedApp, publisher: "OpenAI", confidence: ApplicationDiscoveryLaunchConfidence.High),
            Candidate(WriteFakeExe(@"Apps\Other\other.exe"), "Other", DiscoverySource.StartMenu, confidence: ApplicationDiscoveryLaunchConfidence.High),
        };

        IReadOnlyList<DiscoveredApplication> catalog = ApplicationDiscoveryCatalog.BuildInstalledCatalog(candidates, []);
        Assert.Single(ApplicationDiscoverySearch.Filter(catalog, "chat"));
        Assert.Single(ApplicationDiscoverySearch.Filter(catalog, "openai"));
    }

    [Fact]
    public void Helper_subprocess_display_name_does_not_replace_uninstall_name()
    {
        string exe = WriteFakeExe(@"Apps\Tool\tool.exe");
        var candidates = new[]
        {
            Candidate(exe, "Tool Runtime Helper", DiscoverySource.RunningProcess, isRunning: true),
            Candidate(exe, "Professional Tool Suite", DiscoverySource.Uninstall, confidence: ApplicationDiscoveryLaunchConfidence.Medium),
        };

        DiscoveredApplication merged = Assert.Single(ApplicationDiscoveryMerger.Merge(candidates));
        Assert.Equal("Professional Tool Suite", merged.DisplayName);
    }

    [Fact]
    public void User32InteractiveWindowEnumerator_skips_tool_window_without_appwindow()
    {
        bool eligible = User32InteractiveWindowEnumerator.IsEligibleTopLevelWindow(IntPtr.Zero, out _, out _, out _);
        Assert.False(eligible);
    }

    [Fact]
    public void Interactive_window_discovery_deduplicates_by_executable()
    {
        var enumerator = new FakeWindowEnumerator(
            new InteractiveWindowSnapshot(101, "ChatGPT", "ApplicationFrameWindow"),
            new InteractiveWindowSnapshot(102, "ChatGPT", "ApplicationFrameWindow"));
        string sharedExe = WriteFakeExe(@"Apps\ChatGPT\ChatGPT.exe");
        var resolver = new FakeProcessPathResolver(new Dictionary<int, string>
        {
            [101] = sharedExe,
            [102] = sharedExe,
        });

        var discovery = new InteractiveWindowDiscovery(enumerator, resolver);
        Assert.Single(discovery.Collect().ToList());
    }


    private static string WriteFakeExe(string relativePath)
    {
        string root = Path.Combine(Path.GetTempPath(), "vpn-discovery-test-" + Guid.NewGuid().ToString("N"));
        string full = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "fake");
        return full;
    }
    private static DiscoveredApplicationCandidate Candidate(
        string exe,
        string displayName,
        DiscoverySource source,
        bool isRunning = false,
        string? publisher = null,
        ApplicationDiscoveryLaunchConfidence confidence = ApplicationDiscoveryLaunchConfidence.Medium,
        string? installLocation = null)
        => new()
        {
            ExecutablePath = exe,
            DisplayName = displayName,
            Publisher = publisher,
            Source = source,
            IsRunning = isRunning,
            LaunchConfidence = confidence,
            InstallLocation = installLocation,
            IconPath = exe,
        };

    private sealed class FakeWindowEnumerator(params InteractiveWindowSnapshot[] windows) : IInteractiveWindowEnumerator
    {
        public IReadOnlyList<InteractiveWindowSnapshot> EnumerateVisibleTopLevelWindows() => windows;
    }

    private sealed class FakeProcessPathResolver(Dictionary<int, string> map) : IProcessPathResolver
    {
        public string? TryGetExecutablePath(int processId) => map.TryGetValue(processId, out string? path) ? path : null;
    }
}