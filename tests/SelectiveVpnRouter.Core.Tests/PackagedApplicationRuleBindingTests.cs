using SelectiveVpnRouter.Core;
using SelectiveVpnRouter.Core.ApplicationDiscovery;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public class PackagedApplicationRuleBindingTests
{
    private const string AlternateUserSid = "S-1-5-21-0000000000-0000000000-0000000000-1001";
    private static string TestUserSid => InteractiveUserSid.TryGetCurrent() ?? AlternateUserSid;
    private const string Pfn = "Contoso.SampleApp_abc123";
    private const string AppId = "App";

    [Fact]
    public void Win32_rule_without_binding_is_unchanged_by_rebind()
    {
        string exe = Environment.ProcessPath!;
        var config = new AppConfiguration
        {
            Rules = [RoutingRule.Create(RuleType.Application, "proc", exe, RouteMode.Vpn)],
        };
        var resolver = new FakePackagedApplicationPathResolver();
        PackagedApplicationRuleRebindResult result = PackagedApplicationRuleRebinder.TryRebind(config, resolver);
        Assert.False(result.Changed);
        Assert.Equal(exe, result.Config.Rules[0].Target);
        Assert.Null(result.Config.Rules[0].PackagedBinding);
    }

    [Fact]
    public void Packaged_rule_with_current_target_does_not_change()
    {
        using TempPackage package = CreatePackage("26.930.0.0", "app\\ChatGPT.exe");
        var binding = CreateBinding(package);
        var config = ConfigWithPackagedRule(package.ExecutablePath, binding);
        var resolver = FakeFor(package);
        PackagedApplicationRuleRebindResult result = PackagedApplicationRuleRebinder.TryRebind(config, resolver);
        Assert.False(result.Changed);
    }

    [Fact]
    public void Stale_versioned_target_rebinds_to_current_package_path()
    {
        using TempPackage package = CreatePackage("26.941.0.0", "app\\ChatGPT.exe");
        string staleTarget = Path.Combine(
            Path.GetTempPath(),
            "WindowsApps",
            "Contoso.SampleApp_26.930.2377.0_x64__abc123",
            "app",
            "ChatGPT.exe");
        var binding = CreateBinding(package) with { ResolvedPackageFullName = "Contoso.SampleApp_26.930.2377.0_x64__abc123" };
        Guid id = Guid.NewGuid();
        var config = new AppConfiguration
        {
            Rules =
            [
                new RoutingRule
                {
                    Id = id,
                    Enabled = true,
                    Type = RuleType.Application,
                    Name = "ChatGPT",
                    Target = staleTarget,
                    Mode = RouteMode.Vpn,
                    PackagedBinding = binding,
                },
            ],
        };

        var resolver = FakeFor(package);
        PackagedApplicationRuleRebindResult result = PackagedApplicationRuleRebinder.TryRebind(config, resolver);
        Assert.True(result.Changed);
        RoutingRule updated = result.Config.Rules.Single();
        Assert.Equal(id, updated.Id);
        Assert.Equal("ChatGPT", updated.Name);
        Assert.True(updated.Enabled);
        Assert.Equal(RouteMode.Vpn, updated.Mode);
        Assert.Equal(ApplicationRulesHelper.NormalizeExePath(package.ExecutablePath), updated.Target);
        Assert.Equal(package.PackageFullName, updated.PackagedBinding!.ResolvedPackageFullName);
    }

    [Fact]
    public void Same_packaged_identity_with_different_target_is_duplicate()
    {
        using TempPackage package = CreatePackage("26.941.0.0", "app\\ChatGPT.exe");
        var binding = CreateBinding(package);
        string otherPath = Path.Combine(Path.GetTempPath(), "other.exe");
        var existing = new RoutingRule
        {
            Id = Guid.NewGuid(),
            Enabled = true,
            Type = RuleType.Application,
            Name = "ChatGPT",
            Target = otherPath,
            Mode = RouteMode.Vpn,
            PackagedBinding = binding,
        };
        var config = new AppConfiguration { Rules = [existing] };
        DiscoveredApplication discovered = CreateDiscoveredApp(
            package.ExecutablePath,
            "ChatGPT",
            AppId,
            package.PackageFullName);

        ApplicationRuleAddResult add = ApplicationRulesHelper.TryAddApplicationRuleFromDiscovery(config, discovered);
        Assert.True(add.IsDuplicate);
    }

    [Fact]
    public void Different_application_id_in_same_pfn_is_not_duplicate()
    {
        using TempPackage package = CreatePackage("26.941.0.0", "app\\ChatGPT.exe", ("OtherApp", "app\\Other.exe"));
        File.WriteAllBytes(Path.Combine(package.InstallRoot, "app", "Other.exe"), [0x4D, 0x5A]);

        var binding = CreateBinding(package);
        var config = ConfigWithPackagedRule(package.ExecutablePath, binding);
        string otherExe = Path.Combine(package.InstallRoot, "app", "Other.exe");
        DiscoveredApplication discovered = CreateDiscoveredApp(
            otherExe,
            "Other",
            "OtherApp",
            package.PackageFullName,
            "app/Other.exe");

        ApplicationRuleAddResult add = ApplicationRulesHelper.TryAddApplicationRuleFromDiscovery(config, discovered);
        Assert.True(add.Ok);
        Assert.Equal(2, add.Config!.Rules.Count);
    }

    [Fact]
    public void Missing_package_keeps_rule_without_rebind_change()
    {
        string staleTarget = @"C:\Program Files\WindowsApps\missing\app\ChatGPT.exe";
        var binding = new PackagedApplicationBinding
        {
            PackageFamilyName = Pfn,
            ApplicationId = AppId,
            RelativeExecutablePath = "app/ChatGPT.exe",
            UserSid = TestUserSid,
        };
        var config = ConfigWithPackagedRule(staleTarget, binding);
        var resolver = new FakePackagedApplicationPathResolver();
        PackagedApplicationRuleRebindResult result = PackagedApplicationRuleRebinder.TryRebind(config, resolver);
        Assert.False(result.Changed);
        Assert.Equal(staleTarget, result.Config.Rules[0].Target);
    }

    [Fact]
    public void Package_returning_later_rebinds_again()
    {
        using TempPackage package = CreatePackage("26.941.0.0", "app\\ChatGPT.exe");
        var binding = CreateBinding(package);
        string staleTarget = @"C:\old\ChatGPT.exe";
        var config = ConfigWithPackagedRule(staleTarget, binding);
        var resolver = new FakePackagedApplicationPathResolver();
        PackagedApplicationRuleRebindResult missing = PackagedApplicationRuleRebinder.TryRebind(config, resolver);
        Assert.False(missing.Changed);

        resolver.AddPackage(package.ToFake());
        PackagedApplicationRuleRebindResult found = PackagedApplicationRuleRebinder.TryRebind(config, resolver);
        Assert.True(found.Changed);
        Assert.Equal(ApplicationRulesHelper.NormalizeExePath(package.ExecutablePath), found.Config.Rules[0].Target);
    }

    [Fact]
    public void Manifest_executable_change_uses_current_relative_path()
    {
        using TempPackage package = CreatePackage("26.941.0.0", "app\\ChatGPT.exe");
        WriteManifest(package.InstallRoot, AppId, @"app\ChatGPT-v2.exe");
        string v2Exe = Path.Combine(package.InstallRoot, "app", "ChatGPT-v2.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(v2Exe)!);
        File.WriteAllBytes(v2Exe, [0x4D, 0x5A]);

        var binding = CreateBinding(package) with { RelativeExecutablePath = "app/ChatGPT.exe" };
        var config = ConfigWithPackagedRule(package.ExecutablePath, binding);
        var resolver = new FakePackagedApplicationPathResolver().AddPackage(new FakeInstalledPackage
        {
            UserSid = TestUserSid,
            PackageFamilyName = Pfn,
            PackageFullName = package.PackageFullName,
            InstallRoot = package.InstallRoot,
            Applications = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [AppId] = @"app\ChatGPT-v2.exe",
            },
        });

        PackagedApplicationRuleRebindResult result = PackagedApplicationRuleRebinder.TryRebind(config, resolver);
        Assert.True(result.Changed);
        Assert.Equal(ApplicationRulesHelper.NormalizeExePath(v2Exe), result.Config.Rules[0].Target);
        Assert.Equal("app/ChatGPT-v2.exe", result.Config.Rules[0].PackagedBinding!.RelativeExecutablePath);
    }

    [Fact]
    public void Unsafe_relative_executable_is_rejected_by_fake_resolver()
    {
        using TempPackage package = CreatePackage("26.941.0.0", "app\\ChatGPT.exe");
        var binding = CreateBinding(package);
        var resolver = new FakePackagedApplicationPathResolver().AddPackage(new FakeInstalledPackage
        {
            UserSid = TestUserSid,
            PackageFamilyName = Pfn,
            PackageFullName = package.PackageFullName,
            InstallRoot = package.InstallRoot,
            Applications = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [AppId] = @"..\evil.exe",
            },
        });
        PackagedApplicationPathResolveResult resolved = resolver.Resolve(binding);
        Assert.False(resolved.Found);
    }

    [Fact]
    public void Resolved_path_outside_install_root_is_rejected()
    {
        using TempPackage package = CreatePackage("26.941.0.0", "app\\ChatGPT.exe");
        var binding = CreateBinding(package);
        var resolver = new FakePackagedApplicationPathResolver().AddPackage(new FakeInstalledPackage
        {
            UserSid = TestUserSid,
            PackageFamilyName = Pfn,
            PackageFullName = package.PackageFullName,
            InstallRoot = package.InstallRoot,
            Applications = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [AppId] = @"..\..\outside.exe",
            },
        });
        PackagedApplicationPathResolveResult resolved = resolver.Resolve(binding);
        Assert.False(resolved.Found);
    }

    [Fact]
    public void Unicode_and_spaces_in_install_root_resolve()
    {
        string root = Path.Combine(Path.GetTempPath(), "Packaged Binding Tests", "пакет 01");
        Directory.CreateDirectory(Path.Combine(root, "app"));
        string exe = Path.Combine(root, "app", "App.exe");
        File.WriteAllBytes(exe, [0x4D, 0x5A]);
        WriteManifest(root, AppId, @"app\App.exe");

        var package = new FakeInstalledPackage
        {
            UserSid = TestUserSid,
            PackageFamilyName = Pfn,
            PackageFullName = "Contoso.SampleApp_1.0.0.0_x64__abc123",
            InstallRoot = root,
            Applications = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [AppId] = @"app\App.exe" },
        };
        var resolver = new FakePackagedApplicationPathResolver().AddPackage(package);
        var binding = new PackagedApplicationBinding
        {
            PackageFamilyName = Pfn,
            ApplicationId = AppId,
            RelativeExecutablePath = "app/App.exe",
            UserSid = TestUserSid,
        };
        PackagedApplicationPathResolveResult resolved = resolver.Resolve(binding);
        Assert.True(resolved.Found);
        Assert.Equal(ApplicationRulesHelper.NormalizeExePath(exe), resolved.ResolvedExecutablePath);
    }

    [Fact]
    public void Config_json_without_binding_roundtrips()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        var config = new AppConfiguration
        {
            Rules = [RoutingRule.Create(RuleType.Application, "proc", Environment.ProcessPath!, RouteMode.Vpn)],
        };
        try
        {
            ConfigSerializer.Save(path, config);
            AppConfiguration loaded = ConfigSerializer.LoadOrDefault(path);
            Assert.Null(loaded.Rules[0].PackagedBinding);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Config_json_with_binding_roundtrips()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        var binding = new PackagedApplicationBinding
        {
            PackageFamilyName = Pfn,
            ApplicationId = AppId,
            RelativeExecutablePath = "app/ChatGPT.exe",
            UserSid = TestUserSid,
            ResolvedPackageFullName = "Contoso.SampleApp_26.941.0.0_x64__abc123",
        };
        var rule = RoutingRule.Create(RuleType.Application, "ChatGPT", @"C:\apps\ChatGPT.exe", RouteMode.Vpn) with
        {
            PackagedBinding = binding,
        };
        var config = new AppConfiguration { Rules = [rule] };
        try
        {
            ConfigSerializer.Save(path, config);
            AppConfiguration loaded = ConfigSerializer.LoadOrDefault(path);
            PackagedApplicationBinding loadedBinding = loaded.Rules[0].PackagedBinding!;
            Assert.Equal(Pfn, loadedBinding.PackageFamilyName);
            Assert.Equal(AppId, loadedBinding.ApplicationId);
            Assert.Equal(TestUserSid, loadedBinding.UserSid);
            Assert.Equal("app/ChatGPT.exe", loadedBinding.RelativeExecutablePath);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Vpn_path_collector_uses_rebound_target()
    {
        using TempPackage package = CreatePackage("26.941.0.0", "app\\ChatGPT.exe");
        var binding = CreateBinding(package);
        string staleTarget = @"C:\old\ChatGPT.exe";
        var config = ConfigWithPackagedRule(staleTarget, binding);
        AppConfiguration rebound = PackagedApplicationRuleRebinder.TryRebind(config, FakeFor(package)).Config;
        IReadOnlyList<string> paths = VpnApplicationPathCollector.Collect(rebound.Rules, paused: false);
        Assert.Contains(ApplicationRulesHelper.NormalizeExePath(package.ExecutablePath), paths);
        Assert.DoesNotContain(staleTarget, paths);
    }

    [Fact]
    public void Appx_manifest_lookup_uses_current_executable_attribute()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        WriteManifest(root, AppId, @"app\FromManifest.exe");
        Assert.True(AppxManifestApplicationLookup.TryGetExecutableRelativePath(root, AppId, out string relative));
        Assert.Equal(Path.Combine("app", "FromManifest.exe"), relative);
    }

    [Fact]
    public void Appx_manifest_rejects_traversal_executable()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        WriteManifest(root, AppId, @"..\..\evil.exe");
        Assert.False(AppxManifestApplicationLookup.TryGetExecutableRelativePath(root, AppId, out _));
    }

    [Fact]
    public void Binding_identity_ignores_package_full_name_version()
    {
        var left = new PackagedApplicationBinding
        {
            PackageFamilyName = Pfn,
            ApplicationId = AppId,
            UserSid = TestUserSid,
            ResolvedPackageFullName = "Contoso.SampleApp_26.930.0.0_x64__abc123",
        };
        var right = left with { ResolvedPackageFullName = "Contoso.SampleApp_26.941.0.0_x64__abc123" };
        Assert.True(PackagedApplicationRuleIdentity.AreSameLogicalApplication(left, right));
    }

    [Fact]
    public void Resolver_uses_explicit_user_sid_key()
    {
        using TempPackage package = CreatePackage("26.941.0.0", "app\\ChatGPT.exe");
        var binding = CreateBinding(package) with { UserSid = AlternateUserSid };
        var resolver = FakeFor(package);
        Assert.False(resolver.Resolve(binding).Found);

        var bindingForUser = binding with { UserSid = TestUserSid };
        Assert.True(resolver.Resolve(bindingForUser).Found);
    }

    [Fact]
    public void Attach_existing_route_modes_matches_packaged_rule_by_binding_not_stale_path()
    {
        using TempPackage package = CreatePackage("26.941.0.0", "app\\ChatGPT.exe");
        var binding = CreateBinding(package);
        string staleTarget = @"C:\old\ChatGPT.exe";
        var rule = new RoutingRule
        {
            Id = Guid.NewGuid(),
            Enabled = true,
            Type = RuleType.Application,
            Name = "ChatGPT",
            Target = staleTarget,
            Mode = RouteMode.Vpn,
            PackagedBinding = binding,
        };
        DiscoveredApplication discovered = CreateDiscoveredApp(
            package.ExecutablePath,
            "ChatGPT",
            AppId,
            package.PackageFullName);

        IReadOnlyList<DiscoveredApplication> enriched = ApplicationDiscoveryCatalog.AttachExistingRouteModes(
            [discovered],
            [rule]);
        Assert.True(enriched[0].IsAlreadyConfigured);
        Assert.Equal(RouteMode.Vpn, enriched[0].ExistingRouteMode!.Value);
    }

    private static AppConfiguration ConfigWithPackagedRule(string target, PackagedApplicationBinding binding) =>
        new()
        {
            Rules =
            [
                new RoutingRule
                {
                    Id = Guid.NewGuid(),
                    Enabled = true,
                    Type = RuleType.Application,
                    Name = "ChatGPT",
                    Target = target,
                    Mode = RouteMode.Vpn,
                    PackagedBinding = binding,
                },
            ],
        };

    private static DiscoveredApplication CreateDiscoveredApp(
        string executablePath,
        string displayName,
        string applicationId,
        string packageFullName,
        string relativeExecutablePath = "app/ChatGPT.exe") =>
        new()
        {
            Id = PackagedApplicationRuleIdentity.FormatBindingKey(new PackagedApplicationBinding
            {
                PackageFamilyName = Pfn,
                ApplicationId = applicationId,
                UserSid = TestUserSid,
            }),
            DisplayName = displayName,
            ExecutablePath = executablePath,
            Publisher = "Contoso",
            PackageIdentity = new PackagedApplicationIdentity
            {
                PackageFamilyName = Pfn,
                ApplicationId = applicationId,
                RelativeExecutablePath = relativeExecutablePath,
                PackageFullName = packageFullName,
            },
        };

    private static PackagedApplicationBinding CreateBinding(TempPackage package) =>
        new()
        {
            PackageFamilyName = Pfn,
            ApplicationId = AppId,
            RelativeExecutablePath = "app/ChatGPT.exe",
            UserSid = TestUserSid,
            ResolvedPackageFullName = package.PackageFullName,
        };

    private static FakePackagedApplicationPathResolver FakeFor(TempPackage package) =>
        new FakePackagedApplicationPathResolver().AddPackage(package.ToFake());

    private static TempPackage CreatePackage(string version, string relativeExecutable, params (string AppId, string Relative)[] extraApps)
    {
        string installRoot = Path.Combine(Path.GetTempPath(), "PackagedBinding", Guid.NewGuid().ToString("N"));
        string exeRelative = relativeExecutable.Replace('/', Path.DirectorySeparatorChar);
        string exePath = Path.Combine(installRoot, exeRelative);
        Directory.CreateDirectory(Path.GetDirectoryName(exePath)!);
        File.WriteAllBytes(exePath, [0x4D, 0x5A]);
        WriteManifest(installRoot, AppId, exeRelative);

        var apps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [AppId] = exeRelative };
        foreach ((string id, string rel) in extraApps)
        {
            apps[id] = rel.Replace('/', Path.DirectorySeparatorChar);
        }

        return new TempPackage(
            installRoot,
            $"Contoso.SampleApp_{version}_x64__abc123",
            exeRelative,
            exePath,
            apps);
    }

    private static void WriteManifest(string installRoot, string applicationId, string executable)
    {
        string manifest = $"""
                           <?xml version="1.0" encoding="utf-8"?>
                           <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
                             <Applications>
                               <Application Id="{applicationId}" Executable="{executable.Replace('\\', '/')}"/>
                             </Applications>
                           </Package>
                           """;
        File.WriteAllText(Path.Combine(installRoot, "AppxManifest.xml"), manifest);
    }

    private sealed class TempPackage : IDisposable
    {
        public TempPackage(
            string installRoot,
            string packageFullName,
            string relativeExecutable,
            string executablePath,
            Dictionary<string, string> applications)
        {
            InstallRoot = installRoot;
            PackageFullName = packageFullName;
            RelativeExecutable = relativeExecutable;
            ExecutablePath = executablePath;
            Applications = applications;
        }

        public string InstallRoot { get; }
        public string PackageFullName { get; }
        public string RelativeExecutable { get; }
        public string ExecutablePath { get; }
        public Dictionary<string, string> Applications { get; }

        public FakeInstalledPackage ToFake() =>
            new()
            {
                UserSid = TestUserSid,
                PackageFamilyName = Pfn,
                PackageFullName = PackageFullName,
                InstallRoot = InstallRoot,
                Applications = Applications,
            };

        public void Dispose()
        {
            try
            {
                Directory.Delete(InstallRoot, recursive: true);
            }
            catch (Exception)
            {
            }
        }
    }
}
