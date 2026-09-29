using SelectiveVpnRouter.Core;

namespace SelectiveVpnRouter.App;

public sealed class RealAppFlowHistoryRow
{
    public Guid FlowId { get; init; }
    public string UpdatedAt { get; init; } = "";
    public int Pid { get; init; }
    public string Destination { get; init; } = "";
    public string Status { get; init; } = "";
    public string Wfp { get; init; } = "";
    public string Proxy { get; init; } = "";
    public string Context { get; init; } = "";
    public string Bound { get; init; } = "";
    public string Connected { get; init; } = "";
    public string Route { get; init; } = "";
    public string Error { get; init; } = "";
    public bool IsTarget { get; init; }

    private static string Mark(bool value) => value ? "\u2713" : "\u2014";

    public static RealAppFlowHistoryRow FromDto(
        TempAppVpnFlowDto dto,
        IEnumerable<string> targetAddresses,
        int targetPort)
    {
        bool isTarget = TargetFlowMatcher.MatchesTarget(dto, targetAddresses, targetPort);
        string error = "\u2014";

        if (FlowStatusHelper.IsError(dto.Status) ||
            FlowStatusHelper.IsCancelled(dto.Status))
        {
            error = dto.ErrorReason ?? dto.ErrorMessage ?? dto.Status;
        }

        return new RealAppFlowHistoryRow
        {
            FlowId = dto.FlowId,
            UpdatedAt = dto.UpdatedAt.ToLocalTime().ToString("HH:mm:ss"),
            Pid = dto.ProcessId,
            Destination = dto.Destination + ":" + dto.Port,
            Status = dto.Status,
            Wfp = Mark(dto.WfpRedirect),
            Proxy = Mark(dto.ProxyAccepted),
            Context = Mark(dto.RedirectContextRecovered),
            Bound = Mark(dto.VpnOutboundBound),
            Connected = Mark(dto.VpnOutboundConnected),
            Route = dto.Route.ToString(),
            Error = error,
            IsTarget = isTarget,
        };
    }
}
