using System.Net;
using SelectiveVpnRouter.Core;
using SelectiveVpnRouter.Network;

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
        bool ipv6Block = Config.Vpn.Ipv6Policy is Ipv6Policy.BlockForVpnRoutedApps
            || (Config.Vpn.Ipv6Policy is Ipv6Policy.Auto && _vpnAdapter?.Ipv6.Any(a => !a.StartsWith("fe80", StringComparison.OrdinalIgnoreCase)) != true);

        IReadOnlyList<FlowEvent> matching = exe is null
            ? []
            : flows
                .Where(f => string.Equals(f.ProcessPath, Path.GetFullPath(exe), StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => f.UpdatedAt)
                .ThenByDescending(f => f.SequenceId)
                .ToArray();

        RealAppFlowObservation? selected = matching.FirstOrDefault() is FlowEvent latest
            ? RealAppRoutingObservation.FromFlow(latest)
            : null;

        string? routeDiagnostic = null;
        if (selected is not null && IPAddress.TryParse(selected.Destination, out IPAddress? target))
        {
            routeDiagnostic = VpnRouteDiagnostics.Summarize(
                target,
                _vpnAdapter?.Ipv4Index,
                _vpn?.RouteGateway,
                _owned.Any(r => r.Reason == "vpn-transport-high-metric"));
        }

        var status = new TempAppVpnStatus
        {
            Active = !string.IsNullOrWhiteSpace(exe),
            ExePath = exe,
            FileExists = exe is not null && File.Exists(exe),
            AppIdResolved = filter?.AppIdResolved == true,
            FilterInstalled = filter?.FilterInstalled == true,
            FilterId = filter?.FilterId ?? 0,
            Error = filter?.Error,
            QueriedAt = DateTimeOffset.UtcNow,
            SelectedFlow = selected,
            RecentFlows = matching.Take(10).Select(RealAppRoutingObservation.FromFlow).ToArray(),
            ObservationState = RealAppRoutingObservation.ComputeState(selected),
            WfpLayerAudit = WfpPolicyLayerAudit.Summary(_driver?.IsLoaded == true, ipv6Block),
            RouteDiagnostic = routeDiagnostic,
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