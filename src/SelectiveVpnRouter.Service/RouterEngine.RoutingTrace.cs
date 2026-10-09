using SelectiveVpnRouter.Core;
using SelectiveVpnRouter.Core.BrowserRouting;
using SelectiveVpnRouter.Core.RoutingTrace;
using SelectiveVpnRouter.Network;

namespace SelectiveVpnRouter.Service;

public sealed partial class RouterEngine
{
    private readonly RoutingTraceHost _routingTrace = new(new WindowsPackagedApplicationPathResolver());

    public RouterEngine(
        Lazy<IBrowserIntegrationSnapshotProvider> browserIntegrationSnapshot,
        VpnSessionDnsStore sessionDns)
    {
        _browserIntegrationSnapshot = browserIntegrationSnapshot;
        _sessionDns = sessionDns;
        _routingTrace.BindConfigProvider(() => Config);
    }

    public RoutingTraceSession StartRoutingTrace(StartRoutingTraceRequest request)
        => _routingTrace.Start(request, _proxy);

    public RoutingTraceSession? StopRoutingTrace()
        => _routingTrace.Stop(_proxy);

    public bool TryClearRoutingTrace(out string? errorCode)
        => _routingTrace.TryClear(out errorCode);

    public RoutingTraceStatus GetRoutingTraceStatus()
        => _routingTrace.GetStatus();

    public RoutingTraceSnapshot GetRoutingTraceSnapshot()
        => _routingTrace.GetSnapshot();

    public RoutingTraceEventsPage GetRoutingTraceEvents(GetRoutingTraceEventsRequest request)
        => _routingTrace.GetEvents(request);

    public string ExportRoutingTraceJson()
    {
        if (!_routingTrace.TryGetExportData(out RoutingTraceSession? session, out IReadOnlyList<RoutingTraceEvent> events)
            || session is null)
        {
            throw new InvalidOperationException("routing_trace_unavailable");
        }

        return RoutingTraceExport.ToRedactedJson(session, events);
    }

    public string ExportRoutingTraceText()
    {
        if (!_routingTrace.TryGetExportData(out RoutingTraceSession? session, out IReadOnlyList<RoutingTraceEvent> events)
            || session is null)
        {
            throw new InvalidOperationException("routing_trace_unavailable");
        }

        return RoutingTraceExport.FormatTextReport(session, events);
    }
}
