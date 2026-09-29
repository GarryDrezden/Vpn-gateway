namespace SelectiveVpnRouter.Core;

public sealed record TempAppVpnFlowsRequest
{
    public required string ExePath { get; init; }
    public int MaxCount { get; init; } = 30;
}

public sealed record TempAppVpnFlowsResponse
{
    public DateTimeOffset QueriedAt { get; init; } = DateTimeOffset.UtcNow;
    public IReadOnlyList<TempAppVpnFlowDto> Flows { get; init; } = [];
}

public sealed record TempAppVpnFlowDto
{
    public Guid FlowId { get; init; }
    public long SequenceId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public int ProcessId { get; init; }
    public string ProcessPath { get; init; } = "";
    public string Destination { get; init; } = "";
    public int Port { get; init; }
    public string Status { get; init; } = "";

    public bool WfpRedirect { get; init; }
    public bool ProxyAccepted { get; init; }
    public bool RedirectContextRecovered { get; init; }

    public bool VpnOutboundCreated { get; init; }
    public bool VpnOutboundBound { get; init; }
    public bool VpnOutboundConnected { get; init; }

    public bool TcpConnectSuccess { get; init; }
    public FlowRoute Route { get; init; }

    public string? ErrorPhase { get; init; }
    public string? ErrorReason { get; init; }
    public string? SocketError { get; init; }
    public int? NativeError { get; init; }
    public string? ErrorMessage { get; init; }
}

public static class TempAppVpnFlowMapper
{
    public static TempAppVpnFlowDto FromFlow(FlowEvent flow)
    {
        bool proxyAccepted = flow.ProxyAccepted
            || (flow.WfpRedirect && !FlowStatusHelper.IsBypass(flow.Status));
        bool hasFlowError = FlowStatusHelper.IsError(flow.Status) || FlowStatusHelper.IsCancelled(flow.Status);
        bool routingObserved = flow.WfpRedirect
            && proxyAccepted
            && flow.RedirectRecordsApplied
            && flow.VpnOutboundBound;
        bool tcpConnectSuccess = routingObserved
            && flow.VpnOutboundConnected
            && !hasFlowError
            && !FlowStatusHelper.IsCancelled(flow.Status);

        return new TempAppVpnFlowDto
        {
            FlowId = flow.FlowId,
            SequenceId = flow.SequenceId,
            CreatedAt = flow.CreatedAt,
            UpdatedAt = flow.UpdatedAt,
            ProcessId = flow.Pid,
            ProcessPath = flow.ProcessPath,
            Destination = flow.Destination,
            Port = flow.Port,
            Status = flow.Status,
            WfpRedirect = flow.WfpRedirect,
            ProxyAccepted = proxyAccepted,
            RedirectContextRecovered = flow.RedirectRecordsApplied,
            VpnOutboundCreated = flow.VpnOutboundCreated,
            VpnOutboundBound = flow.VpnOutboundBound,
            VpnOutboundConnected = flow.VpnOutboundConnected,
            TcpConnectSuccess = tcpConnectSuccess,
            Route = flow.Route,
            ErrorPhase = flow.ErrorDetails?.Phase,
            ErrorReason = flow.ErrorDetails?.CancellationReason,
            SocketError = flow.ErrorDetails?.SocketErrorCode,
            NativeError = flow.ErrorDetails?.NativeErrorCode,
            ErrorMessage = flow.ErrorDetails?.Message,
        };
    }
}

public static class TempAppVpnFlowQuery
{
    public const int DefaultMaxCount = 30;
    public const int MaxAllowedCount = 50;

    public static IReadOnlyList<TempAppVpnFlowDto> Query(
        IEnumerable<FlowEvent> flows,
        string exePath,
        int maxCount = DefaultMaxCount)
    {
        if (string.IsNullOrWhiteSpace(exePath))
        {
            return [];
        }

        int take = Math.Clamp(maxCount, 1, MaxAllowedCount);
        string full = Path.GetFullPath(exePath);
        return flows
            .Where(f => string.Equals(f.ProcessPath, full, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f.UpdatedAt)
            .ThenByDescending(f => f.SequenceId)
            .Take(take)
            .Select(TempAppVpnFlowMapper.FromFlow)
            .ToArray();
    }
}

public static class TargetEndpointParser
{
    public static bool TryParse(string target, out string host, out int port)
    {
        host = "";
        port = 443;
        if (string.IsNullOrWhiteSpace(target))
        {
            return false;
        }

        string trimmed = target.Trim();
        int colon = trimmed.LastIndexOf(':');
        if (colon > 0 && colon < trimmed.Length - 1 && int.TryParse(trimmed[(colon + 1)..], out int parsedPort))
        {
            host = trimmed[..colon].Trim();
            port = parsedPort;
            return host.Length > 0;
        }

        host = trimmed;
        return host.Length > 0;
    }
}

public static class TargetFlowMatcher
{
    public static bool MatchesTarget(TempAppVpnFlowDto flow, IEnumerable<string> targetAddresses, int targetPort)
    {
        if (flow.Port != targetPort)
        {
            return false;
        }

        foreach (string address in targetAddresses)
        {
            if (string.Equals(flow.Destination, address, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static TempAppVpnFlowDto? SelectLatestTargetFlow(
        IEnumerable<TempAppVpnFlowDto> flows,
        IEnumerable<string> targetAddresses,
        int targetPort)
    {
        return flows
            .Where(f => MatchesTarget(f, targetAddresses, targetPort))
            .OrderByDescending(f => f.UpdatedAt)
            .ThenByDescending(f => f.SequenceId)
            .FirstOrDefault();
    }
}

public static class TargetAcceptanceState
{
    public const string NotSeen = "TARGET NOT SEEN";
    public const string Connecting = "TARGET CONNECTING";
    public const string RoutingObserved = "TARGET ROUTING OBSERVED";
    public const string Connected = "TARGET CONNECTED";
    public const string ClosedPass = "TARGET CLOSED / PASS";
    public const string Error = "TARGET ERROR";
}

public static class TargetFlowAcceptance
{
    public static bool HasTargetTcpFlowPass(TempAppVpnFlowDto flow) =>
        flow.WfpRedirect
        && flow.ProxyAccepted
        && flow.RedirectContextRecovered
        && flow.VpnOutboundBound
        && flow.VpnOutboundConnected
        && flow.TcpConnectSuccess
        && flow.Status is FlowLifecycle.Connected or FlowLifecycle.Relaying or FlowLifecycle.Closed;

    public static bool HasRoutingObserved(TempAppVpnFlowDto flow) =>
        flow.WfpRedirect
        && flow.ProxyAccepted
        && flow.RedirectContextRecovered
        && flow.VpnOutboundBound;

    public static bool HasFlowError(TempAppVpnFlowDto flow) =>
        FlowStatusHelper.IsError(flow.Status) || FlowStatusHelper.IsCancelled(flow.Status);

    public static string ComputeState(TempAppVpnFlowDto? targetFlow)
    {
        if (targetFlow is null)
        {
            return TargetAcceptanceState.NotSeen;
        }

        if (HasFlowError(targetFlow))
        {
            return TargetAcceptanceState.Error;
        }

        if (HasTargetTcpFlowPass(targetFlow))
        {
            return TargetAcceptanceState.ClosedPass;
        }

        if (targetFlow.VpnOutboundConnected && HasRoutingObserved(targetFlow))
        {
            return TargetAcceptanceState.Connected;
        }

        if (HasRoutingObserved(targetFlow))
        {
            return TargetAcceptanceState.RoutingObserved;
        }

        if (targetFlow.Status is FlowLifecycle.Connecting
            or FlowLifecycle.OutboundCreated
            or FlowLifecycle.OutboundBound
            or FlowLifecycle.Accepted
            or FlowLifecycle.RedirectContextRecovered)
        {
            return TargetAcceptanceState.Connecting;
        }

        return TargetAcceptanceState.NotSeen;
    }

    public static string FormatTargetStatusLine(string state, TempAppVpnFlowDto? targetFlow)
    {
        if (state == TargetAcceptanceState.ClosedPass)
        {
            return state + " - TARGET TCP FLOW PASS";
        }

        if (targetFlow is null)
        {
            return state;
        }

        string dest = targetFlow.Destination + ":" + targetFlow.Port;
        if (state == TargetAcceptanceState.Error)
        {
            string err = targetFlow.ErrorReason ?? targetFlow.ErrorMessage ?? targetFlow.Status;
            return state + " - " + dest + " (" + err + ")";
        }

        return state + " - " + dest + " status=" + targetFlow.Status;
    }
}