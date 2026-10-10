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
