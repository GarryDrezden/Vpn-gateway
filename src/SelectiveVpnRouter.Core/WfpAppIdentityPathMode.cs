namespace SelectiveVpnRouter.Core;

/// <summary>
/// How executable paths are turned into WFP ALE_APP_ID filter identities.
/// </summary>
public enum WfpAppIdentityPathMode
{
    /// <summary>Long path APP_ID; optional extra filter on 8.3 short path when it differs (non-ASCII paths).</summary>
    Default = 0,

    /// <summary>Diagnostic: only FwpmGetAppIdFromFileName0(long path).</summary>
    LongPathOnly = 1,

    /// <summary>Diagnostic: use input path verbatim for APP_ID (no Path.GetFullPath).</summary>
    ShortPathOnly = 2,
}

public sealed record WfpAppFilterOptions
{
    public WfpAppIdentityPathMode IdentityPathMode { get; init; } = WfpAppIdentityPathMode.Default;

    /// <summary>
    /// When set, used as the WFP identity source path instead of the collected display path
    /// (diagnostic ShortPathOnly for temp app route).
    /// </summary>
    public string? WfpIdentitySourceOverride { get; init; }

    /// <summary>
    /// Display/full path of the VPN app that receives WfpIdentitySourceOverride
    /// when multiple executables are in policy (temp Probe + production apps).
    /// </summary>
    public string? WfpIdentityOverrideExePath { get; init; }
}