using System.ServiceProcess;
using SelectiveVpnRouter.Network;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public sealed class CalloutDriverLifecycleTests
{
    [Fact]
    public void Running_scm_state_maps_to_legacy_running_code()
    {
        Assert.True(CalloutDriverLifecycle.IsDriverServiceRunning((int)ServiceControllerStatus.Running));
        Assert.False(CalloutDriverLifecycle.IsDriverServiceRunning((int)ServiceControllerStatus.Stopped));
    }

    [Fact]
    public void Already_running_queried_state_is_success_without_start()
    {
        Assert.True(CalloutDriverLifecycle.IsRunningStatus(ServiceControllerStatus.Running));
        Assert.False(CalloutDriverLifecycle.IsRunningStatus(ServiceControllerStatus.Stopped));
    }

    [Fact]
    public void Start_outcome_treats_1056_as_already_running()
    {
        Assert.True(CalloutDriverLifecycle.IsAlreadyRunningStartOutcome(1056, null));
        Assert.True(CalloutDriverLifecycle.IsAlreadyRunningStartOutcome(1, 1056));
        Assert.False(CalloutDriverLifecycle.IsAlreadyRunningStartOutcome(1060, null));
    }

    [Fact]
    public void Benign_already_running_exception_shapes_are_recognized()
    {
        Assert.True(CalloutDriverLifecycle.IsBenignAlreadyRunningException(new InvalidOperationException("already running")));
        Assert.True(CalloutDriverLifecycle.IsBenignAlreadyRunningException(
            new System.ComponentModel.Win32Exception(CalloutDriverLifecycle.ErrorServiceAlreadyRunning)));
        Assert.False(CalloutDriverLifecycle.IsBenignAlreadyRunningException(new InvalidOperationException("missing service")));
    }

    [Fact]
    public void Genuine_start_failure_when_not_running_and_not_1056()
    {
        Assert.False(CalloutDriverLifecycle.IsAlreadyRunningStartOutcome(1060, 1060));
        Assert.False(CalloutDriverLifecycle.IsDriverServiceRunning((int)ServiceControllerStatus.Stopped));
    }

    [Fact]
    public void RouterEngine_source_ensures_driver_on_service_ipc_start()
    {
        string repo = FindRepoRoot()!;
        string pipeHost = File.ReadAllText(Path.Combine(repo, "src", "SelectiveVpnRouter.Service", "PipeIpcHost.cs"));
        string engine = File.ReadAllText(Path.Combine(repo, "src", "SelectiveVpnRouter.Service", "RouterEngine.cs"));
        Assert.Contains("EnsureApplicationRoutingDriver", pipeHost);
        Assert.Contains("public void EnsureApplicationRoutingDriver()", engine);
        int disconnectStart = engine.IndexOf("public async Task DisconnectAsync()", StringComparison.Ordinal);
        int disconnectEnd = engine.IndexOf("public async Task EmergencyRestoreAsync()", disconnectStart, StringComparison.Ordinal);
        Assert.True(disconnectStart >= 0 && disconnectEnd > disconnectStart);
        string disconnectBody = engine[disconnectStart..disconnectEnd];
        Assert.DoesNotContain("_driver?.Dispose()", disconnectBody, StringComparison.Ordinal);
        Assert.Contains("_driver?.Dispose()", engine, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureApplicationRoutingDriver_attempts_TryOpen_after_service_running()
    {
        string repo = FindRepoRoot()!;
        string engine = File.ReadAllText(Path.Combine(repo, "src", "SelectiveVpnRouter.Service", "RouterEngine.cs"));
        int start = engine.IndexOf("public void EnsureApplicationRoutingDriver()", StringComparison.Ordinal);
        Assert.True(start >= 0);
        string body = engine[start..(start + 1200)];
        Assert.Contains("CalloutDriverLifecycle.EnsureServiceRunning()", body, StringComparison.Ordinal);
        Assert.Contains("CalloutDriverClient.TryOpen()", body, StringComparison.Ordinal);
        Assert.Contains("if (!ensure.ServiceRunning)", body, StringComparison.Ordinal);
        Assert.Contains("if (_driver.IsLoaded)", body, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureApplicationRoutingDriver_surfaces_TryOpen_failure()
    {
        string repo = FindRepoRoot()!;
        string engine = File.ReadAllText(Path.Combine(repo, "src", "SelectiveVpnRouter.Service", "RouterEngine.cs"));
        int start = engine.IndexOf("public void EnsureApplicationRoutingDriver()", StringComparison.Ordinal);
        string body = engine[start..(start + 1200)];
        Assert.Contains("_driverLoadError = ensure.Error ??", body, StringComparison.Ordinal);
        Assert.Contains("callout-driver-unavailable", body, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureApplicationRoutingDriver_is_idempotent_when_driver_loaded()
    {
        string repo = FindRepoRoot()!;
        string engine = File.ReadAllText(Path.Combine(repo, "src", "SelectiveVpnRouter.Service", "RouterEngine.cs"));
        int start = engine.IndexOf("public void EnsureApplicationRoutingDriver()", StringComparison.Ordinal);
        string body = engine[start..(start + 400)];
        Assert.Contains("if (_driver?.IsLoaded == true)", body, StringComparison.Ordinal);
        Assert.Contains("_driverLoadError = null", body, StringComparison.Ordinal);
        Assert.Contains("return;", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Lifecycle_does_not_parse_sc_query_output_for_authoritative_state()
    {
        string repo = FindRepoRoot()!;
        string lifecycle = File.ReadAllText(Path.Combine(repo, "src", "SelectiveVpnRouter.Network", "CalloutDriverLifecycle.cs"));
        Assert.DoesNotContain("RunSc(", lifecycle, StringComparison.Ordinal);
        Assert.DoesNotContain("TryParseScWin32", lifecycle, StringComparison.Ordinal);
        Assert.Contains("ServiceController", lifecycle, StringComparison.Ordinal);
    }

    private static string? FindRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "SelectiveVpnRouter.sln")))
            {
                return dir;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        return null;
    }
}
