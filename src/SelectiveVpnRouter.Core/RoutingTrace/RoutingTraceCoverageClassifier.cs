namespace SelectiveVpnRouter.Core.RoutingTrace;

public static class RoutingTraceCoverageClassifier
{
    public static RoutingTraceEvent Apply(RoutingTraceEvent e)
    {
        RoutingTraceObservedRoute observed = e.ObservedRoute;
        RoutingTraceCoverageReason reason = RoutingTraceCoverageReason.None;

        if (e.DestinationKind == RoutingTraceDestinationKind.Loopback || e.ExpectedRoute == RoutingTraceExpectedRoute.Local)
        {
            return e with
            {
                ObservedRoute = RoutingTraceObservedRoute.Local,
                CoverageReason = RoutingTraceCoverageReason.LocalBypass,
            };
        }

        if (RoutingTraceObservedRouteClassifier.IsProvenDirect(e.EvidenceFlags))
        {
            return e with
            {
                ObservedRoute = RoutingTraceObservedRoute.Direct,
                CoverageReason = RoutingTraceCoverageReason.ProvenDirect,
            };
        }

        if (RoutingTraceObservedRouteClassifier.IsProvenVpnTcp4(e.EvidenceFlags)
            || (e.EvidenceFlags.HasFlag(RoutingTraceEvidenceFlags.ProxyAccepted)
                && e.EvidenceFlags.HasFlag(RoutingTraceEvidenceFlags.VpnBound)))
        {
            return e with
            {
                ObservedRoute = RoutingTraceObservedRoute.Vpn,
                CoverageReason = RoutingTraceCoverageReason.None,
            };
        }

        if (e.ExpectedRoute == RoutingTraceExpectedRoute.Vpn)
        {
            if (e.Protocol == RoutingTraceProtocol.Udp)
            {
                return e with
                {
                    ObservedRoute = RoutingTraceObservedRoute.Uncovered,
                    CoverageReason = RoutingTraceCoverageReason.UnsupportedProtocol,
                };
            }

            if (e.AddressFamily == RoutingTraceAddressFamily.IPv6)
            {
                return e with
                {
                    ObservedRoute = RoutingTraceObservedRoute.Uncovered,
                    CoverageReason = RoutingTraceCoverageReason.UnsupportedAddressFamily,
                };
            }

            if (e.PreExistingAtTraceStart)
            {
                return e with
                {
                    ObservedRoute = RoutingTraceObservedRoute.Unknown,
                    CoverageReason = RoutingTraceCoverageReason.PreExistingAtTraceStart,
                };
            }

            bool passive = e.EvidenceFlags.HasFlag(RoutingTraceEvidenceFlags.PassiveSocketTable);
            bool proxy = e.EvidenceFlags.HasFlag(RoutingTraceEvidenceFlags.ProxyAccepted)
                || e.EvidenceFlags.HasFlag(RoutingTraceEvidenceFlags.WfpRedirectApplied);

            if (!proxy)
            {
                return e with
                {
                    ObservedRoute = RoutingTraceObservedRoute.Unknown,
                    CoverageReason = passive
                        ? RoutingTraceCoverageReason.MissingProxyEvidence
                        : RoutingTraceCoverageReason.InsufficientEvidence,
                };
            }
        }

        if (observed == RoutingTraceObservedRoute.Uncovered)
        {
            reason = RoutingTraceCoverageReason.UnsupportedProtocol;
        }
        else if (observed == RoutingTraceObservedRoute.Unknown)
        {
            reason = RoutingTraceCoverageReason.InsufficientEvidence;
        }

        return e with { CoverageReason = reason };
    }
}
