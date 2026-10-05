namespace SelectiveVpnRouter.Core.RoutingTrace;

public static class RoutingTraceEvidenceFormatter
{
    public static string ToSummary(RoutingTraceEvidenceFlags flags)
    {
        IEnumerable<string> tokens = ToTokens(flags);
        return tokens.Any() ? string.Join(',', tokens) : "-";
    }

    public static IEnumerable<string> ToTokens(RoutingTraceEvidenceFlags flags)
    {
        if (flags.HasFlag(RoutingTraceEvidenceFlags.PassiveSocketTable))
        {
            yield return "passive";
        }

        if (flags.HasFlag(RoutingTraceEvidenceFlags.WfpRedirectApplied))
        {
            yield return "wfp";
        }

        if (flags.HasFlag(RoutingTraceEvidenceFlags.ProxyAccepted))
        {
            yield return "proxy";
        }

        if (flags.HasFlag(RoutingTraceEvidenceFlags.VpnBound))
        {
            yield return "vpn-bound";
        }

        if (flags.HasFlag(RoutingTraceEvidenceFlags.ProxyConnected))
        {
            yield return "connected";
        }

        if (flags.HasFlag(RoutingTraceEvidenceFlags.DirectObserved))
        {
            yield return "direct";
        }

        if (flags.HasFlag(RoutingTraceEvidenceFlags.LoopbackPermitMatched))
        {
            yield return "loopback";
        }

        if (flags.HasFlag(RoutingTraceEvidenceFlags.ChildProcess))
        {
            yield return "child";
        }

        if (flags.HasFlag(RoutingTraceEvidenceFlags.PackagedHelper))
        {
            yield return "packaged-helper";
        }

        if (flags.HasFlag(RoutingTraceEvidenceFlags.QuicCandidate))
        {
            yield return "quic-candidate";
        }
    }
}
