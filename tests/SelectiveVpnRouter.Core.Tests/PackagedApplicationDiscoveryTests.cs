using SelectiveVpnRouter.Core.ApplicationDiscovery;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public class PackagedApplicationDiscoveryTests
{
    [Fact]
    public void A_same_packaged_app_from_package_manager_and_start_menu_merges_to_one_row()
    {
        string exe = WriteFakeExe(@"WindowsApps\Contoso.Demo_1.0.0.0_x64__hash\app\Demo.exe");
        string altExe = WriteFakeExe(@"WindowsApps\Contoso.Demo_1.0.0.0_x64__hash\app\Demo.exe");
        var identity = Identity("Contoso.Demo_hash", "Contoso.Demo_1.0.0.0_x64__hash", "App", "app/Demo.exe");

        var candidates = new[]
        {
            PackagedCandidate(exe, identity),
            StartMenuCandidate(altExe, identity),
        };

        DiscoveredApplication merged = Assert.Single(ApplicationDiscoveryMerger.Merge(candidates));
        Assert.Equal(DiscoverySource.PackagedApp | DiscoverySource.StartMenu, merged.Sources);
        Assert.Equal("msix:Contoso.Demo_hash:App", merged.Id);
    }

    [Fact]
    public void B_same_pfn_two_distinct_application_ids_remain_separate()
    {
        var identityApp = Identity("Contoso.Demo_hash", "Contoso.Demo_1.0.0.0_x64__hash", "App", "app/Demo.exe");
        var identityTool = Identity("Contoso.Demo_hash", "Contoso.Demo_1.0.0.0_x64__hash", "Tool", "tools/Tool.exe");
        string exe1 = WriteFakeExe(@"WindowsApps\Contoso.Demo_1.0.0.0_x64__hash\app\Demo.exe");
        string exe2 = WriteFakeExe(@"WindowsApps\Contoso.Demo_1.0.0.0_x64__hash\tools\Tool.exe");

        var candidates = new[]
        {
            PackagedCandidate(exe1, identityApp, "Demo App"),
            PackagedCandidate(exe2, identityTool, "Demo Tool"),
        };

        Assert.Equal(2, ApplicationDiscoveryMerger.Merge(candidates).Count);
    }

    [Fact]
    public void C_same_application_id_different_resolved_paths_merge()
    {
        var identity = Identity("Contoso.Demo_hash", "Contoso.Demo_1.0.0.0_x64__hash", "App", "app/Demo.exe");
        string exeA = WriteFakeExe(@"WindowsApps\Contoso.Demo_1.0.0.0_x64__hash\app\Demo.exe");
        string exeB = WriteFakeExe(@"WindowsApps\Contoso.Demo_1.0.0.0_x64__hash\app\Demo.exe");

        var candidates = new[]
        {
            PackagedCandidate(exeA, identity),
            PackagedCandidate(exeB, identity with { RelativeExecutablePath = @"app\Demo.exe" }),
        };

        Assert.Single(ApplicationDiscoveryMerger.Merge(candidates));
    }

    [Fact]
    public void D_packaged_identity_merge_key_does_not_depend_on_executable_path()
    {
        var identity = Identity("Contoso.Demo_hash", "Contoso.Demo_2.0.0.0_x64__hash", "App", "app/Demo.exe");
        string oldExe = WriteFakeExe(@"WindowsApps\Contoso.Demo_1.0.0.0_x64__hash\app\Demo.exe");
        string newExe = WriteFakeExe(@"WindowsApps\Contoso.Demo_2.0.0.0_x64__hash\app\Demo.exe");

        Assert.True(ApplicationDiscoveryMergeIdentity.TryGetMergeKey(PackagedCandidate(oldExe, identity), out string keyA));
        Assert.True(ApplicationDiscoveryMergeIdentity.TryGetMergeKey(PackagedCandidate(newExe, identity), out string keyB));
        Assert.Equal(keyA, keyB);
    }

    [Fact]
    public void E_manifest_logo_scale_variant_resolves()
    {
        string root = CreateAssetRoot("scale");
        string? resolved = PackagedAssetResolver.ResolveExistingAssetPath(root, @"assets\Logo.png");
        Assert.Equal(Path.Combine(root, @"assets\Logo.scale-200.png"), resolved);
    }

    [Fact]
    public void F_manifest_logo_targetsize_variant_resolves()
    {
        string root = CreateAssetRoot("target");
        string? resolved = PackagedAssetResolver.ResolveExistingAssetPath(root, @"assets\Square44x44Logo.png");
        Assert.Equal(Path.Combine(root, @"assets\Square44x44Logo.targetsize-32.png"), resolved);
    }

    [Fact]
    public void G_missing_logo_asset_returns_null()
    {
        string root = Path.Combine(Path.GetTempPath(), "vpn-packaged-asset-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "assets"));
        Assert.Null(PackagedAssetResolver.ResolveExistingAssetPath(root, @"assets\Missing.png"));
    }

    [Fact]
    public void H_low_confidence_system_package_hidden_by_default()
    {
        string exe = WriteFakeExe(@"SystemApps\Microsoft.AsyncTextService\AsyncTextService.exe");
        var candidates = new[]
        {
            new DiscoveredApplicationCandidate
            {
                ExecutablePath = exe,
                DisplayName = "AsyncTextService",
                Source = DiscoverySource.PackagedApp,
                LaunchConfidence = ApplicationDiscoveryLaunchConfidence.Low,
                IsSystemComponent = true,
                InstallLocation = @"C:\Windows\SystemApps\Microsoft.AsyncTextService",
            },
        };

        Assert.Empty(ApplicationDiscoveryCatalog.BuildInstalledCatalog(candidates, []));
    }

    [Fact]
    public void I_low_confidence_entry_visible_with_show_system_and_service_entries()
    {
        string exe = WriteFakeExe(@"SystemApps\Microsoft.AsyncTextService\AsyncTextService.exe");
        var candidates = new[]
        {
            new DiscoveredApplicationCandidate
            {
                ExecutablePath = exe,
                DisplayName = "AsyncTextService",
                Source = DiscoverySource.PackagedApp,
                LaunchConfidence = ApplicationDiscoveryLaunchConfidence.Low,
                IsSystemComponent = true,
                InstallLocation = @"C:\Windows\SystemApps\Microsoft.AsyncTextService",
            },
        };

        var options = new ApplicationDiscoveryViewOptions { ShowSystemAndServiceEntries = true };
        Assert.Single(ApplicationDiscoveryCatalog.BuildInstalledCatalog(candidates, [], options: options));
    }

    [Fact]
    public void J_ordinary_user_facing_packaged_app_retained()
    {
        string exe = WriteFakeExe(@"WindowsApps\Contoso.Photo_1.0.0.0_x64__hash\Photo.exe");
        var candidates = new[]
        {
            new DiscoveredApplicationCandidate
            {
                ExecutablePath = exe,
                DisplayName = "Contoso Photo",
                Source = DiscoverySource.PackagedApp,
                LaunchConfidence = ApplicationDiscoveryLaunchConfidence.High,
                PackageIdentity = Identity("Contoso.Photo_hash", "Contoso.Photo_1.0.0.0_x64__hash", "App", "Photo.exe"),
            },
        };

        Assert.Single(ApplicationDiscoveryCatalog.BuildInstalledCatalog(candidates, []));
    }

    [Fact]
    public void K_generic_win32_path_dedup_remains_unchanged()
    {
        var candidates = new[]
        {
            Candidate(@"C:\Apps\Game\game.exe", "Game", DiscoverySource.AppPaths),
            Candidate(@"c:\apps\game\GAME.exe", "Game", DiscoverySource.StartMenu),
        };

        Assert.Single(ApplicationDiscoveryMerger.Merge(candidates));
    }

    [Fact]
    public void L_unicode_paths_and_names_merge_and_filter()
    {
        string exe = WriteFakeExe(@"Apps\Вендор\приложение.exe");
        var identity = Identity("Contoso.Юникод_hash", "Contoso.Юникод_1.0.0.0_x64__hash", "App", "app/приложение.exe");
        var candidates = new[]
        {
            PackagedCandidate(exe, identity, "Приложение"),
            StartMenuCandidate(exe, identity) with { DisplayName = "Приложение" },
        };

        DiscoveredApplication merged = Assert.Single(ApplicationDiscoveryMerger.Merge(candidates));
        Assert.Equal("Приложение", merged.DisplayName);
    }

    [Fact]
    public void Non_user_facing_manifest_application_entry_is_excluded()
    {
        Assert.False(PackagedApplicationHeuristics.IsUserFacingApplicationEntry(
            "CodexCoreCommandRunner",
            "none",
            @"app\resources\codex-command-runner.exe"));
    }

    [Fact]
    public void Windows_apps_folder_name_parses_to_package_family_name()
    {
        bool ok = WindowsAppsPackageIdentityResolver.TryParsePackageFolderName(
            "OpenAI.Codex_26.930.2377.0_x64__2p2nqsd0c76g0",
            out string? family,
            out string? fullName);

        Assert.True(ok);
        Assert.Equal("OpenAI.Codex_2p2nqsd0c76g0", family);
        Assert.Equal("OpenAI.Codex_26.930.2377.0_x64__2p2nqsd0c76g0", fullName);
    }

    private static string CreateAssetRoot(string variant)
    {
        string root = Path.Combine(Path.GetTempPath(), "vpn-packaged-asset-" + Guid.NewGuid().ToString("N"));
        string assets = Path.Combine(root, "assets");
        Directory.CreateDirectory(assets);
        if (variant == "scale")
        {
            File.WriteAllText(Path.Combine(assets, "Logo.scale-200.png"), "png");
        }
        else
        {
            File.WriteAllText(Path.Combine(assets, "Square44x44Logo.targetsize-32.png"), "png");
        }

        return root;
    }

    private static PackagedApplicationIdentity Identity(
        string family,
        string fullName,
        string applicationId,
        string relativeExe)
        => new()
        {
            PackageFamilyName = family,
            PackageFullName = fullName,
            ApplicationId = applicationId,
            AppUserModelId = family + "!" + applicationId,
            RelativeExecutablePath = relativeExe.Replace('/', Path.DirectorySeparatorChar),
        };

    private static DiscoveredApplicationCandidate PackagedCandidate(
        string exe,
        PackagedApplicationIdentity identity,
        string displayName = "Demo")
        => new()
        {
            ExecutablePath = exe,
            DisplayName = displayName,
            Source = DiscoverySource.PackagedApp,
            LaunchConfidence = ApplicationDiscoveryLaunchConfidence.High,
            PackageIdentity = identity,
        };

    private static DiscoveredApplicationCandidate StartMenuCandidate(string exe, PackagedApplicationIdentity identity)
        => new()
        {
            ExecutablePath = exe,
            DisplayName = "Demo",
            Source = DiscoverySource.StartMenu,
            LaunchConfidence = ApplicationDiscoveryLaunchConfidence.High,
            PackageIdentity = identity,
        };

    private static DiscoveredApplicationCandidate Candidate(string exe, string displayName, DiscoverySource source)
        => new()
        {
            ExecutablePath = exe,
            DisplayName = displayName,
            Source = source,
            LaunchConfidence = ApplicationDiscoveryLaunchConfidence.Medium,
        };

    private static string WriteFakeExe(string relativePath)
    {
        string root = Path.Combine(Path.GetTempPath(), "vpn-discovery-test-" + Guid.NewGuid().ToString("N"));
        string full = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "fake");
        return full;
    }
}
