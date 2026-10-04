using Xunit;
using SelectiveVpnRouter.Core.Portable;

namespace SelectiveVpnRouter.Core.Tests;

public class PublishRuntimeLifecyclePlannerTests
{
    private const string PublishRoot = @"C:\dev\artifacts\publish\SelectiveVpnRouter";
    private const string DriverUnderPublish = @"C:\dev\artifacts\publish\SelectiveVpnRouter\driver\SelectiveVpnCallout.sys";
    private const string DriverOutside = @"C:\Windows\System32\drivers\SelectiveVpnCallout.sys";
    private const string DriverReleaseBuild = @"C:\dev\artifacts\driver\Release\SelectiveVpnCallout.sys";

    [Fact]
    public void Scenario_A_both_running_restores_both()
    {
        var snap = Snapshot(productRunning: true, driverRunning: true);
        PublishRuntimeRestorePlan plan = PublishRuntimeLifecyclePlanner.PlanRestore(snap);

        Assert.True(plan.StartDriver);
        Assert.True(plan.StartProduct);
        Assert.True(PublishRuntimeLifecyclePlanner.ShouldStopDriverForPublishSwap(snap, PublishRoot));
    }

    [Fact]
    public void Scenario_B_service_running_driver_stopped_keeps_driver_stopped()
    {
        var snap = Snapshot(productRunning: true, driverRunning: false);
        PublishRuntimeRestorePlan plan = PublishRuntimeLifecyclePlanner.PlanRestore(snap);

        Assert.False(plan.StartDriver);
        Assert.True(plan.StartProduct);
        Assert.False(PublishRuntimeLifecyclePlanner.ShouldStopDriverForPublishSwap(snap, PublishRoot));
    }

    [Fact]
    public void Scenario_C_service_stopped_driver_running_restores_only_driver()
    {
        var snap = Snapshot(productRunning: false, driverRunning: true);
        PublishRuntimeRestorePlan plan = PublishRuntimeLifecyclePlanner.PlanRestore(snap);

        Assert.True(plan.StartDriver);
        Assert.False(plan.StartProduct);
    }

    [Fact]
    public void Scenario_D_both_stopped_does_not_start_either()
    {
        var snap = Snapshot(productRunning: false, driverRunning: false);
        PublishRuntimeRestorePlan plan = PublishRuntimeLifecyclePlanner.PlanRestore(snap);

        Assert.False(plan.StartDriver);
        Assert.False(plan.StartProduct);
    }

    [Fact]
    public void Scenario_E_restore_plan_is_stable_for_rollback()
    {
        var snap = Snapshot(productRunning: true, driverRunning: true);
        PublishRuntimeRestorePlan before = PublishRuntimeLifecyclePlanner.PlanRestore(snap);
        PublishRuntimeRestorePlan afterFailure = PublishRuntimeLifecyclePlanner.PlanRestore(snap);

        Assert.Equal(before, afterFailure);
    }

    [Fact]
    public void Scenario_F_driver_under_publish_tree_participates_in_swap()
    {
        string retiredRoot = PublishRoot + ".retired-20260101120000000";
        string driverOnRetired = Path.Combine(retiredRoot, "driver", "SelectiveVpnCallout.sys");

        Assert.True(PublishRuntimeLifecyclePlanner.ShouldParticipateInPublishSwap(DriverUnderPublish, PublishRoot));
        Assert.True(PublishRuntimeLifecyclePlanner.ShouldStopDriverForRetiredCleanup(
            driverInstalled: true,
            driverRunning: true,
            driverImagePath: driverOnRetired,
            retiredRoot: retiredRoot));
    }

    [Fact]
    public void Scenario_G_driver_outside_publish_tree_is_not_stopped_for_swap()
    {
        var snap = new PublishRuntimeSnapshot(
            ProductInstalled: true,
            ProductWasRunning: true,
            ProductImagePath: Path.Combine(PublishRoot, "SelectiveVpnRouter.Service.exe"),
            DriverInstalled: true,
            DriverWasRunning: true,
            DriverImagePath: DriverOutside);

        Assert.False(PublishRuntimeLifecyclePlanner.ShouldStopDriverForPublishSwap(snap, PublishRoot));
        Assert.False(PublishRuntimeLifecyclePlanner.ShouldParticipateInPublishSwap(DriverOutside, PublishRoot));
    }

    [Fact]
    public void Kernel_image_path_prefix_is_normalized_for_under_root_checks()
    {
        string kernelPath = @"\??\" + DriverUnderPublish;
        Assert.True(PublishRuntimeLifecyclePlanner.ShouldParticipateInPublishSwap(kernelPath, PublishRoot));
    }

    [Fact]
    public void Scenario_H_release_build_driver_does_not_repair_to_publish_layout()
    {
        var snap = new PublishRuntimeSnapshot(
            ProductInstalled: true,
            ProductWasRunning: true,
            ProductImagePath: Path.Combine(PublishRoot, "SelectiveVpnRouter.Service.exe"),
            DriverInstalled: true,
            DriverWasRunning: true,
            DriverImagePath: DriverReleaseBuild);

        Assert.False(PublishRuntimeLifecyclePlanner.ShouldRepairDriverImagePathToPublishLayout(snap, PublishRoot));
    }

    [Fact]
    public void Scenario_I_publish_layout_driver_repairs_to_publish_layout()
    {
        var snap = new PublishRuntimeSnapshot(
            ProductInstalled: true,
            ProductWasRunning: true,
            ProductImagePath: Path.Combine(PublishRoot, "SelectiveVpnRouter.Service.exe"),
            DriverInstalled: true,
            DriverWasRunning: true,
            DriverImagePath: DriverUnderPublish);

        Assert.True(PublishRuntimeLifecyclePlanner.ShouldRepairDriverImagePathToPublishLayout(snap, PublishRoot));
    }

    private static PublishRuntimeSnapshot Snapshot(bool productRunning, bool driverRunning) =>
        new(
            ProductInstalled: true,
            ProductWasRunning: productRunning,
            ProductImagePath: Path.Combine(PublishRoot, "SelectiveVpnRouter.Service.exe"),
            DriverInstalled: true,
            DriverWasRunning: driverRunning,
            DriverImagePath: DriverUnderPublish);
}
