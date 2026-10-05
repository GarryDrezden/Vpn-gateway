namespace SelectiveVpnRouter.Core.RoutingTrace;

public static class RoutingTraceEventMerger
{
    public static bool TryMergeIntoExisting(
        RoutingTraceEvent existing,
        RoutingTraceEvent incoming,
        out RoutingTraceEvent merged)
    {
        merged = existing;
        bool sameKey = string.Equals(existing.FlowCorrelationKey, incoming.FlowCorrelationKey, StringComparison.Ordinal);
        bool sameFlowId = existing.FlowId != Guid.Empty && existing.FlowId == incoming.FlowId;
        bool sameProxySource = existing.SourceProxyFlowId is Guid a
            && incoming.SourceProxyFlowId is Guid b
            && a != Guid.Empty
            && a == b;
        if (!sameKey && !sameFlowId && !sameProxySource)
        {
            return false;
        }

        RoutingTraceObservedRoute observed = RoutingTraceObservedRouteClassifier.ObservedRouteRank(incoming.ObservedRoute)
            >= RoutingTraceObservedRouteClassifier.ObservedRouteRank(existing.ObservedRoute)
            ? incoming.ObservedRoute
            : existing.ObservedRoute;

        RoutingTraceOutcome outcome = RoutingTraceObservedRouteClassifier.OutcomeRank(incoming.Outcome)
            >= RoutingTraceObservedRouteClassifier.OutcomeRank(existing.Outcome)
            ? incoming.Outcome
            : existing.Outcome;

        merged = existing with
        {
            ObservedRoute = observed,
            Outcome = outcome,
            EvidenceFlags = existing.EvidenceFlags | incoming.EvidenceFlags,
            Timestamp = incoming.Timestamp > existing.Timestamp ? incoming.Timestamp : existing.Timestamp,
            ProcessPath = string.IsNullOrWhiteSpace(incoming.ProcessPath) ? existing.ProcessPath : incoming.ProcessPath,
            LocalAddress = string.IsNullOrWhiteSpace(existing.LocalAddress) ? incoming.LocalAddress : existing.LocalAddress,
            LocalPort = existing.LocalPort > 0 ? existing.LocalPort : incoming.LocalPort,
            ProcessStartUtcTicks = existing.ProcessStartUtcTicks != 0 ? existing.ProcessStartUtcTicks : incoming.ProcessStartUtcTicks,
            FirstSeenUtc = existing.FirstSeenUtc == default ? incoming.FirstSeenUtc : existing.FirstSeenUtc,
            LastSeenUtc = incoming.LastSeenUtc != default && incoming.LastSeenUtc >= existing.LastSeenUtc ? incoming.LastSeenUtc : existing.LastSeenUtc,
            PreExistingAtTraceStart = existing.PreExistingAtTraceStart || incoming.PreExistingAtTraceStart,
            SourceProxyFlowId = existing.SourceProxyFlowId ?? incoming.SourceProxyFlowId,
        };
        return true;
    }
}
