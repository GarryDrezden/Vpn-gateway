namespace SelectiveVpnRouter.Core;

public sealed record WfpFilterInstallResult
{
    /// <summary>Display / rule path (long Unicode path shown to the user).</summary>
    public string ExePath { get; init; } = "";

    /// <summary>Path passed to FwpmGetAppIdFromFileName0 for this filter.</summary>
    public string IdentityPathUsed { get; init; } = "";

    public bool IsShortPathFallback { get; init; }
    public bool FileExists { get; init; }
    public bool AppIdResolved { get; init; }
    public uint AppIdStatus { get; init; }
    public bool FilterInstalled { get; init; }
    public uint FilterAddStatus { get; init; }
    public ulong FilterId { get; init; }
    public bool IsCalloutFilter { get; init; }
    public WfpFilterRole Role { get; init; } = WfpFilterRole.RedirectCallout;
    public ulong FilterWeight { get; init; }
    public uint ActionType { get; init; }
    public string? FilterAddContext { get; init; }
    public string? Error { get; init; }
}

public sealed record WfpPolicyApplyResult
{
    public IReadOnlyList<WfpFilterInstallResult> Filters { get; init; } = [];
    public int RequestedVpnApps { get; init; }
    public int InstalledAppFilters { get; init; }
    public bool DriverPresent { get; init; }
    public bool SessionOpen { get; init; }
    public bool PolicyHealthy { get; init; }
    public string? LastError { get; init; }

    public static WfpPolicyApplyResult NoSession(IReadOnlyList<string> requestedPaths) =>
        new()
        {
            RequestedVpnApps = requestedPaths.Count,
            InstalledAppFilters = 0,
            PolicyHealthy = requestedPaths.Count == 0,
            DriverPresent = false,
            SessionOpen = false,
            LastError = requestedPaths.Count == 0 ? null : "WFP session is not open.",
        };

    public static WfpPolicyApplyResult FromException(IReadOnlyList<string> requestedPaths, string message) =>
        new()
        {
            RequestedVpnApps = requestedPaths.Count,
            InstalledAppFilters = 0,
            PolicyHealthy = false,
            LastError = message,
        };
}

public sealed record WfpPolicyDiagnostics
{
    public int RequestedVpnApps { get; init; }
    public int InstalledAppFilters { get; init; }
    public bool PolicyHealthy { get; init; }
    public bool DriverPresent { get; init; }
    public bool SessionOpen { get; init; }
    public IReadOnlyList<string> RequestedPaths { get; init; } = [];
    public IReadOnlyList<WfpFilterInstallResult> Filters { get; init; } = [];
    public string? LastError { get; init; }

    public static WfpPolicyDiagnostics FromApply(WfpPolicyApplyResult result, IReadOnlyList<string> requestedPaths) =>
        new()
        {
            RequestedVpnApps = result.RequestedVpnApps,
            InstalledAppFilters = result.InstalledAppFilters,
            PolicyHealthy = result.PolicyHealthy,
            DriverPresent = result.DriverPresent,
            SessionOpen = result.SessionOpen,
            RequestedPaths = requestedPaths,
            Filters = result.Filters,
            LastError = result.LastError,
        };

    public static WfpPolicyDiagnostics Empty { get; } = new();
}