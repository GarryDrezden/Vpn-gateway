namespace SelectiveVpnRouter.Core;

public static class WfpPolicyHealth
{
    public static bool IsHealthy(WfpPolicyApplyResult result) =>
        result.RequestedVpnApps == 0 || result.InstalledAppFilters >= result.RequestedVpnApps;

    public static bool IsExeFilterReady(WfpFilterInstallResult? filter) =>
        filter is
        {
            FileExists: true,
            AppIdResolved: true,
            FilterInstalled: true,
            FilterId: > 0,
            IsCalloutFilter: true,
            Role: WfpFilterRole.RedirectCallout,
        };

    public static bool IsLoopbackPermitReady(WfpFilterInstallResult? filter) =>
        filter is
        {
            FileExists: true,
            AppIdResolved: true,
            FilterInstalled: true,
            FilterId: > 0,
            Role: WfpFilterRole.LoopbackPermitV4,
        };

    public static bool IsVpnAppWfpReady(WfpPolicyDiagnostics policy, string exePath) =>
        IsExeFilterReady(FindCalloutFilter(policy, exePath))
        && IsLoopbackPermitReady(FindLoopbackPermitFilter(policy, exePath));

    public static WfpFilterInstallResult? FindLoopbackPermitFilter(WfpPolicyDiagnostics policy, string exePath)
    {
        string fullPath = Path.GetFullPath(exePath);
        return policy.Filters.FirstOrDefault(f =>
            f.Role == WfpFilterRole.LoopbackPermitV4
            && string.Equals(f.ExePath, fullPath, StringComparison.OrdinalIgnoreCase)
            && !f.IsShortPathFallback)
            ?? policy.Filters.FirstOrDefault(f =>
                f.Role == WfpFilterRole.LoopbackPermitV4
                && string.Equals(f.ExePath, fullPath, StringComparison.OrdinalIgnoreCase));
    }

    public static WfpFilterInstallResult? FindCalloutFilter(WfpPolicyDiagnostics policy, string exePath)
    {
        string fullPath = Path.GetFullPath(exePath);
        return policy.Filters.FirstOrDefault(f =>
            f.IsCalloutFilter
            && f.Role == WfpFilterRole.RedirectCallout
            && string.Equals(f.ExePath, fullPath, StringComparison.OrdinalIgnoreCase)
            && !f.IsShortPathFallback)
            ?? policy.Filters.FirstOrDefault(f =>
                f.IsCalloutFilter
                && f.Role == WfpFilterRole.RedirectCallout
                && string.Equals(f.ExePath, fullPath, StringComparison.OrdinalIgnoreCase));
    }

    public static IReadOnlyList<WfpFilterInstallResult> FindCalloutFilters(WfpPolicyDiagnostics policy, string exePath)
    {
        string fullPath = Path.GetFullPath(exePath);
        return policy.Filters
            .Where(f => f.IsCalloutFilter && string.Equals(f.ExePath, fullPath, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    public static string DescribeStatus(uint status) =>
        status switch
        {
            0 => "SUCCESS",
            0x80320024 => "FWP_E_INVALID_ACTION_TYPE",
            0x80320025 => "FWP_E_INVALID_WEIGHT / nedopustimyj ves WFP-filtra",
            0x0000053A => "ERROR_INVALID_SECURITY_DESCR (1338) / uslovie moglo byt interpretirovano kak security descriptor",
            _ => "0x" + status.ToString("X8"),
        };

    public static string FormatFilterLine(WfpFilterInstallResult filter) =>
        $"role={filter.Role} exe={filter.ExePath} identity={filter.IdentityPathUsed} shortFallback={filter.IsShortPathFallback} " +
        $"weight={filter.FilterWeight} actionType=0x{filter.ActionType:X8} actionKind={WfpActionDiagnostics.DescribeActionKind(filter.ActionType)} " +
        $"fileExists={filter.FileExists} appIdResolved={filter.AppIdResolved} " +
        $"appIdStatus=0x{filter.AppIdStatus:X8} ({DescribeStatus(filter.AppIdStatus)}) " +
        $"filterInstalled={filter.FilterInstalled} " +
        $"filterAddStatus=0x{filter.FilterAddStatus:X8} ({DescribeStatus(filter.FilterAddStatus)}) " +
        $"filterId={filter.FilterId}" +
        (filter.FilterAddContext is null ? "" : " ctx=[" + filter.FilterAddContext + "]")
        + (filter.Error is null ? "" : " error=" + filter.Error);

    public static string SummarizeCalloutFilters(IEnumerable<WfpFilterInstallResult> filters)
    {
        var parts = filters
            .Where(f => f.IsCalloutFilter)
            .Select(f => (f.IsShortPathFallback ? "short" : "long") + ":id=" + f.FilterId + ",identity=" + f.IdentityPathUsed)
            .ToArray();
        return parts.Length == 0 ? "(none)" : string.Join("; ", parts);
    }
}