using SelectiveVpnRouter.Core;

namespace SelectiveVpnRouter.Service;

public sealed partial class RouterEngine
{
    private string? _tempRealAppExePath;

    public TempAppVpnStatus GetTempAppVpnStatus()
    {
        string? exe = _tempRealAppExePath;
        WfpPolicyDiagnostics policy = WfpPolicy;
        WfpFilterInstallResult? filter = exe is null ? null : WfpPolicyHealth.FindCalloutFilter(policy, exe);
        IReadOnlyList<FlowEvent> flows = Proxy?.Flows ?? [];
        var status = new TempAppVpnStatus
        {
            Active = !string.IsNullOrWhiteSpace(exe),
            ExePath = exe,
            FileExists = exe is not null && File.Exists(exe),
            AppIdResolved = filter?.AppIdResolved == true,
            FilterInstalled = filter?.FilterInstalled == true,
            FilterId = filter?.FilterId ?? 0,
            Error = filter?.Error,
            ObservationState = RealAppRoutingObservation.ComputeState(flows, exe),
            Flows = flows
                .Where(f => exe is not null && string.Equals(f.ProcessPath, Path.GetFullPath(exe), StringComparison.OrdinalIgnoreCase))
                .Select(RealAppRoutingObservation.FromFlow)
                .ToArray(),
        };

        return status with { Summary = RealAppRoutingObservation.BuildSummary(status) };
    }

    public async Task<TempAppVpnStatus> ApplyTempAppVpnRouteAsync(string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath))
        {
            throw new ArgumentException("Executable path is required.", nameof(exePath));
        }

        _tempRealAppExePath = Path.GetFullPath(exePath);
        await RefreshPolicyAsync().ConfigureAwait(false);
        Log("temp-app-vpn-enabled exe=" + _tempRealAppExePath);
        return GetTempAppVpnStatus();
    }

    public async Task<TempAppVpnStatus> RemoveTempAppVpnRouteAsync()
    {
        _tempRealAppExePath = null;
        await RefreshPolicyAsync().ConfigureAwait(false);
        Log("temp-app-vpn-removed");
        return GetTempAppVpnStatus();
    }

    private void ClearTempRealAppRoute()
    {
        if (_tempRealAppExePath is null)
        {
            return;
        }

        _tempRealAppExePath = null;
        Log("temp-app-vpn-cleared-on-disconnect");
    }
}