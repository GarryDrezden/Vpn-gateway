using SelectiveVpnRouter.Core;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public class FlowPresentationHelperTests
{
    [Fact]
    public void Temp_probe_flows_are_not_user_visible()
    {
        string tempExe = Path.Combine(Path.GetTempPath(), "svr-iso-test", ProbeCopyHelper.ProbeExeName);
        var flow = new FlowEvent
        {
            ProcessPath = tempExe,
            Destination = "1.2.3.4",
            Port = 443,
            Route = FlowRoute.Vpn,
            Status = "Closed",
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        Assert.False(FlowPresentationHelper.IsUserVisibleFlow(flow));
    }

    [Fact]
    public void SelectUserFlows_limits_and_orders()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var flows = new[]
        {
            new FlowEvent { ProcessPath = @"C:\apps\a.exe", Destination = "1.1.1.1", Port = 443, UpdatedAt = now.AddMinutes(-1) },
            new FlowEvent { ProcessPath = @"C:\apps\b.exe", Destination = "2.2.2.2", Port = 443, UpdatedAt = now },
        };

        var picked = FlowPresentationHelper.SelectUserFlows(flows, 1).ToArray();
        Assert.Single(picked);
        Assert.Contains("b.exe", picked[0].ProcessPath, StringComparison.OrdinalIgnoreCase);
    }
}