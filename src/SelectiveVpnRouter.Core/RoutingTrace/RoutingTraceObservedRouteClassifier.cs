namespace SelectiveVpnRouter.Core.RoutingTrace;

public static class RoutingTraceObservedRouteClassifier
{
    public static bool IsConfirmedLeak(
        RoutingTraceExpectedRoute expected,
        RoutingTraceObservedRoute observed,
        RoutingTraceDestinationKind destinationKind)
    {
        if (expected != RoutingTraceExpectedRoute.Vpn)
        {
            return false;
        }

        if (observed != RoutingTraceObservedRoute.Direct)
        {
            return false;
        }

        return destinationKind is RoutingTraceDestinationKind.External
            or RoutingTraceDestinationKind.PrivateLan
            or RoutingTraceDestinationKind.Unknown;
    }

    public static bool IsProvenVpnTcp4(RoutingTraceEvidenceFlags flags)
    {
        return flags.HasFlag(RoutingTraceEvidenceFlags.WfpRedirectApplied)
            && flags.HasFlag(RoutingTraceEvidenceFlags.ProxyAccepted)
            && flags.HasFlag(RoutingTraceEvidenceFlags.VpnBound);
    }

    public static bool IsProvenDirect(RoutingTraceEvidenceFlags flags)
        => flags.HasFlag(RoutingTraceEvidenceFlags.DirectObserved);

    public static int ObservedRouteRank(RoutingTraceObservedRoute route) => route switch
    {
        RoutingTraceObservedRoute.Vpn => 5,
        RoutingTraceObservedRoute.Direct => 4,
        RoutingTraceObservedRoute.Local => 3,
        RoutingTraceObservedRoute.Uncovered => 2,
        RoutingTraceObservedRoute.Unknown => 1,
        _ => 0,
    };

    public static int OutcomeRank(RoutingTraceOutcome outcome) => outcome switch
    {
        RoutingTraceOutcome.Connected => 5,
        RoutingTraceOutcome.Listening => 4,
        RoutingTraceOutcome.Failed => 3,
        RoutingTraceOutcome.Timeout => 2,
        RoutingTraceOutcome.Reset => 2,
        RoutingTraceOutcome.AccessDenied => 2,
        RoutingTraceOutcome.Unknown => 1,
        _ => 0,
    };
}
