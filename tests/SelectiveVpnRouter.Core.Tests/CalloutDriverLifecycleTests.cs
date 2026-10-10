using SelectiveVpnRouter.Network;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public sealed class CalloutDriverLifecycleTests
{
    [Fact]
    public void Sc_query_state_line_shape_matches_parser_expectations()
    {
        const string raw = """
            SERVICE_NAME: SelectiveVpnCallout
            TYPE               : 1  KERNEL_DRIVER
            STATE              : 4  RUNNING
            """;
        int state = ParseStateFromSample(raw);
        Assert.Equal(4, state);
    }

    [Fact]
    public void Already_running_queried_state_is_success_without_start()
    {
        Assert.True(CalloutDriverLifecycle.IsDriverServiceRunning(4));
        Assert.False(CalloutDriverLifecycle.IsDriverServiceRunning(1));
    }

    [Fact]
    public void Start_outcome_treats_exit_1056_as_already_running()
    {
        Assert.True(CalloutDriverLifecycle.IsAlreadyRunningStartOutcome(1056, null));
        Assert.True(CalloutDriverLifecycle.IsAlreadyRunningStartOutcome(1, 1056));
        Assert.False(CalloutDriverLifecycle.IsAlreadyRunningStartOutcome(1060, null));
    }

    [Fact]
    public void Start_outcome_parses_localized_sc_error_number_without_win32_prefix()
    {
        const string raw = """
            [SC] StartService: ошибка: 1056:
            Одна копия службы уже запущена.
            """;
        Assert.Equal(1056, CalloutDriverLifecycle.TryParseScWin32(raw));
    }

    [Fact]
    public void Start_outcome_parses_english_win32_prefix()
    {
        const string raw = "[SC] StartService FAILED 1056:\nWIN32: 1056";
        Assert.Equal(1056, CalloutDriverLifecycle.TryParseScWin32(raw));
    }

    [Fact]
    public void Genuine_start_failure_when_not_running_and_not_1056()
    {
        Assert.False(CalloutDriverLifecycle.IsAlreadyRunningStartOutcome(1060, 1060));
        Assert.False(CalloutDriverLifecycle.IsDriverServiceRunning(1));
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

    private static int ParseStateFromSample(string raw)
    {
        foreach (string line in raw.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("STATE", StringComparison.OrdinalIgnoreCase))
            {
                int colon = trimmed.IndexOf(':');
                if (colon >= 0)
                {
                    string afterColon = trimmed[(colon + 1)..].Trim();
                    int space = afterColon.IndexOf(' ');
                    string token = space >= 0 ? afterColon[..space] : afterColon;
                    if (int.TryParse(token, out int code))
                    {
                        return code;
                    }
                }
            }
        }

        return -1;
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
