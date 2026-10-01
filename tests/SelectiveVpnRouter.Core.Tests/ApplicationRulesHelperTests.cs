using SelectiveVpnRouter.Core;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public class ApplicationRulesHelperTests
{
    [Fact]
    public void Normalize_exe_path_uses_full_path_and_trims()
    {
        string normalized = ApplicationRulesHelper.NormalizeExePath(@"C:\Temp\..\Windows\System32\cmd.exe");
        Assert.Equal(Path.GetFullPath(@"C:\Windows\System32\cmd.exe"), normalized);
    }

    [Fact]
    public void Duplicate_app_detection_is_case_insensitive_on_full_path()
    {
        string exe = Environment.ProcessPath!;
        var config = new AppConfiguration { Rules = [RoutingRule.Create(RuleType.Application, "proc", exe, RouteMode.Vpn)] };
        Assert.True(ApplicationRulesHelper.IsDuplicateApplicationRule(config.Rules, exe.ToUpperInvariant()));
    }

    [Fact]
    public void Friendly_app_name_falls_back_to_filename_without_extension()
    {
        Assert.Equal("MyTool", ApplicationRulesHelper.ResolveFriendlyAppName(@"C:\missing\MyTool.exe"));
    }

    [Fact]
    public void Create_permanent_application_rule_defaults_to_vpn()
    {
        var result = ApplicationRulesHelper.TryAddApplicationRule(new AppConfiguration(), Environment.ProcessPath!);
        Assert.True(result.Ok);
        Assert.Equal(RouteMode.Vpn, result.Rule!.Mode);
    }

    [Fact]
    public void Duplicate_add_returns_duplicate_flag()
    {
        string exe = Environment.ProcessPath!;
        var config = new AppConfiguration { Rules = [RoutingRule.Create(RuleType.Application, "proc", exe, RouteMode.Vpn)] };
        var result = ApplicationRulesHelper.TryAddApplicationRule(config, exe);
        Assert.True(result.IsDuplicate);
    }

    [Fact]
    public void Update_application_rule_vpn_to_direct_and_back()
    {
        string exe = Environment.ProcessPath!;
        var rule = RoutingRule.Create(RuleType.Application, "proc", exe, RouteMode.Vpn);
        var config = new AppConfiguration { Rules = [rule] };
        var direct = ApplicationRulesHelper.TrySetApplicationRouteMode(config, rule.Id, RouteMode.Direct);
        var vpn = ApplicationRulesHelper.TrySetApplicationRouteMode(direct!, rule.Id, RouteMode.Vpn);
        Assert.Equal(RouteMode.Vpn, vpn!.Rules.Single().Mode);
    }

    [Fact]
    public void Delete_one_application_rule_preserves_others()
    {
        var a = RoutingRule.Create(RuleType.Application, "a", Environment.ProcessPath!, RouteMode.Vpn);
        var b = RoutingRule.Create(RuleType.Application, "b", @"C:\other\app.exe", RouteMode.Direct);
        var config = new AppConfiguration { Rules = [a, b] };
        var updated = ApplicationRulesHelper.TryRemoveApplicationRule(config, a.Id);
        Assert.Single(updated!.Rules);
        Assert.Equal(b.Id, updated.Rules[0].Id);
    }

    [Fact]
    public void Permanent_apps_excludes_domain_and_cidr_rules()
    {
        var config = new AppConfiguration
        {
            Rules =
            [
                RoutingRule.Create(RuleType.Application, "proc", Environment.ProcessPath!, RouteMode.Vpn),
                RoutingRule.Create(RuleType.Domain, "yt", "*.youtube.com", RouteMode.Vpn),
            ],
        };
        Assert.Single(ApplicationRulesHelper.GetPermanentApplicationRules(config));
    }

    [Fact]
    public void Merge_rules_combines_application_and_advanced_rules_for_ipc_payload()
    {
        var app = RoutingRule.Create(RuleType.Application, "proc", Environment.ProcessPath!, RouteMode.Vpn);
        var domain = RoutingRule.Create(RuleType.Domain, "yt", "*.youtube.com", RouteMode.Vpn);
        var merged = ApplicationRulesHelper.MergeRules(new AppConfiguration(), [app], [domain]);
        Assert.Equal(2, merged.Rules.Count);
    }

    [Fact]
    public void Diagnostic_tmp_iso_probe_in_temp_are_excluded_from_permanent_apps()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "svr-iso-" + Guid.NewGuid().ToString("N"), "vpn");
        string exe = Path.Combine(tempRoot, ProbeCopyHelper.ProbeExeName);
        var config = new AppConfiguration
        {
            Rules =
            [
                RoutingRule.Create(RuleType.Application, "tmp-iso-vpn", exe, RouteMode.Vpn),
                RoutingRule.Create(RuleType.Application, "Chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe", RouteMode.Vpn),
            ],
        };

        IReadOnlyList<RoutingRule> permanent = ApplicationRulesHelper.GetPermanentApplicationRules(config);
        Assert.Single(permanent);
        Assert.Equal("Chrome", permanent[0].Name);
    }


    [Fact]
    public void Regression_A_persistent_vpn_app_is_visible()
    {
        var rule = RoutingRule.Create(RuleType.Application, "Chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe", RouteMode.Vpn);
        var config = new AppConfiguration { Rules = [rule] };
        IReadOnlyList<RoutingRule> permanent = ApplicationRulesHelper.GetPermanentApplicationRules(config);
        Assert.Single(permanent);
        Assert.Equal(RouteMode.Vpn, permanent[0].Mode);
    }

    [Fact]
    public void Regression_B_persistent_direct_app_is_visible()
    {
        var rule = RoutingRule.Create(RuleType.Application, "Steam", @"C:\Program Files (x86)\Steam\steam.exe", RouteMode.Direct);
        var config = new AppConfiguration { Rules = [rule] };
        IReadOnlyList<RoutingRule> permanent = ApplicationRulesHelper.GetPermanentApplicationRules(config);
        Assert.Single(permanent);
        Assert.Equal(RouteMode.Direct, permanent[0].Mode);
    }

    [Fact]
    public void Regression_C_two_persistent_apps_count_is_two()
    {
        var config = new AppConfiguration
        {
            Rules =
            [
                RoutingRule.Create(RuleType.Application, "Chrome", @"C:\Apps\chrome.exe", RouteMode.Vpn),
                RoutingRule.Create(RuleType.Application, "Steam", @"C:\Apps\steam.exe", RouteMode.Direct),
            ],
        };
        Assert.Equal(2, ApplicationRulesHelper.CountPermanentApplications(config.Rules));
        Assert.Equal(1, ApplicationRulesHelper.CountVpnRoutedApplications(config.Rules));
    }

    [Fact]
    public void Regression_D_temp_diagnostic_app_is_invisible()
    {
        string exe = Path.Combine(Path.GetTempPath(), "svr-iso-test", "vpn", ProbeCopyHelper.ProbeExeName);
        var rule = RoutingRule.Create(RuleType.Application, "tmp-iso-vpn", exe, RouteMode.Vpn);
        Assert.True(ApplicationRulesHelper.IsDiagnosticApplicationRule(rule));
        Assert.Empty(ApplicationRulesHelper.GetPermanentApplicationRules(new AppConfiguration { Rules = [rule] }));
    }

    [Fact]
    public void Regression_E_tmp_rule_does_not_suppress_persistent_rule()
    {
        string exe = Path.Combine(Path.GetTempPath(), "svr-iso-" + Guid.NewGuid().ToString("N"), "vpn", ProbeCopyHelper.ProbeExeName);
        var config = new AppConfiguration
        {
            Rules =
            [
                RoutingRule.Create(RuleType.Application, "tmp-iso-vpn", exe, RouteMode.Vpn),
                RoutingRule.Create(RuleType.Application, "Chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe", RouteMode.Vpn),
                RoutingRule.Create(RuleType.Application, "Steam", @"C:\Program Files (x86)\Steam\steam.exe", RouteMode.Direct),
            ],
        };
        IReadOnlyList<RoutingRule> permanent = ApplicationRulesHelper.GetPermanentApplicationRules(config);
        Assert.Equal(2, permanent.Count);
        Assert.Contains(permanent, r => r.Name == "Chrome");
        Assert.Contains(permanent, r => r.Name == "Steam");
    }

    [Fact]
    public void Regression_F_path_normalization_does_not_remove_valid_rule()
    {
        string rawPath = @"C:\Apps\.\Chrome\chrome.exe";
        string normalizedPath = ApplicationRulesHelper.NormalizeExePath(rawPath);
        var config = new AppConfiguration
        {
            Rules = [RoutingRule.Create(RuleType.Application, "Chrome", normalizedPath, RouteMode.Vpn)],
        };
        Assert.Single(ApplicationRulesHelper.GetPermanentApplicationRules(config));
        Assert.NotNull(ApplicationRulesHelper.FindApplicationRuleByPath(config.Rules, rawPath));
    }
    [Fact]
    public void FilterApplicationRules_supports_search_and_mode()
    {
        var rules = new[]
        {
            RoutingRule.Create(RuleType.Application, "Google Chrome", @"C:\Chrome\chrome.exe", RouteMode.Vpn),
            RoutingRule.Create(RuleType.Application, "Steam", @"C:\Steam\steam.exe", RouteMode.Direct),
        };

        var vpnOnly = ApplicationRulesHelper.FilterApplicationRules(rules, null, RouteMode.Vpn).ToArray();
        Assert.Single(vpnOnly);

        var search = ApplicationRulesHelper.FilterApplicationRules(rules, "steam", null).ToArray();
        Assert.Single(search);
        Assert.Equal("Steam", search[0].Name);
    }
}