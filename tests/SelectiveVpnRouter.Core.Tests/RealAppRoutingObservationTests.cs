using SelectiveVpnRouter.Core;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public class RealAppRoutingObservationTests
{
    [Fact]
    public void Pass_requires_wfp_proxy_redirect_and_vpn_bound_outbound()
    {
        string exe = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "real-app.exe"));
        var flows = new List<FlowEvent>
        {
            new()
            {
                ProcessPath = exe,
                Pid = 1234,
                Destination = "93.184.216.34",
                Port = 443,
                WfpRedirect = true,
                RedirectRecordsApplied = true,
                Route = FlowRoute.Vpn,
                Status = "connected",
            },
        };

        Assert.Equal(RealAppRoutingObservation.Pass, RealAppRoutingObservation.ComputeState(flows, exe));
    }

    [Fact]
    public void Waiting_when_rule_active_but_no_flows()
    {
        string exe = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "waiting.exe"));
        Assert.Equal(RealAppRoutingObservation.Waiting, RealAppRoutingObservation.ComputeState([], exe));
    }

    [Fact]
    public void Partial_when_wfp_redirect_without_full_chain()
    {
        string exe = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "partial.exe"));
        var flows = new List<FlowEvent>
        {
            new()
            {
                ProcessPath = exe,
                WfpRedirect = true,
                RedirectRecordsApplied = false,
                Route = FlowRoute.Direct,
                Status = "pending",
            },
        };

        Assert.Equal(RealAppRoutingObservation.Partial, RealAppRoutingObservation.ComputeState(flows, exe));
    }
}