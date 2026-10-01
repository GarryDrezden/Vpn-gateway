using System.Net;
using SelectiveVpnRouter.Core;
using SelectiveVpnRouter.Network;

namespace SelectiveVpnRouter.Service;

public sealed partial class RouterEngine
{
    private string? _tempRealAppExePath;
    private WfpAppIdentityPathMode _tempRealAppWfpMode = WfpAppIdentityPathMode.Default;
    private string? _tempRealAppWfpIdentityOverride;

    public TempAppVpnStatus GetTempAppVpnStatus()
    {
        string? exe = _tempRealAppExePath;
        WfpPolicyDiagnostics policy = WfpPolicy;
        IReadOnlyList<WfpFilterInstallResult> calloutFilters = exe is null ? [] : WfpPolicyHealth.FindCalloutFilters(policy, exe);
        WfpFilterInstallResult? filter = calloutFilters.FirstOrDefault(f => !f.IsShortPathFallback)
            ?? calloutFilters.FirstOrDefault();
        WfpFilterInstallResult? shortFilter = calloutFilters.FirstOrDefault(f => f.IsShortPathFallback);
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

        IReadOnlyList<TempAppVpnFlowDto> recentDtos = matching
            .Take(10)
            .Select(TempAppVpnFlowMapper.FromFlow)
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
            FilterInstalled = calloutFilters.Any(f => f.FilterInstalled),
            FilterId = filter?.FilterId ?? 0,
            ShortPathFilterId = shortFilter?.FilterId ?? 0,
            WfpFiltersSummary = calloutFilters.Count == 0 ? null : WfpPolicyHealth.SummarizeCalloutFilters(calloutFilters),
            IdentityPathMode = _tempRealAppWfpMode,
            Error = filter?.Error ?? calloutFilters.FirstOrDefault(f => f.Error is not null)?.Error,
            QueriedAt = DateTimeOffset.UtcNow,
            SelectedFlow = selected,
            RecentFlows = recentDtos.Select(TempAppVpnFlowDtoToObservation).ToArray(),
            ObservationState = RealAppRoutingObservation.ComputeState(selected),
            WfpLayerAudit = WfpPolicyLayerAudit.Summary(_driver?.IsLoaded == true, ipv6Block),
            RouteDiagnostic = routeDiagnostic,
        };

        return status with { Summary = RealAppRoutingObservation.BuildSummary(status) };
    }

    public async Task<TempAppVpnStatus> ApplyTempAppVpnRouteAsync(
        string exePath,
        WfpAppIdentityPathMode identityPathMode = WfpAppIdentityPathMode.Default)
    {
        if (string.IsNullOrWhiteSpace(exePath))
        {
            throw new ArgumentException("Executable path is required.", nameof(exePath));
        }

        _tempRealAppWfpMode = identityPathMode;
        _tempRealAppWfpIdentityOverride = identityPathMode == WfpAppIdentityPathMode.ShortPathOnly
            ? exePath.Trim().Trim('"')
            : null;
        _tempRealAppExePath = Path.GetFullPath(exePath);
        await RefreshPolicyAsync().ConfigureAwait(false);
        Log("temp-app-vpn-enabled exe=" + _tempRealAppExePath
            + " wfpMode=" + _tempRealAppWfpMode
            + (_tempRealAppWfpIdentityOverride is null ? "" : " wfpIdentityOverride=" + _tempRealAppWfpIdentityOverride));
        return GetTempAppVpnStatus();
    }

    public async Task<TempAppVpnStatus> RemoveTempAppVpnRouteAsync()
    {
        _tempRealAppExePath = null;
        _tempRealAppWfpMode = WfpAppIdentityPathMode.Default;
        _tempRealAppWfpIdentityOverride = null;
        await RefreshPolicyAsync().ConfigureAwait(false);
        Log("temp-app-vpn-removed");
        return GetTempAppVpnStatus();
    }

    public TempAppVpnFlowsResponse GetTempAppVpnFlows(string exePath, int maxCount = TempAppVpnFlowQuery.DefaultMaxCount)
    {
        string? path = string.IsNullOrWhiteSpace(exePath) ? _tempRealAppExePath : exePath;
        IReadOnlyList<FlowEvent> flows = Proxy?.Flows ?? [];
        return new TempAppVpnFlowsResponse
        {
            QueriedAt = DateTimeOffset.UtcNow,
            Flows = path is null ? [] : TempAppVpnFlowQuery.Query(flows, path, maxCount),
        };
    }

    private static RealAppFlowObservation TempAppVpnFlowDtoToObservation(TempAppVpnFlowDto dto) =>
        new()
        {
            FlowId = dto.FlowId,
            SequenceId = dto.SequenceId,
            CreatedAt = dto.CreatedAt,
            UpdatedAt = dto.UpdatedAt,
            Pid = dto.ProcessId,
            Destination = dto.Destination,
            Port = dto.Port,
            WfpRedirect = dto.WfpRedirect,
            ProxyAccepted = dto.ProxyAccepted,
            RedirectRecordsApplied = dto.RedirectContextRecovered,
            VpnOutboundCreated = dto.VpnOutboundCreated,
            VpnOutboundBound = dto.VpnOutboundBound,
            VpnOutboundConnected = dto.VpnOutboundConnected,
            Route = dto.Route,
            Status = dto.Status,
            HasFlowError = FlowStatusHelper.IsError(dto.Status) || FlowStatusHelper.IsCancelled(dto.Status),
            RoutingObserved = TargetFlowAcceptance.HasRoutingObserved(dto),
            TcpConnectSuccess = dto.TcpConnectSuccess,
            EgressVerified = dto.TcpConnectSuccess && FlowStatusHelper.IsSuccessTerminal(dto.Status),
            ErrorDetails = dto.ErrorPhase is null && dto.ErrorMessage is null
                ? null
                : new FlowErrorDetails
                {
                    Phase = dto.ErrorPhase ?? "",
                    ExceptionType = dto.ErrorReason ?? "",
                    CancellationReason = dto.ErrorReason,
                    SocketErrorCode = dto.SocketError,
                    NativeErrorCode = dto.NativeError,
                    Message = dto.ErrorMessage ?? "",
                },
        };

    private void ClearTempRealAppRoute()
    {
        if (_tempRealAppExePath is null)
        {
            return;
        }

        _tempRealAppExePath = null;
        _tempRealAppWfpMode = WfpAppIdentityPathMode.Default;
        _tempRealAppWfpIdentityOverride = null;
        Log("temp-app-vpn-cleared-on-disconnect");
    }
}