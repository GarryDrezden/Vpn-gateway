using SelectiveVpnRouter.Core;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public class RealAppRoutingObservationTests
{
    private static FlowEvent SuccessFlow(string exe) => new()
    {
        ProcessPath = exe,
        Pid = 1234,
        Destination = "93.184.216.34",
        Port = 443,
        WfpRedirect = true,
        ProxyAccepted = true,
        RedirectRecordsApplied = true,
        VpnOutboundCreated = true,
        VpnOutboundBound = true,
        VpnOutboundConnected = true,
        Route = FlowRoute.Vpn,
        Status = FlowLifecycle.Closed,
    };

    [Fact]
    public void Egress_verified_requires_connected_terminal_status()
    {
        string exe = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "real-app.exe"));
        var obs = RealAppRoutingObservation.FromFlow(SuccessFlow(exe));
        Assert.Equal(RealAppRoutingObservation.EgressVerified, RealAppRoutingObservation.ComputeState(obs));
    }

    [Fact]
    public void Connect_timeout_with_routing_chain_is_warning_not_pass()
    {
        string exe = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "warn.exe"));
        var flow = SuccessFlow(exe) with
        {
            VpnOutboundConnected = false,
            Status = FlowLifecycle.Cancelled,
            ErrorDetails = new FlowErrorDetails
            {
                Phase = "connect-original-destination",
                ExceptionType = nameof(OperationCanceledException),
                CancellationReason = "ConnectTimeout",
                TimeoutMs = 15000,
                Message = "Outbound connect timed out after 15000 ms.",
            },
        };
        var obs = RealAppRoutingObservation.FromFlow(flow);
        Assert.Equal(RealAppRoutingObservation.Warning, RealAppRoutingObservation.ComputeState(obs));
        Assert.False(obs.TcpConnectSuccess);
    }

    [Fact]
    public void Waiting_when_no_selected_flow()
    {
        Assert.Equal(RealAppRoutingObservation.Waiting, RealAppRoutingObservation.ComputeState(null));
    }

    [Fact]
    public void Routing_observed_without_connected_outbound()
    {
        string exe = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "routing.exe"));
        var flow = SuccessFlow(exe) with
        {
            VpnOutboundConnected = false,
            Status = FlowLifecycle.Connecting,
        };
        var obs = RealAppRoutingObservation.FromFlow(flow);
        Assert.Equal(RealAppRoutingObservation.RoutingObserved, RealAppRoutingObservation.ComputeState(obs));
        Assert.True(obs.RoutingObserved);
        Assert.False(obs.TcpConnectSuccess);
    }

    [Fact]
    public void Bound_outbound_without_connected_is_not_tcp_success()
    {
        string exe = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "bound.exe"));
        var flow = SuccessFlow(exe) with { VpnOutboundConnected = false, Status = FlowLifecycle.OutboundBound };
        var obs = RealAppRoutingObservation.FromFlow(flow);
        Assert.True(obs.RoutingObserved);
        Assert.False(obs.TcpConnectSuccess);
    }
}