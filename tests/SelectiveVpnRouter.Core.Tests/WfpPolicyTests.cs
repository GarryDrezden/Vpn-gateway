using SelectiveVpnRouter.Core;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public class WfpPolicyTests
{
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
        };
        Assert.True(WfpPolicyHealth.IsExeFilterReady(filter));
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
}