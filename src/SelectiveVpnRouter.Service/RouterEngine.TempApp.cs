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
            FilterInstalled = filter?.FilterInstalled == true,
            FilterId = filter?.FilterId ?? 0,
            Error = filter?.Error,
            QueriedAt = DateTimeOffset.UtcNow,
            SelectedFlow = selected,
            RecentFlows = recentDtos.Select(TempAppVpnFlowDtoToObservation).ToArray(),
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
        Log("temp-app-vpn-cleared-on-disconnect");
    }
}