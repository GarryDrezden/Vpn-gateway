namespace SelectiveVpnRouter.Core;

public enum WorkVpnSessionState
{
    Disconnected = 0,
    Connecting = 1,
    WaitingForCredentials = 2,
    WaitingForMfa = 3,
    Connected = 4,
    Disconnecting = 5,
    Failed = 6,
}

public sealed record WorkVpnSettings
{
    public bool Enabled { get; init; } = true;
    public string ProfilePath { get; init; } = "";
    public string? Username { get; init; }
    public bool RememberUsername { get; init; } = true;
}

public sealed record ConnectWorkVpnRequest
{
    public string? OpenVpnPath { get; init; }
    public string? ProfilePath { get; init; }
    public string? Username { get; init; }
    public string? Password { get; init; }
    public bool? DisableDco { get; init; }
}

public static class WorkVpnProfileDefaults
{
    public static string? TryDetectKnownDevProfile()
    {
        string path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "OpenVPN",
            "config",
            "vpn-valpolyakov",
            "vpn-valpolyakov.ovpn");
        return File.Exists(path) ? path : null;
    }

    public static WorkVpnSettings WithMigrationDefaults(WorkVpnSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.ProfilePath))
        {
            return settings;
        }

        string? detected = TryDetectKnownDevProfile();
        return detected is null ? settings : settings with { ProfilePath = detected };
    }
}

public static class WorkVpnReadiness
{
    public static bool IsReady(
        WorkVpnSessionState state,
        bool processRunning,
        int? interfaceIndex,
        string? tunnelIpv4)
        => state == WorkVpnSessionState.Connected
            && processRunning
            && interfaceIndex is > 0
            && !string.IsNullOrWhiteSpace(tunnelIpv4);
}

public static class OpenVpnProcessOwnership
{
    public static bool IsOwnedSelectiveProcess(int? pid, int? selectivePid)
        => pid is > 0 && selectivePid is > 0 && pid == selectivePid;

    public static bool IsOwnedWorkProcess(int? pid, int? workPid)
        => pid is > 0 && workPid is > 0 && pid == workPid;

    /// <summary>
    /// Returns true when the PID is owned by this service (selective or work session).
    /// </summary>
    public static bool IsServiceOwnedOpenVpn(int? pid, int? selectivePid, int? workPid)
        => IsOwnedSelectiveProcess(pid, selectivePid) || IsOwnedWorkProcess(pid, workPid);

    public static bool MayServiceTerminate(int pid, int? selectivePid, int? workPid)
        => IsServiceOwnedOpenVpn(pid, selectivePid, workPid);
}

public static class WorkVpnRouteClassification
{
    public static bool IsLikelyCorporateSplitTunnelRoute(string destinationPrefix)
    {
        if (string.IsNullOrWhiteSpace(destinationPrefix))
        {
            return false;
        }

        string prefix = destinationPrefix.Trim();
        if (prefix.StartsWith("10.", StringComparison.Ordinal))
        {
            return true;
        }

        if (prefix.StartsWith("172.", StringComparison.Ordinal))
        {
            return true;
        }

        if (prefix.StartsWith("192.168.", StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    public static bool IsFullTunnelDefaultRoute(string destinationPrefix)
    {
        if (string.IsNullOrWhiteSpace(destinationPrefix))
        {
            return false;
        }

        string p = destinationPrefix.Trim();
        return p is "0.0.0.0/0" or "0.0.0.0/1" or "128.0.0.0/1";
    }
}
