using SelectiveVpnRouter.Core.Portable;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public class PortableBootstrapRepairPlannerTests
{
    [Fact]
    public void Correct_driver_running_skips_destructive_repair()
    {
        string sys = @"C:\publish\SelectiveVpnRouter\driver\SelectiveVpnCallout.sys";
        var driver = new DriverProbeSnapshot
        {
            Installed = true,
            Running = true,
            BinaryPath = sys,
        };
        Assert.True(PortableBootstrapRepairPlanner.ShouldSkipDriverRepair(driver, sys));
        Assert.False(PortableBootstrapRepairPlanner.ShouldUpdateDriverBinPathInPlace(driver, sys));
    }

    [Fact]
    public void Correct_driver_stopped_only_needs_start()
    {
        string sys = @"C:\publish\SelectiveVpnRouter\driver\SelectiveVpnCallout.sys";
        var driver = new DriverProbeSnapshot
        {
            Installed = true,
            Running = false,
            BinaryPath = sys,
        };
        Assert.False(PortableBootstrapRepairPlanner.ShouldSkipDriverRepair(driver, sys));
        Assert.True(PortableBootstrapRepairPlanner.ShouldOnlyStartDriver(driver, sys));
    }

    [Fact]
    public void Wrong_driver_path_updates_in_place()
    {
        string expected = @"C:\publish\SelectiveVpnRouter\driver\SelectiveVpnCallout.sys";
        var driver = new DriverProbeSnapshot
        {
            Installed = true,
            Running = true,
            BinaryPath = @"C:\dev\artifacts\driver\staging\Release\SelectiveVpnCallout.sys",
        };
        Assert.True(PortableBootstrapRepairPlanner.ShouldUpdateDriverBinPathInPlace(driver, expected));
        Assert.False(PortableBootstrapRepairPlanner.ShouldSkipDriverRepair(driver, expected));
    }

    [Fact]
    public void Product_service_running_on_expected_path_skips_restart()
    {
        string service = @"C:\publish\SelectiveVpnRouter\SelectiveVpnRouter.Service.exe";
        var snap = new ServiceProbeSnapshot
        {
            Installed = true,
            Running = true,
            ImagePath = "\"" + service + "\"",
        };
        Assert.True(PortableBootstrapRepairPlanner.ShouldSkipProductServiceRestart(snap, service));
    }
}
