using SelectiveVpnRouter.Core.RoutingTrace;

namespace SelectiveVpnRouter.App;

public sealed class RoutingTraceEventRow
{
    public RoutingTraceEventRow(RoutingTraceEvent e)
    {
        Event = e;
        Time = e.Timestamp.ToLocalTime().ToString("HH:mm:ss");
        Process = string.IsNullOrWhiteSpace(e.ProcessPath) ? $"pid:{e.ProcessId}" : System.IO.Path.GetFileName(e.ProcessPath);
        Proto = $"{e.Protocol}{(int)e.AddressFamily % 10}";
        Destination = $"{e.RemoteAddress}:{e.RemotePort}";
        Expected = FormatRoute(e.ExpectedRoute);
        Observed = FormatObserved(e.ObservedRoute);
        Status = RoutingTraceUiText.FormatGridStatus(e);
    }

    public RoutingTraceEvent Event { get; private set; }
    public string Time { get; }
    public string Process { get; }
    public string Proto { get; }
    public string Destination { get; }
    public string Expected { get; }
    public string Observed { get; }
    public string Status { get; }

    public void Update(RoutingTraceEvent updated)
    {
        Event = updated;
    }

    public string Details =>
        $"Process: {Event.ProcessPath}{Environment.NewLine}" +
        $"PID: {Event.ProcessId}  Parent: {Event.ParentProcessId?.ToString() ?? "—"}{Environment.NewLine}" +
        $"Protocol: {Event.Protocol} / {Event.AddressFamily}{Environment.NewLine}" +
        $"Remote: {Destination}{Environment.NewLine}" +
        (string.IsNullOrWhiteSpace(Event.Hostname) ? "" : $"Hostname: {Event.Hostname}{Environment.NewLine}") +
        $"Expected: {Expected}{Environment.NewLine}" +
        $"Observed: {Observed}{Environment.NewLine}" +
        $"Evidence: {Event.EvidenceFlags}{Environment.NewLine}" +
        $"Outcome: {Event.Outcome}";

    private static string FormatRoute(RoutingTraceExpectedRoute route) =>
        route switch
        {
            RoutingTraceExpectedRoute.Vpn => "VPN",
            RoutingTraceExpectedRoute.Direct => "Direct",
            RoutingTraceExpectedRoute.Local => "Local",
            _ => "Default",
        };

    private static string FormatObserved(RoutingTraceObservedRoute route) =>
        route switch
        {
            RoutingTraceObservedRoute.Vpn => "VPN",
            RoutingTraceObservedRoute.Direct => "DIRECT",
            RoutingTraceObservedRoute.Local => "LOCAL",
            RoutingTraceObservedRoute.Uncovered => "UNCOVERED",
            _ => "UNKNOWN",
        };

    private static string FormatStatus(RoutingTraceEvent e)
    {
        if (RoutingTraceObservedRouteClassifier.IsConfirmedLeak(e.ExpectedRoute, e.ObservedRoute, e.DestinationKind))
        {
            return "LEAK";
        }

        if (e.ObservedRoute == RoutingTraceObservedRoute.Uncovered)
        {
            return "GAP";
        }

        if (e.Outcome == RoutingTraceOutcome.Connected)
        {
            return "OK";
        }

        if (e.Outcome == RoutingTraceOutcome.Failed)
        {
            return "FAIL";
        }

        return "…";
    }
}
