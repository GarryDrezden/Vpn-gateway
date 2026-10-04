using SelectiveVpnRouter.Core;
using SelectiveVpnRouter.Core.ApplicationDiscovery;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public class PackagedRoutingTargetTests
{
    private const string Pfn = "Contoso.SampleApp_abc123";
    private const string AppId = "App";
    private static string TestUserSid => InteractiveUserSid.TryGetCurrent() ?? "S-1-5-21-0000000000-0000000000-0000000000-1001";

    [Fact]
    public void Single_visible_app_includes_hidden_manifest_helper_not_in_discovery()
    {
        using TempPackage package = CreatePackage(
            ("App", @"app\ChatGPT.exe", null),
            ("CodexCoreCommandRunner", @"app\resources\codex-command-runner.exe", "none"));

        var binding = CreateBinding(package, AppId);
        var rule = VpnRule("ChatGPT", package.ExecutablePath, binding);
        PackagedRoutingTargetsForRule plan = PackagedRoutingTargetResolver.ResolveForRule(rule, FakeFor(package));

        Assert.Equal(2, plan.Targets.Count);
        Assert.Contains(plan.Targets, t => t.Kind == PackagedRoutingTargetKind.Primary && t.ApplicationId == "App");
        Assert.Contains(plan.Targets, t => t.Kind == PackagedRoutingTargetKind.AssociatedHelper && t.ApplicationId == "CodexCoreCommandRunner");

        Assert.False(PackagedApplicationHeuristics.IsUserFacingApplicationEntry(
            "CodexCoreCommandRunner",
            "none",
            @"app\resources\codex-command-runner.exe"));
    }

    [Fact]
    public void Vpn_rule_collects_primary_and_helper_wfp_paths()
    {
        using TempPackage package = CreatePackage(
            ("App", @"app\ChatGPT.exe", null),
            ("CodexCoreCommandRunner", @"app\resources\codex-command-runner.exe", "none"));

        var rule = VpnRule("ChatGPT", package.ExecutablePath, CreateBinding(package, AppId));
        IReadOnlyList<string> paths = VpnApplicationPathCollector.Collect([rule], paused: false, packagedResolver: FakeFor(package));

        Assert.Equal(2, paths.Count);
        Assert.Contains(package.ExecutablePath, paths, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(package.HelperExecutablePath!, paths, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Two_visible_app_ids_remain_separate_rules()
    {
        using TempPackage package = CreatePackage(
            ("App", @"app\ChatGPT.exe", null),
            ("OtherApp", @"app\Other.exe", null));

        string otherExe = Path.Combine(package.InstallRoot, "app", "Other.exe");
        File.WriteAllBytes(otherExe, [0x4D, 0x5A]);

        var chatRule = VpnRule("ChatGPT", package.ExecutablePath, CreateBinding(package, "App"));
        var otherRule = VpnRule("Other", otherExe, CreateBinding(package, "OtherApp") with { RelativeExecutablePath = "app/Other.exe" });

        PackagedRoutingTargetsForRule chatPlan = PackagedRoutingTargetResolver.ResolveForRule(chatRule, FakeFor(package));
        PackagedRoutingTargetsForRule otherPlan = PackagedRoutingTargetResolver.ResolveForRule(otherRule, FakeFor(package));

        Assert.Single(chatPlan.Targets);
        Assert.Single(otherPlan.Targets);
    }

    [Fact]
    public void Hidden_helper_with_other_visible_id_does_not_attach_to_unrelated_sibling()
    {
        using TempPackage package = CreatePackage(
            ("App", @"app\ChatGPT.exe", null),
            ("OtherApp", @"app\Other.exe", null),
            ("OtherAppRunner", @"app\OtherAppRunner.exe", "none"));

        string otherExe = Path.Combine(package.InstallRoot, "app", "Other.exe");
        File.WriteAllBytes(otherExe, [0x4D, 0x5A]);
        File.WriteAllBytes(Path.Combine(package.InstallRoot, "app", "OtherAppRunner.exe"), [0x4D, 0x5A]);

        var chatRule = VpnRule("ChatGPT", package.ExecutablePath, CreateBinding(package, "App"));
        PackagedRoutingTargetsForRule chatPlan = PackagedRoutingTargetResolver.ResolveForRule(chatRule, FakeFor(package));

        Assert.DoesNotContain(chatPlan.Targets, t => t.ApplicationId == "OtherAppRunner");
    }

    [Fact]
    public void Helper_missing_after_update_primary_still_routes_and_reports_unresolved()
    {
        using TempPackage package = CreatePackage(
            ("App", @"app\ChatGPT.exe", null),
            ("CodexCoreCommandRunner", @"app\resources\codex-command-runner.exe", "none"));

        File.Delete(package.HelperExecutablePath!);

        var rule = VpnRule("ChatGPT", package.ExecutablePath, CreateBinding(package, AppId));
        PackagedRoutingTargetsForRule plan = PackagedRoutingTargetResolver.ResolveForRule(rule, FakeFor(package));

        Assert.Single(plan.VpnWfpExecutablePaths);
        PackagedRoutingTarget helper = Assert.Single(plan.Targets, t => t.Kind == PackagedRoutingTargetKind.AssociatedHelper);
        Assert.False(helper.Resolved);
    }

    [Fact]
    public void Win32_rule_stays_single_path()
    {
        string exe = Environment.ProcessPath!;
        var rule = VpnRule("proc", exe, null);
        PackagedRoutingTargetsForRule plan = PackagedRoutingTargetResolver.ResolveForRule(rule, null);
        Assert.Single(plan.Targets);
        Assert.Single(plan.VpnWfpExecutablePaths);
    }

    [Fact]
    public void Packaged_index_groups_helper_flow_under_primary_and_logical_name()
    {
        using TempPackage package = CreatePackage(
            ("App", @"app\ChatGPT.exe", null),
            ("CodexCoreCommandRunner", @"app\resources\codex-command-runner.exe", "none"));

        var rule = VpnRule("ChatGPT", package.ExecutablePath, CreateBinding(package, AppId));
        PackagedRoutingTargetIndex index = PackagedRoutingTargetIndex.Build([rule], FakeFor(package));

        Assert.True(index.TryGetLogicalRule(package.HelperExecutablePath!, out RoutingRule? logical, out string? primary));
        Assert.Equal("ChatGPT", logical!.Name);
        Assert.Equal(package.ExecutablePath, primary, StringComparer.OrdinalIgnoreCase);

        var flow = new FlowEvent { ProcessPath = package.HelperExecutablePath!, Route = FlowRoute.Vpn };
        Assert.Equal("ChatGPT", ApplicationRulesHelper.ResolveFlowApplicationDisplayName(flow, index));
        Assert.Equal(package.ExecutablePath, index.NormalizeConnectionsGroupKey(package.HelperExecutablePath!), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void MatchProcess_matches_associated_helper_to_packaged_vpn_rule()
    {
        using TempPackage package = CreatePackage(
            ("App", @"app\ChatGPT.exe", null),
            ("CodexCoreCommandRunner", @"app\resources\codex-command-runner.exe", "none"));

        var rule = VpnRule("ChatGPT", package.ExecutablePath, CreateBinding(package, AppId));
        PackagedRoutingTargetIndex index = PackagedRoutingTargetIndex.Build([rule], FakeFor(package));

        ProcessMatch match = RuleEvaluator.MatchProcess([rule], package.HelperExecutablePath!, index);
        Assert.Equal(RouteMode.Vpn, match.EffectiveMode);
        Assert.Equal("ChatGPT", match.Rule?.Name);
    }

    [Fact]
    public void OpenAI_codex_live_package_includes_command_runner_helper_when_installed()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string primary = @"C:\Program Files\WindowsApps\OpenAI.Codex_26.930.2377.0_x64__2p2nqsd0c76g0\app\ChatGPT.exe";
        if (!File.Exists(primary))
        {
            return;
        }

        var binding = new PackagedApplicationBinding
        {
            PackageFamilyName = "OpenAI.Codex_2p2nqsd0c76g0",
            ApplicationId = "App",
            RelativeExecutablePath = "app/ChatGPT.exe",
            UserSid = InteractiveUserSid.TryGetCurrent() ?? TestUserSid,
        };
        var rule = VpnRule("ChatGPT", primary, binding);
        PackagedRoutingTargetsForRule plan = PackagedRoutingTargetResolver.ResolveForRule(rule, new WindowsPackagedApplicationPathResolver());

        PackagedRoutingTarget helper = Assert.Single(
            plan.Targets,
            t => t.Kind == PackagedRoutingTargetKind.AssociatedHelper && t.ApplicationId == "CodexCoreCommandRunner");
        Assert.True(helper.Resolved);
        Assert.EndsWith(@"codex-command-runner.exe", helper.ExecutablePath, StringComparison.OrdinalIgnoreCase);
    }

    private static RoutingRule VpnRule(string name, string target, PackagedApplicationBinding? binding) =>
        new()
        {
            Id = Guid.NewGuid(),
            Enabled = true,
            Type = RuleType.Application,
            Name = name,
            Target = target,
            Mode = RouteMode.Vpn,
            PackagedBinding = binding,
        };

    private static PackagedApplicationBinding CreateBinding(TempPackage package, string applicationId) =>
        new()
        {
            PackageFamilyName = Pfn,
            ApplicationId = applicationId,
            RelativeExecutablePath = applicationId == AppId ? "app/ChatGPT.exe" : "app/Other.exe",
            UserSid = TestUserSid,
            ResolvedPackageFullName = package.PackageFullName,
        };

    private static FakePackagedApplicationPathResolver FakeFor(TempPackage package) =>
        new FakePackagedApplicationPathResolver().AddPackage(package.ToFake());

    private static TempPackage CreatePackage(params (string AppId, string RelativeExe, string? AppListEntry)[] apps)
    {
        string installRoot = Path.Combine(Path.GetTempPath(), "PackagedRouting", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(installRoot);

        var appMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? primaryPath = null;
        string? helperPath = null;

        foreach ((string appId, string relativeExe, string? appListEntry) in apps)
        {
            string rel = relativeExe.Replace('/', Path.DirectorySeparatorChar);
            string full = Path.Combine(installRoot, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, [0x4D, 0x5A]);
            appMap[appId] = rel;
            if (appId == AppId)
            {
                primaryPath = full;
            }

            if (appId == "CodexCoreCommandRunner")
            {
                helperPath = full;
            }
        }

        WriteManifest(installRoot, apps);

        return new TempPackage(installRoot, "Contoso.SampleApp_1.0.0.0_x64__abc123", primaryPath!, helperPath, appMap);
    }

    private static void WriteManifest(string installRoot, (string AppId, string RelativeExe, string? AppListEntry)[] apps)
    {
        var lines = apps.Select(a =>
        {
            string entry = a.AppListEntry is null ? "" : $" AppListEntry=\"{a.AppListEntry}\"";
            string exe = a.RelativeExe.Replace('\\', '/');
            return $"    <Application Id=\"{a.AppId}\" Executable=\"{exe}\"{entry} />";
        });
        string manifest = $"""
                           <?xml version="1.0" encoding="utf-8"?>
                           <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
                             <Applications>
                           {string.Join(Environment.NewLine, lines)}
                             </Applications>
                           </Package>
                           """;
        File.WriteAllText(Path.Combine(installRoot, "AppxManifest.xml"), manifest);
    }

    private sealed class TempPackage : IDisposable
    {
        public TempPackage(string installRoot, string packageFullName, string executablePath, string? helperExecutablePath, Dictionary<string, string> applications)
        {
            InstallRoot = installRoot;
            PackageFullName = packageFullName;
            ExecutablePath = executablePath;
            HelperExecutablePath = helperExecutablePath;
            Applications = applications;
        }

        public string InstallRoot { get; }
        public string PackageFullName { get; }
        public string ExecutablePath { get; }
        public string? HelperExecutablePath { get; }
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
