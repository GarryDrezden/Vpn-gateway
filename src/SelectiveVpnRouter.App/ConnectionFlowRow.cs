using SelectiveVpnRouter.Core;

using System.IO;

namespace SelectiveVpnRouter.App;

public sealed class ConnectionFlowRow
{
    public ConnectionFlowRow(FlowEvent flow)
    {
        Application = ResolveApplicationName(flow);
        Destination = $"{flow.Destination}:{flow.Port}";
        Route = flow.Route switch
        {
            FlowRoute.Vpn => "VPN",
            FlowRoute.Direct => "Напрямую",
            FlowRoute.Blocked => "Блок",
            _ => "-",
        };
        State = ConnectionUxProjection.FormatDisplayState(
            ConnectionUxProjection.ClassifyDisplayState(flow.Status));
        Time = flow.UpdatedAt.ToLocalTime().ToString("HH:mm:ss");
        ProcessPath = flow.ProcessPath;
        FlowId = flow.FlowId;
    }

    public string Application { get; }
    public string Destination { get; }
    public string Route { get; }
    public string State { get; }
    public string Time { get; }
    public string ProcessPath { get; }
    public Guid FlowId { get; }

    private static string ResolveApplicationName(FlowEvent flow) =>
        ApplicationRulesHelper.ResolveFlowApplicationDisplayName(flow);
}