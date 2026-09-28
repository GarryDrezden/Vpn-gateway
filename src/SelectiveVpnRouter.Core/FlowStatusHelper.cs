using System.Net;
using System.Net.Sockets;

namespace SelectiveVpnRouter.Core;

public static class FlowLifecycle
{
    public const string Accepted = "Accepted";
    public const string RedirectContextRecovered = "RedirectContextRecovered";
    public const string OutboundCreated = "OutboundCreated";
    public const string OutboundBound = "OutboundBound";
    public const string Connecting = "Connecting";
    public const string Connected = "Connected";
    public const string Relaying = "Relaying";
    public const string Closed = "Closed";
    public const string Error = "Error";
    public const string Cancelled = "Cancelled";
    public const string LoopRejected = "loop-rejected";
    public const string LoopbackBypass = "loopback-bypass";
    public const string ProxySelfRejected = "proxy-self-rejected";
}

public static class FlowStatusHelper
{
    public const int DefaultConnectTimeoutMs = 15000;

    public static bool IsIpv4Loopback(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        byte[] bytes = address.GetAddressBytes();
        return bytes.Length == 4 && bytes[0] == 127;
    }

    public static bool IsLoopbackAddress(IPAddress address) =>
        IPAddress.IsLoopback(address) || IsIpv4Loopback(address);

    public static bool IsRejected(string status) =>
        status is FlowLifecycle.LoopRejected or FlowLifecycle.ProxySelfRejected;

    public static bool IsBypass(string status) =>
        status is FlowLifecycle.LoopRejected or FlowLifecycle.LoopbackBypass or FlowLifecycle.ProxySelfRejected;

    public static bool IsError(string status) =>
        status == FlowLifecycle.Error
        || status.StartsWith("error", StringComparison.OrdinalIgnoreCase)
        || status.Contains("SocketException", StringComparison.OrdinalIgnoreCase);

    public static bool IsCancelled(string status) => status == FlowLifecycle.Cancelled;

    public static bool IsTerminal(string status) =>
        status is FlowLifecycle.Closed or FlowLifecycle.Error or FlowLifecycle.Cancelled
        || IsBypass(status)
        || IsRejected(status);

    public static bool IsSuccessTerminal(string status) =>
        status is FlowLifecycle.Connected or FlowLifecycle.Relaying or FlowLifecycle.Closed;

    public static bool HasRoutingObserved(RealAppFlowObservation flow) =>
        flow.WfpRedirect
        && flow.ProxyAccepted
        && flow.RedirectRecordsApplied
        && flow.VpnOutboundBound;

    public static bool HasTcpConnectSuccess(RealAppFlowObservation flow) =>
        HasRoutingObserved(flow)
        && flow.VpnOutboundConnected
        && !flow.HasFlowError
        && !IsCancelled(flow.Status);

    public static FlowErrorDetails ErrorFromException(
        string phase,
        Exception ex,
        bool connectTimeoutRequested = false,
        bool serviceStopping = false,
        int? timeoutMs = null)
    {
        if (ex is OperationCanceledException)
        {
            string reason = connectTimeoutRequested
                ? "ConnectTimeout"
                : serviceStopping
                    ? "ServiceStopping"
                    : "UnknownCancellation";
            return new FlowErrorDetails
            {
                Phase = phase,
                ExceptionType = nameof(OperationCanceledException),
                CancellationReason = reason,
                TimeoutMs = connectTimeoutRequested ? timeoutMs : null,
                Message = reason switch
                {
                    "ConnectTimeout" => $"Outbound connect timed out after {timeoutMs ?? DefaultConnectTimeoutMs} ms.",
                    "ServiceStopping" => "Proxy service lifetime token was cancelled.",
                    _ => ex.Message,
                },
            };
        }

        if (ex is SocketException se)
        {
            return new FlowErrorDetails
            {
                Phase = phase,
                ExceptionType = nameof(SocketException),
                SocketErrorCode = se.SocketErrorCode.ToString(),
                NativeErrorCode = se.NativeErrorCode,
                Message = se.Message,
            };
        }

        return new FlowErrorDetails
        {
            Phase = phase,
            ExceptionType = ex.GetType().Name,
            Message = ex.Message,
        };
    }

    public static string FormatErrorStatus(FlowErrorDetails details)
    {
        if (details.CancellationReason == "ConnectTimeout")
        {
            return
                $"{FlowLifecycle.Error} phase={details.Phase} error=ConnectTimeout timeoutMs={details.TimeoutMs} " +
                $"destination={details.Destination} vpnIf={details.VpnInterfaceIndex} localBind={details.LocalBindEndpoint}";
        }

        return
            $"{FlowLifecycle.Error} phase={details.Phase} exception={details.ExceptionType} " +
            (details.CancellationReason is null ? "" : $"cancellation={details.CancellationReason} ") +
            (details.SocketErrorCode is null ? "" : $"socketError={details.SocketErrorCode} ") +
            (details.NativeErrorCode is null ? "" : $"nativeError={details.NativeErrorCode} ") +
            $"message={details.Message}";
    }

    public static string FormatBypassStatus(string bypassReason) =>
        bypassReason switch
        {
            "proxy-endpoint" => FlowLifecycle.LoopRejected,
            "loopback-destination" => FlowLifecycle.LoopbackBypass,
            "proxy-pid" => FlowLifecycle.ProxySelfRejected,
            _ => FlowLifecycle.LoopbackBypass,
        };

    public static string? TryGetBypassReason(IPEndPoint original, bool socks, int proxyPort)
    {
        if (IsLoopbackAddress(original.Address))
        {
            if (original.Port == proxyPort)
            {
                return "proxy-endpoint";
            }

            if (!socks)
            {
                return "loopback-destination";
            }
        }

        return null;
    }

    public static RealAppFlowObservation? SelectLatestFlowForExe(IEnumerable<FlowEvent> flows, string exePath)
    {
        string full = Path.GetFullPath(exePath);
        FlowEvent? latest = flows
            .Where(f => string.Equals(f.ProcessPath, full, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f.UpdatedAt)
            .ThenByDescending(f => f.SequenceId)
            .FirstOrDefault();
        return latest is null ? null : RealAppRoutingObservation.FromFlow(latest);
    }
}