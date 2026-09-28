namespace SelectiveVpnRouter.Core;

public sealed record TempAppVpnRequest
{
    public required string ExePath { get; init; }
}

public sealed record RealAppFlowObservation
{
    public int Pid { get; init; }
    public string Destination { get; init; } = "";
    public int Port { get; init; }
    public bool WfpRedirect { get; init; }
    public bool ProxyAccepted { get; init; }
    public bool RedirectRecordsApplied { get; init; }
    public bool VpnBoundOutboundCreated { get; init; }
    public FlowRoute Route { get; init; }
    public string? LocalInterface { get; init; }
    public string Status { get; init; } = "";
}

public sealed record TempAppVpnStatus
{
    public bool Active { get; init; }
    public string? ExePath { get; init; }
    public bool FileExists { get; init; }
    public bool AppIdResolved { get; init; }
    public bool FilterInstalled { get; init; }
    public ulong FilterId { get; init; }
    public string? Error { get; init; }
    public string ObservationState { get; init; } = "NONE";
    public IReadOnlyList<RealAppFlowObservation> Flows { get; init; } = [];
    public string Summary { get; init; } = "";
}

public static class RealAppRoutingObservation
{
    public const string None = "NONE";
    public const string Waiting = "WAITING";
    public const string Partial = "PARTIAL";
    public const string Pass = "PASS";

    public static RealAppFlowObservation FromFlow(FlowEvent flow) =>
        new()
        {
            Pid = flow.Pid,
            Destination = flow.Destination,
            Port = flow.Port,
            WfpRedirect = flow.WfpRedirect,
            ProxyAccepted = flow.WfpRedirect && flow.Status is not "loop-rejected" and not "proxy-self-rejected",
            RedirectRecordsApplied = flow.RedirectRecordsApplied,
            VpnBoundOutboundCreated = flow.RedirectRecordsApplied && flow.Route == FlowRoute.Vpn,
            Route = flow.Route,
            LocalInterface = flow.LocalInterface,
            Status = flow.Status,
        };

    public static string ComputeState(IReadOnlyList<FlowEvent> flows, string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath))
        {
            return None;
        }

        string full = Path.GetFullPath(exePath);
        RealAppFlowObservation[] appFlows = flows
            .Where(f => string.Equals(f.ProcessPath, full, StringComparison.OrdinalIgnoreCase))
            .Select(FromFlow)
            .ToArray();
        if (appFlows.Length == 0)
        {
            return Waiting;
        }

        if (appFlows.Any(f => f.WfpRedirect && f.ProxyAccepted && f.RedirectRecordsApplied && f.VpnBoundOutboundCreated))
        {
            return Pass;
        }

        return appFlows.Any(f => f.WfpRedirect) ? Partial : Waiting;
    }

    public static string BuildSummary(TempAppVpnStatus status)
    {
        if (!status.Active)
        {
            return "Temporary real-app VPN route is not active.";
        }

        RealAppFlowObservation? latest = status.Flows.FirstOrDefault();
        if (latest is null)
        {
            return $"Temporary rule active for {status.ExePath}. Waiting for TCP traffic from this process.";
        }

        return
            "REAL APP ROUTING OBSERVED: " +
            $"process={status.ExePath} wfpRedirect={latest.WfpRedirect} proxyAccepted={latest.ProxyAccepted} " +
            $"redirectContextRecovered={latest.RedirectRecordsApplied} route={latest.Route} " +
            $"local={latest.LocalInterface ?? "—"} status={latest.Status}";
    }
}