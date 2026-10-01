namespace SelectiveVpnRouter.Core;

public static class WfpProbeRedirectDiagnosticVerdict
{
    /// <summary>
    /// Verdict for installed WFP callout filters during probe redirect diagnostics.
    /// Production uses a single long-path filter with normalized ALE_APP_ID (no 8.3 dual-filter requirement).
    /// </summary>
    public static bool InstalledFiltersMeetExpectation(
        WfpAppIdentityPathMode mode,
        string expectedIdentityInput,
        IReadOnlyList<WfpFilterInstallResult> filters)
    {
        if (mode == WfpAppIdentityPathMode.ShortPathOnly)
        {
            return filters.Any(f =>
                f is { FilterInstalled: true, AppIdResolved: true, IsCalloutFilter: true }
                && string.Equals(f.IdentityPathUsed, expectedIdentityInput, StringComparison.OrdinalIgnoreCase));
        }

        return filters.Any(f =>
            f is { FilterInstalled: true, AppIdResolved: true, IsCalloutFilter: true }
            && !f.IsShortPathFallback);
    }

    public static bool EndToEndRoutingSucceeded(
        bool filtersOk,
        bool httpOk,
        bool calloutMatched,
        bool applyModified,
        bool proxyAccepted,
        bool redirectContextRecovered,
        bool proxyFlowObserved,
        string? localEndpoint,
        string? publicIp,
        string? launchError,
        string? infrastructureFailure,
        string? baselineDirectPublicIp)
    {
        if (launchError is not null || infrastructureFailure is not null)
        {
            return false;
        }

        if (!filtersOk
            || !httpOk
            || !calloutMatched
            || !applyModified
            || !proxyAccepted
            || !redirectContextRecovered
            || !proxyFlowObserved)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(localEndpoint) || string.IsNullOrWhiteSpace(publicIp))
        {
            return false;
        }

        if (baselineDirectPublicIp is not null
            && string.Equals(publicIp.Trim(), baselineDirectPublicIp.Trim(), StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }
}