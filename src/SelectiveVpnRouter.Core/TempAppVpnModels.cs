namespace SelectiveVpnRouter.Core;

public sealed record TempAppVpnRequest
{
    public required string ExePath { get; init; }

    /// <summary>Diagnostic WFP APP_ID path mode (Default = long + optional 8.3 fallback).</summary>
    public WfpAppIdentityPathMode IdentityPathMode { get; init; } = WfpAppIdentityPathMode.Default;
}

public sealed record RealAppFlowObservation
{
    public Guid FlowId { get; init; }
    public long SequenceId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public int Pid { get; init; }
    public string Destination { get; init; } = "";
    public int Port { get; init; }
    public bool WfpRedirect { get; init; }
    public bool ProxyAccepted { get; init; }
    public bool RedirectRecordsApplied { get; init; }
    public bool VpnOutboundCreated { get; init; }
    public bool VpnOutboundBound { get; init; }
    public bool VpnOutboundConnected { get; init; }
    public bool RoutingObserved { get; init; }
    public bool TcpConnectSuccess { get; init; }
    public bool EgressVerified { get; init; }
    public bool HasFlowError { get; init; }
    public FlowRoute Route { get; init; }
    public string? LocalInterface { get; init; }
    public string? OutboundLocalEndpoint { get; init; }
    public string? OutboundRemoteEndpoint { get; init; }
    public string Status { get; init; } = "";
    public string? BypassReason { get; init; }
    public FlowErrorDetails? ErrorDetails { get; init; }
}

public sealed record TempAppVpnStatus
{
    public bool Active { get; init; }
    public string? ExePath { get; init; }
    public bool FileExists { get; init; }
    public bool AppIdResolved { get; init; }
    public bool FilterInstalled { get; init; }
    public ulong FilterId { get; init; }
    public ulong ShortPathFilterId { get; init; }
    public string? WfpFiltersSummary { get; init; }
    public WfpAppIdentityPathMode IdentityPathMode { get; init; } = WfpAppIdentityPathMode.Default;
    public string? Error { get; init; }
    public string ObservationState { get; init; } = "NONE";
    public DateTimeOffset QueriedAt { get; init; } = DateTimeOffset.UtcNow;
    public RealAppFlowObservation? SelectedFlow { get; init; }
    public IReadOnlyList<RealAppFlowObservation> RecentFlows { get; init; } = [];
    public string Summary { get; init; } = "";
    public string? WfpLayerAudit { get; init; }
    public string? RouteDiagnostic { get; init; }
}

public static class RealAppRoutingObservation
{
    public const string None = "NONE";
    public const string Waiting = "WAITING";
    public const string Partial = "PARTIAL";
    public const string RoutingObserved = "ROUTING_OBSERVED";
    public const string EgressVerified = "EGRESS_VERIFIED";
    public const string FlowError = "FLOW_ERROR";
    public const string Warning = "WARNING";

    public static RealAppFlowObservation FromFlow(FlowEvent flow)
    {
        bool proxyAccepted = flow.ProxyAccepted
            || (flow.WfpRedirect && !FlowStatusHelper.IsBypass(flow.Status));
        var obs = new RealAppFlowObservation
        {
            FlowId = flow.FlowId,
            SequenceId = flow.SequenceId,
            CreatedAt = flow.CreatedAt,
            UpdatedAt = flow.UpdatedAt,
            Pid = flow.Pid,
            Destination = flow.Destination,
            Port = flow.Port,
            WfpRedirect = flow.WfpRedirect,
            ProxyAccepted = proxyAccepted,
            RedirectRecordsApplied = flow.RedirectRecordsApplied,
            VpnOutboundCreated = flow.VpnOutboundCreated,
            VpnOutboundBound = flow.VpnOutboundBound,
            VpnOutboundConnected = flow.VpnOutboundConnected,
            Route = flow.Route,
            LocalInterface = flow.LocalInterface,
            OutboundLocalEndpoint = flow.OutboundLocalEndpoint,
            OutboundRemoteEndpoint = flow.OutboundRemoteEndpoint,
            Status = flow.Status,
            BypassReason = flow.BypassReason,
            ErrorDetails = flow.ErrorDetails,
            HasFlowError = FlowStatusHelper.IsError(flow.Status) || FlowStatusHelper.IsCancelled(flow.Status),
        };

        return obs with
        {
            RoutingObserved = FlowStatusHelper.HasRoutingObserved(obs),
            TcpConnectSuccess = FlowStatusHelper.HasTcpConnectSuccess(obs),
            EgressVerified = FlowStatusHelper.HasTcpConnectSuccess(obs) && FlowStatusHelper.IsSuccessTerminal(flow.Status),
        };
    }

    public static string ComputeState(RealAppFlowObservation? selected)
    {
        if (selected is null)
        {
            return Waiting;
        }

        if (selected.EgressVerified)
        {
            return EgressVerified;
        }

        if (selected.RoutingObserved && selected.HasFlowError)
        {
            return Warning;
        }

        if (selected.HasFlowError)
        {
            return FlowError;
        }

        if (selected.RoutingObserved)
        {
            return RoutingObserved;
        }

        return selected.WfpRedirect ? Partial : Waiting;
    }

    public static string BuildSummary(TempAppVpnStatus status)
    {
        if (!status.Active)
        {
            return "Temporary real-app VPN route is not active.";
        }

        RealAppFlowObservation? latest = status.SelectedFlow;
        if (latest is null)
        {
            return $"Temporary rule active for {status.ExePath}. Waiting for TCP traffic from this process.";
        }

        string routingLine =
            "ROUTING OBSERVED: " +
            $"flow={latest.FlowId:N} updated={latest.UpdatedAt:O} process={status.ExePath} pid={latest.Pid} dest={latest.Destination}:{latest.Port} " +
            $"wfpRedirect={latest.WfpRedirect} proxyAccepted={latest.ProxyAccepted} redirectContextRecovered={latest.RedirectRecordsApplied} " +
            $"vpnOutboundCreated={latest.VpnOutboundCreated} vpnOutboundBound={latest.VpnOutboundBound} vpnOutboundConnected={latest.VpnOutboundConnected} " +
            $"route={latest.Route} local={latest.LocalInterface ?? "—"} status={latest.Status}";

        if (latest.EgressVerified)
        {
            return routingLine + " EGRESS VERIFIED.";
        }

        if (latest.HasFlowError)
        {
            string err = latest.ErrorDetails is null
                ? latest.Status
                : FlowStatusHelper.FormatErrorStatus(latest.ErrorDetails);
            return routingLine + Environment.NewLine +
                   "WARNING — VPN routing observed, but application flow ended with an error: " + err;
        }

        return routingLine;
    }
}