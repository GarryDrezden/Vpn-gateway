using SelectiveVpnRouter.Core;
using SelectiveVpnRouter.Network;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public class WfpRegressionTests
{
    [Fact]
    public void Failed_filter_with_zero_id_is_not_filter_ready()
    {
        var filter = new WfpFilterInstallResult
        {
            ExePath = @"C:\Apps\Probe.exe",
            FileExists = true,
            AppIdResolved = true,
            FilterInstalled = false,
            FilterId = 0,
            IsCalloutFilter = true,
            Error = "FwpmFilterAdd0 failed",
        };

        Assert.False(WfpPolicyHealth.IsExeFilterReady(filter));
    }

    [Fact]
    public void Filter_installed_flag_without_id_is_not_filter_ready()
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

    [Fact]
    public void Abi_expectation_includes_condition_value_offset_24()
    {
        WfpAbiVerification.LayoutExpectation expectation =
            WfpAbiVerification.NativeExpectations.First(e => e.Name == "FWPM_FILTER_CONDITION0");
        Assert.Equal(24, expectation.Offsets!["conditionValue"]);
    }

    [Fact]
    public void Temp_extra_exe_path_is_included_without_permanent_rule()
    {
        string temp = Path.Combine(Path.GetTempPath(), "svr-temp-app-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            string tempExe = Path.Combine(temp, "browser.exe");
            File.WriteAllText(tempExe, "fake");

            IReadOnlyList<string> paths = VpnApplicationPathCollector.Collect([], paused: false, extraVpnExePaths: [tempExe]);
            Assert.Single(paths);
            Assert.Equal(Path.GetFullPath(tempExe), paths[0], StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(temp, true); } catch { }
        }
    }
}