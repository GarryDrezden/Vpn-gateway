using SelectiveVpnRouter.Core.Portable;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public sealed class PublishRuntimeCalloutRegistrationPlannerTests
{
    private const string PublishRoot = @"C:\dev\artifacts\publish\SelectiveVpnRouter";
    private const string DriverUnderPublish = @"C:\dev\artifacts\publish\SelectiveVpnRouter\driver\SelectiveVpnCallout.sys";
    private const string DriverOutside = @"C:\Windows\System32\drivers\SelectiveVpnCallout.sys";

    [Fact]
    public void Missing_callout_with_publish_sys_registers_before_product_start()
    {
        var snap = ProductWillStartSnapshot(driverInstalled: false, driverWasRunning: false);
        CalloutDriverRegistrationAction action = PublishRuntimeCalloutRegistrationPlanner.PlanRegistration(
            snap,
            PublishRoot,
            publishedSysExists: true,
            driverServiceInstalled: false,
            registeredImagePath: null,
            canonicalPublishedSysPath: DriverUnderPublish);

        Assert.Equal(CalloutDriverRegistrationAction.RegisterAtPublish, action);
    }

    [Fact]
    public void Wrong_ImagePath_repairs_to_publish_before_product_start()
    {
        var snap = ProductWillStartSnapshot(driverInstalled: true, driverWasRunning: false);
        CalloutDriverRegistrationAction action = PublishRuntimeCalloutRegistrationPlanner.PlanRegistration(
            snap,
            PublishRoot,
            publishedSysExists: true,
            driverServiceInstalled: true,
            registeredImagePath: DriverOutside,
            canonicalPublishedSysPath: DriverUnderPublish);

        Assert.Equal(CalloutDriverRegistrationAction.RepairToPublish, action);
    }

    [Fact]
    public void Canonical_path_keeps_registration_without_recreate()
    {
        var snap = ProductWillStartSnapshot(driverInstalled: true, driverWasRunning: true);
        CalloutDriverRegistrationAction action = PublishRuntimeCalloutRegistrationPlanner.PlanRegistration(
            snap,
            PublishRoot,
            publishedSysExists: true,
            driverServiceInstalled: true,
            registeredImagePath: DriverUnderPublish,
            canonicalPublishedSysPath: DriverUnderPublish);

        Assert.Equal(CalloutDriverRegistrationAction.KeepCanonical, action);
    }

    [Fact]
    public void Missing_publish_sys_fails_before_product_start()
    {
        var snap = ProductWillStartSnapshot(driverInstalled: false, driverWasRunning: false);
        CalloutDriverRegistrationAction action = PublishRuntimeCalloutRegistrationPlanner.PlanRegistration(
            snap,
            PublishRoot,
            publishedSysExists: false,
            driverServiceInstalled: false,
            registeredImagePath: null,
            canonicalPublishedSysPath: DriverUnderPublish);

        Assert.Equal(CalloutDriverRegistrationAction.FailPublishedSysMissing, action);
    }

    [Fact]
    public void DriverWasRunning_false_still_ensures_registration_when_product_will_start()
    {
        var snap = ProductWillStartSnapshot(driverInstalled: false, driverWasRunning: false);
        PublishRuntimeRestorePlan plan = PublishRuntimeLifecyclePlanner.PlanRestore(snap);

        Assert.False(plan.StartDriver);
        Assert.True(plan.StartProduct);
        Assert.True(plan.EnsureCalloutRegistrationBeforeProductStart);
        Assert.True(PublishRuntimeCalloutRegistrationPlanner.RestoreMustEnsureCalloutBeforeProductStart(plan));
    }

    [Fact]
    public void Restore_ordering_ensures_callout_before_product_start()
    {
        string lifecycle = File.ReadAllText(FindLifecycleScriptPath());
        int ensureIndex = lifecycle.IndexOf(
            "Repair-SvrPublishCalloutDriverRegistration -PublishDir $PublishDir -Snapshot $Snapshot -RequirePublishedSys",
            StringComparison.Ordinal);
        int productStartIndex = lifecycle.IndexOf("Start-SvrPublishedService", ensureIndex, StringComparison.Ordinal);
        int driverStartIndex = lifecycle.IndexOf(
            "Start-SvrWindowsServiceForPublish -ServiceName $script:SvrDriverServiceName",
            ensureIndex,
            StringComparison.Ordinal);

        Assert.True(ensureIndex >= 0);
        Assert.True(productStartIndex > ensureIndex);
        Assert.True(driverStartIndex > ensureIndex);
        Assert.True(productStartIndex > driverStartIndex);
    }

    [Fact]
    public void Publish_repair_script_registers_missing_callout_when_sys_present()
    {
        string lifecycle = File.ReadAllText(FindLifecycleScriptPath());
        Assert.Contains("registering missing SelectiveVpnCallout", lifecycle, StringComparison.Ordinal);
        Assert.Contains("callout ImagePath already canonical", lifecycle, StringComparison.Ordinal);
        Assert.Contains("RequirePublishedSys", lifecycle, StringComparison.Ordinal);
    }

    private static PublishRuntimeSnapshot ProductWillStartSnapshot(bool driverInstalled, bool driverWasRunning) =>
        new(
            ProductInstalled: true,
            ProductWasRunning: true,
            ProductImagePath: Path.Combine(PublishRoot, "SelectiveVpnRouter.Service.exe"),
            DriverInstalled: driverInstalled,
            DriverWasRunning: driverWasRunning,
            DriverImagePath: driverInstalled ? DriverUnderPublish : null);

    private static string FindLifecycleScriptPath()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "SelectiveVpnRouter.sln")))
            {
                return Path.Combine(dir, "scripts", "svr-publish-runtime-lifecycle.ps1");
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("SelectiveVpnRouter.sln not found");
    }
}
