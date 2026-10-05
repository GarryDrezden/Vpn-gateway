namespace SelectiveVpnRouter.Core.RoutingTrace;

public static class RoutingTraceSemanticChange
{
    public static bool HasMeaningfulChange(RoutingTraceEvent before, RoutingTraceEvent after)
    {
        if (before.ProcessId != after.ProcessId)
        {
            return true;
        }

        if (before.ProcessStartUtcTicks != after.ProcessStartUtcTicks)
        {
            return true;
        }

        if (before.ParentProcessId != after.ParentProcessId)
        {
            return true;
        }

        if (!string.Equals(NormalizePath(before.ProcessPath), NormalizePath(after.ProcessPath), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (before.Protocol != after.Protocol || before.AddressFamily != after.AddressFamily)
        {
            return true;
        }

        if (!string.Equals(NormalizeEndpoint(before.LocalAddress), NormalizeEndpoint(after.LocalAddress), StringComparison.OrdinalIgnoreCase)
            || before.LocalPort != after.LocalPort)
        {
            return true;
        }

        if (!string.Equals(NormalizeEndpoint(before.RemoteAddress), NormalizeEndpoint(after.RemoteAddress), StringComparison.OrdinalIgnoreCase)
            || before.RemotePort != after.RemotePort)
        {
            return true;
        }

        if (before.DestinationKind != after.DestinationKind)
        {
            return true;
        }

        if (before.ExpectedRoute != after.ExpectedRoute
            || before.ObservedRoute != after.ObservedRoute
            || before.CoverageReason != after.CoverageReason
            || before.Outcome != after.Outcome)
        {
            return true;
        }

        if (before.EvidenceFlags != after.EvidenceFlags)
        {
            return true;
        }

        if (before.PreExistingAtTraceStart != after.PreExistingAtTraceStart)
        {
            return true;
        }

        if (before.SourceProxyFlowId != after.SourceProxyFlowId)
        {
            return true;
        }

        if (before.LogicalRuleId != after.LogicalRuleId)
        {
            return true;
        }

        if (!string.Equals(NormalizePath(before.LogicalApplicationName), NormalizePath(after.LogicalApplicationName), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.Equals(NormalizePath(before.Hostname), NormalizePath(after.Hostname), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (before.HostnameSource != after.HostnameSource)
        {
            return true;
        }

        return false;
    }

    private static string NormalizePath(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static string NormalizeEndpoint(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
}
