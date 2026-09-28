using System.Net;
using System.Net.Sockets;
using SelectiveVpnRouter.Core;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public class FlowStatusHelperTests
{
    [Fact]
    public void OperationCanceledException_from_connect_timeout_is_not_generic_message()
    {
        var details = FlowStatusHelper.ErrorFromException(
            "connect-original-destination",
            new OperationCanceledException("The operation was canceled."),
            connectTimeoutRequested: true,
            serviceStopping: false,
            timeoutMs: 15000);
        Assert.Equal("ConnectTimeout", details.CancellationReason);
        Assert.Equal(15000, details.TimeoutMs);
    }

    [Fact]
    public void Terminal_error_status_prevents_connecting_regression()
    {
        Assert.True(FlowStatusHelper.IsTerminal(FlowLifecycle.Cancelled));
        Assert.True(FlowStatusHelper.IsTerminal(FlowLifecycle.Error));
        Assert.False(FlowStatusHelper.IsTerminal(FlowLifecycle.Connecting));
    }

    [Fact]
    public void SelectLatestFlow_uses_updated_at_not_mixed_records()
    {
        string exe = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "curl.exe"));
        var older = new FlowEvent
        {
            FlowId = Guid.NewGuid(),
            SequenceId = 1,
            ProcessPath = exe,
            UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            Status = FlowLifecycle.Cancelled,
            WfpRedirect = true,
            ProxyAccepted = true,
            RedirectRecordsApplied = true,
            VpnOutboundBound = true,
            ErrorDetails = new FlowErrorDetails { Phase = "connect-original-destination", CancellationReason = "ConnectTimeout" },
        };
        var newer = new FlowEvent
        {
            FlowId = Guid.NewGuid(),
            SequenceId = 2,
            ProcessPath = exe,
            UpdatedAt = DateTimeOffset.UtcNow,
            Status = FlowLifecycle.Connecting,
            WfpRedirect = true,
            ProxyAccepted = true,
            RedirectRecordsApplied = true,
            VpnOutboundBound = true,
        };

        RealAppFlowObservation? selected = FlowStatusHelper.SelectLatestFlowForExe([older, newer], exe);
        Assert.NotNull(selected);
        Assert.Equal(newer.FlowId, selected!.FlowId);
        Assert.Equal(FlowLifecycle.Connecting, selected.Status);
        Assert.Null(selected.ErrorDetails);
    }

    [Fact]
    public void Proxy_endpoint_uses_loop_rejected_reason()
    {
        var endpoint = new IPEndPoint(IPAddress.Loopback, 19000);
        Assert.Equal("proxy-endpoint", FlowStatusHelper.TryGetBypassReason(endpoint, socks: false, proxyPort: 19000));
        Assert.Equal(FlowLifecycle.LoopRejected, FlowStatusHelper.FormatBypassStatus("proxy-endpoint"));
    }
}