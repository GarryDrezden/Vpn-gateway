using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public sealed class PublishRuntimeScmSyncScriptTests
{
    [Fact]
    public void Set_SvrKernelDriverBinPath_waits_for_scm_absence_before_create()
    {
        string lifecycle = File.ReadAllText(FindLifecycleScriptPath());
        int setBinPath = lifecycle.IndexOf("function Set-SvrKernelDriverBinPath", StringComparison.Ordinal);
        Assert.True(setBinPath >= 0);
        int nextFunction = lifecycle.IndexOf("function Stop-SvrWindowsServiceForPublish", setBinPath, StringComparison.Ordinal);
        Assert.True(nextFunction > setBinPath);
        string body = lifecycle[setBinPath..nextFunction];

        Assert.Contains("delete $($script:SvrDriverServiceName)", body, StringComparison.Ordinal);
        Assert.Contains("Wait-SvrScmServiceAbsent", body, StringComparison.Ordinal);
        Assert.Contains("Invoke-SvrScCreateKernelCalloutDriver", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Start-Sleep -Milliseconds 400", body, StringComparison.Ordinal);

        int deleteIndex = body.IndexOf("delete $($script:SvrDriverServiceName)", StringComparison.Ordinal);
        int waitIndex = body.IndexOf("Wait-SvrScmServiceAbsent", deleteIndex, StringComparison.Ordinal);
        int createIndex = body.IndexOf("Invoke-SvrScCreateKernelCalloutDriver", waitIndex, StringComparison.Ordinal);
        Assert.True(deleteIndex >= 0 && waitIndex > deleteIndex && createIndex > waitIndex);
    }

    [Fact]
    public void Sc_create_retries_1072_after_scm_absence_confirmation()
    {
        string lifecycle = File.ReadAllText(FindLifecycleScriptPath());
        int createHelper = lifecycle.IndexOf("function Invoke-SvrScCreateKernelCalloutDriver", StringComparison.Ordinal);
        Assert.True(createHelper >= 0);
        int nextFunction = lifecycle.IndexOf("function Set-SvrKernelDriverBinPath", createHelper, StringComparison.Ordinal);
        string body = lifecycle[createHelper..nextFunction];

        Assert.Contains("Test-SvrScMarkedForDeleteFailure", body, StringComparison.Ordinal);
        Assert.Contains("1072", body, StringComparison.Ordinal);
        Assert.Contains("Wait-SvrScmServiceAbsent", body, StringComparison.Ordinal);
        Assert.Contains("marked for delete", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Scm_absence_wait_polls_service_controller_not_fixed_sleep_only()
    {
        string lifecycle = File.ReadAllText(FindLifecycleScriptPath());
        int waitHelper = lifecycle.IndexOf("function Wait-SvrScmServiceAbsent", StringComparison.Ordinal);
        Assert.True(waitHelper >= 0);
        int nextFunction = lifecycle.IndexOf("function Test-SvrScMarkedForDeleteFailure", waitHelper, StringComparison.Ordinal);
        string body = lifecycle[waitHelper..nextFunction];

        Assert.Contains("Test-SvrServiceRegisteredInScm", body, StringComparison.Ordinal);
        Assert.Contains("PollIntervalMs", body, StringComparison.Ordinal);
        Assert.Contains("while ((Get-Date) -lt $deadline)", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_callout_registration_uses_create_helper_with_1072_retry()
    {
        string lifecycle = File.ReadAllText(FindLifecycleScriptPath());
        int repair = lifecycle.IndexOf("function Repair-SvrPublishCalloutDriverRegistration", StringComparison.Ordinal);
        Assert.True(repair >= 0);
        int nextFunction = lifecycle.IndexOf("function Restore-SvrPublishRuntimeFromSnapshot", repair, StringComparison.Ordinal);
        string body = lifecycle[repair..nextFunction];

        Assert.Contains("Invoke-SvrScCreateKernelCalloutDriver", body, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Invoke-SvrSc -ArgumentString \"create $($script:SvrDriverServiceName)",
            body,
            StringComparison.Ordinal);
    }

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
