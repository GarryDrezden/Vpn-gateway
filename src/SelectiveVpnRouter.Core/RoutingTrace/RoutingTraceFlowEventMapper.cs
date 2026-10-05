namespace SelectiveVpnRouter.Core.RoutingTrace;

public static class RoutingTraceFlowEventMapper
{
    public static RoutingTraceEvent FromProxyFlow(
        FlowEvent flow,
        RoutingTraceTarget target,
        RoutingTraceAttribution attribution,
        long sequenceId,
        long processStartUtcTicks)
    {
        var family = RoutingTraceAddressFamily.IPv4;
        var destinationKind = RoutingTraceDestinationClassifier.Classify(flow.Destination, family);
        var expected = RoutingTraceExpectedRouteResolver.Resolve(target, attribution, destinationKind);

        var flags = RoutingTraceEvidenceFlags.None;
        if (flow.WfpRedirect)
        {
            flags |= RoutingTraceEvidenceFlags.WfpRedirectApplied | RoutingTraceEvidenceFlags.WfpAppFilterMatched;
        }

        if (flow.RedirectRecordsApplied)
        {
            flags |= RoutingTraceEvidenceFlags.WfpRedirectApplied;
        }

        if (flow.ProxyAccepted)
        {
            flags |= RoutingTraceEvidenceFlags.ProxyAccepted;
        }

        if (flow.VpnOutboundConnected)
        {
            flags |= RoutingTraceEvidenceFlags.ProxyConnected;
        }

        if (flow.VpnOutboundBound)
        {
            flags |= RoutingTraceEvidenceFlags.VpnBound;
        }

        if (flow.Route == FlowRoute.Direct && !flow.WfpRedirect && !flow.ProxyAccepted)
        {
            flags |= RoutingTraceEvidenceFlags.DirectObserved;
        }

        if (destinationKind == RoutingTraceDestinationKind.Loopback)
        {
            flags |= RoutingTraceEvidenceFlags.LoopbackPermitMatched;
        }

        if (attribution.IsChildProcess)
        {
            flags |= RoutingTraceEvidenceFlags.ChildProcess;
        }

        if (attribution.IsPackagedHelper)
        {
            flags |= RoutingTraceEvidenceFlags.PackagedHelper;
        }

        RoutingTraceObservedRoute observed = ClassifyObserved(expected, destinationKind, flags, flow);
        RoutingTraceOutcome outcome = ClassifyOutcome(flow, observed);
        ParseEndpoint(flow.OutboundLocalEndpoint, out string? localAddress, out int localPort);

        return new RoutingTraceEvent
        {
            SequenceId = sequenceId,
            Timestamp = flow.UpdatedAt,
            FlowCorrelationKey = RoutingTraceFlowCorrelation.FromProxyFlow(flow, processStartUtcTicks).PrimaryKey,
            SourceProxyFlowId = flow.FlowId,
            ProcessId = flow.Pid,
            ProcessPath = flow.ProcessPath,
            Protocol = RoutingTraceProtocol.Tcp,
            AddressFamily = family,
            LocalAddress = localAddress ?? string.Empty,
            LocalPort = localPort,
            RemoteAddress = flow.Destination,
            RemotePort = flow.Port,
            DestinationKind = destinationKind,
            ExpectedRoute = expected,
            ObservedRoute = observed,
            Outcome = outcome,
            EvidenceFlags = flags,
            LogicalRuleId = attribution.LogicalRuleId ?? target.RuleId,
            ProcessStartUtcTicks = processStartUtcTicks,
            FirstSeenUtc = flow.UpdatedAt,
            LastSeenUtc = flow.UpdatedAt,
            LogicalApplicationName = attribution.LogicalApplicationName ?? target.DisplayName,
        };
    }

    private static RoutingTraceObservedRoute ClassifyObserved(
        RoutingTraceExpectedRoute expected,
        RoutingTraceDestinationKind destinationKind,
        RoutingTraceEvidenceFlags flags,
        FlowEvent flow)
    {
        if (destinationKind == RoutingTraceDestinationKind.Loopback)
        {
            return RoutingTraceObservedRoute.Local;
        }

        if (RoutingTraceObservedRouteClassifier.IsProvenDirect(flags))
        {
            return RoutingTraceObservedRoute.Direct;
        }

        if (RoutingTraceObservedRouteClassifier.IsProvenVpnTcp4(flags))
        {
            return RoutingTraceObservedRoute.Vpn;
        }

        if (flow.Route == FlowRoute.Vpn && flags.HasFlag(RoutingTraceEvidenceFlags.ProxyAccepted))
        {
            return RoutingTraceObservedRoute.Vpn;
        }

        if (expected == RoutingTraceExpectedRoute.Vpn)
        {
            return RoutingTraceObservedRoute.Unknown;
        }

        return RoutingTraceObservedRoute.Unknown;
    }

    private static RoutingTraceOutcome ClassifyOutcome(FlowEvent flow, RoutingTraceObservedRoute observed)
    {
        if (observed == RoutingTraceObservedRoute.Local)
        {
            return RoutingTraceOutcome.Listening;
        }

        if (flow.Status is FlowLifecycle.Connected or FlowLifecycle.Relaying or FlowLifecycle.Closed)
        {
            return RoutingTraceOutcome.Connected;
        }

        if (flow.Status == FlowLifecycle.Error)
        {
            return RoutingTraceOutcome.Failed;
        }

        return RoutingTraceOutcome.Unknown;
    }

    private static void ParseEndpoint(string? endpoint, out string? address, out int port)
    {
        address = null;
        port = 0;
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return;
        }

        int idx = endpoint.LastIndexOf(':');
        if (idx <= 0)
        {
            return;
        }

        address = endpoint[..idx].Trim();
        if (int.TryParse(endpoint[(idx + 1)..], out int parsed))
        {
            port = parsed;
        }
    }
}
