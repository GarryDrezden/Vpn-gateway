using SelectiveVpnRouter.Core.Portable;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public sealed class InstallIntegrationVerifyPlannerTests
{
    private const string PublishRoot = @"C:\dev\artifacts\publish\SelectiveVpnRouter";

    [Fact]
    public void ProductWasRunning_false_skips_live_service_integration_verify()
    {
        var snap = Snapshot(productRunning: false);
        Assert.True(InstallIntegrationVerifyPlanner.ShouldSkipLiveServiceIntegrationVerify(snap));
        Assert.False(InstallIntegrationVerifyPlanner.ShouldRequireLiveServiceIntegrationVerify(snap));
    }

    [Fact]
    public void ProductWasRunning_true_requires_live_service_integration_verify()
    {
        var snap = Snapshot(productRunning: true);
        Assert.False(InstallIntegrationVerifyPlanner.ShouldSkipLiveServiceIntegrationVerify(snap));
        Assert.True(InstallIntegrationVerifyPlanner.ShouldRequireLiveServiceIntegrationVerify(snap));
    }

    [Fact]
    public void Install_helpers_capture_pre_update_snapshot_before_update_desktop()
    {
        string install = ReadInstallScript();
        int snapshotIdx = install.IndexOf("Get-SvrPublishRuntimeSnapshot", StringComparison.Ordinal);
        int mutationIdx = install.IndexOf("Set-VpnRouteInstallMutationStarted", StringComparison.Ordinal);
        int updateIdx = install.IndexOf("& $updateScript", StringComparison.Ordinal);
        Assert.True(snapshotIdx >= 0 && mutationIdx > snapshotIdx && updateIdx > mutationIdx);
        Assert.Contains("-PreUpdateSnapshot $preUpdateRuntimeSnapshot", install, StringComparison.Ordinal);
    }

    [Fact]
    public void Install_helpers_use_process_capture_for_node_not_erroraction_stderr()
    {
        string helpers = ReadHelpersScript();
        Assert.Contains("function Invoke-VpnRouteNodeCommand", helpers, StringComparison.Ordinal);
        Assert.Contains("Start-Process -FilePath $node", helpers, StringComparison.Ordinal);
        Assert.Contains("RedirectStandardError", helpers, StringComparison.Ordinal);
        Assert.False(
            System.Text.RegularExpressions.Regex.IsMatch(
                helpers,
                @"Invoke-VpnRouteReadOnlyIntegrationVerify[\s\S]*& node @verifyArgs 2>&1",
                System.Text.RegularExpressions.RegexOptions.None));
    }

    [Fact]
    public void ProductWasRunning_false_logs_explicit_live_verify_skip()
    {
        string helpers = ReadHelpersScript();
        Assert.Contains("integration API live verify", helpers, StringComparison.Ordinal);
        Assert.Contains("product service intentionally preserved stopped", helpers, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductWasRunning_true_requires_running_service_before_full_verify()
    {
        string helpers = ReadHelpersScript();
        Assert.Contains("product service was expected Running after update", helpers, StringComparison.Ordinal);
        Assert.Contains("--require-live-service", helpers, StringComparison.Ordinal);
        Assert.Contains("--require-browser-routing-push", helpers, StringComparison.Ordinal);
    }

    private static PublishRuntimeSnapshot Snapshot(bool productRunning) =>
        new(
            ProductInstalled: true,
            ProductWasRunning: productRunning,
            ProductImagePath: Path.Combine(PublishRoot, "SelectiveVpnRouter.Service.exe"),
            DriverInstalled: true,
            DriverWasRunning: false,
            DriverImagePath: Path.Combine(PublishRoot, "driver", "SelectiveVpnCallout.sys"));

    private static string ReadInstallScript() => ReadRepoScript("scripts", "install-vpn-route.ps1");

    private static string ReadHelpersScript() => ReadRepoScript("scripts", "_install-vpn-route-helpers.ps1");

    private static string ReadRepoScript(params string[] parts)
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            string candidate = Path.Combine(new[] { dir }.Concat(parts).ToArray());
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException(string.Join("/", parts) + " not found");
    }
}
