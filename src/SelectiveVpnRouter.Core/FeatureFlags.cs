namespace SelectiveVpnRouter.Core;

/// <summary>
/// Central product feature flags (shared by App and Service).
/// </summary>
public static class FeatureFlags
{
    public const string WorkVpnEnvironmentVariable = "VPN_ROUTE_ENABLE_WORK_VPN";

    /// <summary>
    /// Experimental Work VPN UI/lifecycle. Default off; developers may set <see cref="WorkVpnEnvironmentVariable"/> to 1/true.
    /// </summary>
    public static bool WorkVpn => ParseEnvironmentToggle(Environment.GetEnvironmentVariable(WorkVpnEnvironmentVariable));

    internal static bool ParseEnvironmentToggle(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        string v = raw.Trim();
        return v.Equals("1", StringComparison.OrdinalIgnoreCase)
            || v.Equals("true", StringComparison.OrdinalIgnoreCase)
            || v.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || v.Equals("on", StringComparison.OrdinalIgnoreCase);
    }
}

public static class WorkVpnFeatureGate
{
    public const string DisabledErrorCode = "FeatureDisabled";

    public static void ThrowIfDisabled()
    {
        if (!FeatureFlags.WorkVpn)
        {
            throw new WorkVpnFeatureDisabledException();
        }
    }
}

public sealed class WorkVpnFeatureDisabledException : Exception
{
    public WorkVpnFeatureDisabledException()
        : base(WorkVpnFeatureGate.DisabledErrorCode)
    {
    }
}

public static class WorkVpnLiveStatusFactory
{
    public static WorkVpnLiveStatus Disabled() => new()
    {
        FeatureEnabled = false,
        Configured = false,
        WorkVpnReady = false,
        State = WorkVpnSessionState.Disconnected,
        Connected = false,
        WaitingForMfa = false,
        ProfilePath = null,
        AdapterName = null,
        InterfaceIndex = null,
        Address = null,
        ProcessId = null,
        LastError = null,
        RecentLog = [],
    };
}

public static class WorkVpnUiProjection
{
    public static bool IsVisible => FeatureFlags.WorkVpn;
}