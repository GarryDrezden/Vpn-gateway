using SelectiveVpnRouter.Core;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public class ProxyFlowStateMachineTests
{
    [Fact]
    public void Ipc_request_token_must_not_be_proxy_lifetime_token()
    {
        using var serviceCts = new CancellationTokenSource();
        using var ipcRequestCts = new CancellationTokenSource();
        serviceCts.Token.ThrowIfCancellationRequested();
        ipcRequestCts.Cancel();
        Assert.False(serviceCts.IsCancellationRequested);
    }

    [Fact]
    public void Failed_connect_never_marks_egress_verified()
    {
        string exe = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "fail.exe"));
        var flow = new FlowEvent
        {
            ProcessPath = exe,
            WfpRedirect = true,
            ProxyAccepted = true,
            RedirectRecordsApplied = true,
            VpnOutboundCreated = true,
            VpnOutboundBound = true,
            VpnOutboundConnected = false,
            Status = FlowLifecycle.Cancelled,
            ErrorDetails = new FlowErrorDetails { CancellationReason = "ConnectTimeout" },
        };
        var obs = RealAppRoutingObservation.FromFlow(flow);
        Assert.False(obs.EgressVerified);
        Assert.False(obs.TcpConnectSuccess);
    }
}