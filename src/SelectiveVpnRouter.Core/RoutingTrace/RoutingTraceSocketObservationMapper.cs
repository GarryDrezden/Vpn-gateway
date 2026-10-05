namespace SelectiveVpnRouter.Core.RoutingTrace;

public static class RoutingTraceSocketObservationMapper
{
    public static RoutingTraceEvent FromPassiveObservation(
        PassiveSocketObservation obs,
        RoutingTraceTarget target,
        RoutingTraceAttribution attribution,
        long sequenceId,
        long processStartUtcTicks)
    {
        var destinationKind = RoutingTraceDestinationClassifier.Classify(obs.RemoteAddress, obs.AddressFamily);
        var expected = RoutingTraceExpectedRouteResolver.Resolve(target, attribution, destinationKind);

        var flags = RoutingTraceEvidenceFlags.PassiveSocketTable;
        if (obs.AddressFamily == RoutingTraceAddressFamily.IPv6)
        {
            flags |= RoutingTraceEvidenceFlags.Ipv6;
        }

        if (obs.Protocol == RoutingTraceProtocol.Udp && (obs.LocalPort == 443 || obs.RemotePort == 443))
        {
            flags |= RoutingTraceEvidenceFlags.QuicCandidate;
        }

        if (attribution.IsChildProcess)
        {
            flags |= RoutingTraceEvidenceFlags.ChildProcess;
        }

        if (attribution.IsPackagedHelper)
        {
            flags |= RoutingTraceEvidenceFlags.PackagedHelper;
        }

        RoutingTraceObservedRoute observed = ClassifyPassiveObserved(expected, obs, destinationKind, ref flags);

        return new RoutingTraceEvent
        {
            SequenceId = sequenceId,
            Timestamp = obs.ObservedAt,
            FlowCorrelationKey = RoutingTraceFlowCorrelation.FromPassiveObservation(obs, processStartUtcTicks).PrimaryKey,
            ProcessId = obs.Pid,
            ProcessPath = attribution.ProcessPath ?? "",
            Protocol = obs.Protocol,
            AddressFamily = obs.AddressFamily,
            LocalAddress = obs.LocalAddress,
            LocalPort = obs.LocalPort,
            RemoteAddress = obs.RemoteAddress,
            RemotePort = obs.RemotePort,
            DestinationKind = destinationKind,
            ExpectedRoute = expected,
            ObservedRoute = observed,
            Outcome = RoutingTraceOutcome.Unknown,
            EvidenceFlags = flags,
            LogicalRuleId = attribution.LogicalRuleId ?? target.RuleId,
            ProcessStartUtcTicks = processStartUtcTicks,
            FirstSeenUtc = obs.ObservedAt,
            LastSeenUtc = obs.ObservedAt,
            LogicalApplicationName = attribution.LogicalApplicationName ?? target.DisplayName,
        };
    }

    private static RoutingTraceObservedRoute ClassifyPassiveObserved(
        RoutingTraceExpectedRoute expected,
        PassiveSocketObservation obs,
        RoutingTraceDestinationKind destinationKind,
        ref RoutingTraceEvidenceFlags flags)
    {
        if (destinationKind == RoutingTraceDestinationKind.Loopback)
        {
            return RoutingTraceObservedRoute.Local;
        }

        if (expected == RoutingTraceExpectedRoute.Vpn)
        {
            if (obs.Protocol == RoutingTraceProtocol.Udp || obs.AddressFamily == RoutingTraceAddressFamily.IPv6)
            {
                return RoutingTraceObservedRoute.Uncovered;
            }

            return RoutingTraceObservedRoute.Unknown;
        }

        return RoutingTraceObservedRoute.Unknown;
    }
}
