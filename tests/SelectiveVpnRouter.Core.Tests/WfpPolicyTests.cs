using SelectiveVpnRouter.Core;
using SelectiveVpnRouter.Network;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public class WfpPolicyTests
{
    [Fact]
    public void Fwp_action_constants_match_windows_sdk()
    {
        Assert.Equal(0x00001001u, WfpActionConstants.FwpActionBlock);
        Assert.Equal(0x00001002u, WfpActionConstants.FwpActionPermit);
        Assert.Equal(0x00004005u, WfpActionConstants.FwpActionCalloutUnknown);
        Assert.Equal(0x80320024u, WfpActionConstants.FwpEInvalidActionType);
    }

    [Fact]
    public void Loopback_permit_plan_uses_fwp_action_permit_not_raw_two()
    {
        Assert.Equal(WfpActionConstants.FwpActionPermit, WfpActionDiagnostics.LoopbackPermitV4ActionType);
        Assert.NotEqual(2u, WfpActionDiagnostics.LoopbackPermitV4ActionType);
    }

    [Fact]
    public void Invalid_action_type_hresult_is_mapped()
    {
        Assert.Contains("FWP_E_INVALID_ACTION_TYPE", WfpPolicyHealth.DescribeStatus(WfpActionConstants.FwpEInvalidActionType));
    }

    [Fact]
    public void Loopback_permit_live_smoke_installs_on_windows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        if (!IsElevatedForWfpIntegration())
        {
            return;
        }

        WfpLoopbackPermitSmokeResult result = WfpLoopbackPermitFilterSmokeTest.Run();
        Assert.Equal(WfpActionConstants.FwpActionPermit, result.ActionTypeUsed);
        Assert.True(
            result.Success,
            result.Error ?? ("FilterAddStatus=0x" + result.FilterAddStatus.ToString("X8") + " " + result.DiagnosticLine));
    }

    [Fact]
    public void Nonexistent_exe_is_not_filter_ready()
    {
        var filter = new WfpFilterInstallResult
        {
            ExePath = @"C:\missing\Probe.exe",
            FileExists = false,
            IsCalloutFilter = true,
        };
        Assert.False(WfpPolicyHealth.IsExeFilterReady(filter));
    }

    [Fact]
    public void Successful_filter_result_is_filter_ready()
    {
        var filter = new WfpFilterInstallResult
        {
            ExePath = @"C:\Apps\Probe.exe",
            FileExists = true,
            AppIdResolved = true,
            FilterInstalled = true,
            FilterId = 42,
            IsCalloutFilter = true,
            Role = WfpFilterRole.RedirectCallout,
        };
        Assert.True(WfpPolicyHealth.IsExeFilterReady(filter));
    }

    [Fact]
    public void Vpn_app_filter_plan_includes_loopback_permit_and_redirect()
    {
        IReadOnlyList<WfpFilterRole> roles = WfpVpnAppFilterPlanner.RequiredFiltersForVpnRoutedApp();
        Assert.Equal(2, roles.Count);
        Assert.Contains(WfpFilterRole.LoopbackPermitV4, roles);
        Assert.Contains(WfpFilterRole.RedirectCallout, roles);
    }

    [Fact]
    public void Loopback_permit_has_higher_weight_than_redirect()
    {
        Assert.True(WfpVpnAppFilterPlanner.LoopbackPermitHasHigherPriorityThanRedirect());
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.0.0.2")]
    [InlineData("127.255.255.254")]
    public void Loopback_permit_matches_127_slash_8_hosts(string host)
    {
        Assert.True(WfpLoopbackIpv4.MatchesPermitRange(host));
    }

    [Fact]
    public void External_ipv4_does_not_match_loopback_permit()
    {
        Assert.False(WfpLoopbackIpv4.MatchesPermitRange("8.8.8.8"));
        Assert.True(WfpVpnAppFilterPlanner.ExternalRemoteAddressMatchesLoopbackPermit("8.8.8.8"));
    }

    [Fact]
    public void Direct_app_gets_no_vpn_wfp_filters()
    {
        Assert.Empty(WfpVpnAppFilterPlanner.RequiredFiltersForDirectRoutedApp());
    }

    [Fact]
    public void Loopback_permit_ready_requires_role_and_filter_id()
    {
        var ok = new WfpFilterInstallResult
        {
            Role = WfpFilterRole.LoopbackPermitV4,
            FileExists = true,
            AppIdResolved = true,
            FilterInstalled = true,
            FilterId = 99,
        };
        Assert.True(WfpPolicyHealth.IsLoopbackPermitReady(ok));
        Assert.False(WfpPolicyHealth.IsLoopbackPermitReady(ok with { FilterId = 0 }));
    }

    [Fact]
    public void Vpn_app_wfp_ready_requires_both_filters()
    {
        string exe = @"C:\Apps\Probe.exe";
        var policy = new WfpPolicyDiagnostics
        {
            Filters =
            [
                new WfpFilterInstallResult
                {
                    ExePath = exe,
                    Role = WfpFilterRole.RedirectCallout,
                    IsCalloutFilter = true,
                    FileExists = true,
                    AppIdResolved = true,
                    FilterInstalled = true,
                    FilterId = 1,
                },
                new WfpFilterInstallResult
                {
                    ExePath = exe,
                    Role = WfpFilterRole.LoopbackPermitV4,
                    FileExists = true,
                    AppIdResolved = true,
                    FilterInstalled = true,
                    FilterId = 2,
                },
            ],
        };
        Assert.True(WfpPolicyHealth.IsVpnAppWfpReady(policy, exe));
        Assert.False(WfpPolicyHealth.IsVpnAppWfpReady(policy with { Filters = policy.Filters.Take(1).ToArray() }, exe));
    }

    [Fact]
    public void Find_loopback_permit_filter_matches_exe_path()
    {
        string exe = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Probe.exe"));
        var policy = new WfpPolicyDiagnostics
        {
            Filters =
            [
                new WfpFilterInstallResult
                {
                    ExePath = exe,
                    Role = WfpFilterRole.LoopbackPermitV4,
                    FilterInstalled = true,
                    FilterId = 11,
                    FileExists = true,
                    AppIdResolved = true,
                },
            ],
        };
        Assert.NotNull(WfpPolicyHealth.FindLoopbackPermitFilter(policy, exe));
    }

    [Fact]
    public void Policy_apply_failure_is_not_healthy()
    {
        var result = new WfpPolicyApplyResult
        {
            RequestedVpnApps = 1,
            InstalledAppFilters = 0,
        };
        Assert.False(WfpPolicyHealth.IsHealthy(result));
    }

    [Fact]
    public void DescribeStatus_maps_invalid_weight()
    {
        Assert.Contains("FWP_E_INVALID_WEIGHT", WfpPolicyHealth.DescribeStatus(0x80320025));
    }

    [Fact]
    public void Vpn_path_collector_uses_full_path_and_skips_direct_rules()
    {
        string temp = Path.Combine(Path.GetTempPath(), "svr-wfp-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            string vpnExe = Path.Combine(temp, "vpn", "SelectiveVpnRouter.Probe.exe");
            string directExe = Path.Combine(temp, "direct", "SelectiveVpnRouter.Probe.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(vpnExe)!);
            Directory.CreateDirectory(Path.GetDirectoryName(directExe)!);
            File.WriteAllText(vpnExe, "fake");
            File.WriteAllText(directExe, "fake");

            RoutingRule[] rules =
            [
                RoutingRule.Create(RuleType.Application, "vpn-probe", vpnExe, RouteMode.Vpn),
                RoutingRule.Create(RuleType.Application, "direct-probe", directExe, RouteMode.Direct),
            ];

            IReadOnlyList<string> paths = VpnApplicationPathCollector.Collect(rules, paused: false);
            Assert.Single(paths);
            Assert.Equal(Path.GetFullPath(vpnExe), paths[0], StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(temp, true); } catch { }
        }
    }

    [Fact]
    public void FindCalloutFilter_matches_full_path_case_insensitive()
    {
        string exe = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Probe.exe"));
        var policy = new WfpPolicyDiagnostics
        {
            Filters =
            [
                new WfpFilterInstallResult
                {
                    ExePath = exe.ToUpperInvariant(),
                    IsCalloutFilter = true,
                    FilterInstalled = true,
                    FilterId = 7,
                    FileExists = true,
                    AppIdResolved = true,
                },
            ],
        };

        WfpFilterInstallResult? found = WfpPolicyHealth.FindCalloutFilter(policy, exe.ToLowerInvariant());
        Assert.NotNull(found);
        Assert.Equal(7UL, found!.FilterId);
    }

    [Fact]
    public void Vpn_path_collector_includes_extra_temp_exe_without_rule()
    {
        string temp = Path.Combine(Path.GetTempPath(), "svr-extra-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            string extraExe = Path.Combine(temp, "curl.exe");
            File.WriteAllText(extraExe, "fake");

            IReadOnlyList<string> paths = VpnApplicationPathCollector.Collect([], paused: false, extraVpnExePaths: [extraExe]);
            Assert.Single(paths);
            Assert.Equal(Path.GetFullPath(extraExe), paths[0], StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(temp, true); } catch { }
        }
    }

    [Fact]
    public void Filter_ready_requires_nonzero_filter_id()
    {
        var filter = new WfpFilterInstallResult
        {
            ExePath = @"C:\Apps\Probe.exe",
            FileExists = true,
            AppIdResolved = true,
            FilterInstalled = true,
            FilterId = 0,
            IsCalloutFilter = true,
        };
        Assert.False(WfpPolicyHealth.IsExeFilterReady(filter));
    }

    private static bool IsElevatedForWfpIntegration()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var principal = new System.Security.Principal.WindowsPrincipal(identity);
        return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }
}