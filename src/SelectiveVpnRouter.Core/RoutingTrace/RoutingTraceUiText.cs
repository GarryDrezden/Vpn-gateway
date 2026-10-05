namespace SelectiveVpnRouter.Core.RoutingTrace;

public static class RoutingTraceUiText
{
    public static string FormatActiveMonitoring(TimeSpan elapsed, string? targetName)
        => $"\u25cf \u041c\u043e\u043d\u0438\u0442\u043e\u0440\u0438\u043d\u0433 {elapsed:mm\\:ss} \u2014 {targetName}";

    public static string FormatCompletedReport(TimeSpan duration, string? targetName)
        => $"\u041f\u043e\u0441\u043b\u0435\u0434\u043d\u0438\u0439 \u043e\u0442\u0447\u0451\u0442 \u2014 {targetName} \u00b7 {duration:mm\\:ss}";

    public static string MonitoringNotStarted => "\u041c\u043e\u043d\u0438\u0442\u043e\u0440\u0438\u043d\u0433 \u043d\u0435 \u0437\u0430\u043f\u0443\u0449\u0435\u043d";

    public static string MonitoringEndedNoActiveSession => "\u041c\u043e\u043d\u0438\u0442\u043e\u0440\u0438\u043d\u0433 \u0437\u0430\u0432\u0435\u0440\u0448\u0451\u043d. \u0410\u043a\u0442\u0438\u0432\u043d\u043e\u0439 \u0441\u0435\u0441\u0441\u0438\u0438 \u043d\u0435\u0442.";

    public static string FormatGridStatus(RoutingTraceEvent e) => e.CoverageReason switch
    {
        RoutingTraceCoverageReason.PreExistingAtTraceStart => "\u0414\u043e \u043c\u043e\u043d\u0438\u0442\u043e\u0440\u0438\u043d\u0433\u0430",
        RoutingTraceCoverageReason.MissingProxyEvidence => "\u041d\u0435\u0442 \u0434\u0430\u043d\u043d\u044b\u0445 \u043f\u0440\u043e\u043a\u0441\u0438",
        RoutingTraceCoverageReason.InsufficientEvidence => "\u041d\u0435\u0434\u043e\u0441\u0442\u0430\u0442\u043e\u0447\u043d\u043e \u0434\u0430\u043d\u043d\u044b\u0445",
        RoutingTraceCoverageReason.ProvenDirect => "\u0423\u0442\u0435\u0447\u043a\u0430",
        RoutingTraceCoverageReason.LocalBypass => "\u041b\u043e\u043a\u0430\u043b\u044c\u043d\u043e",
        RoutingTraceCoverageReason.UnsupportedProtocol => "GAP",
        RoutingTraceCoverageReason.UnsupportedAddressFamily => "GAP",
        _ when RoutingTraceObservedRouteClassifier.IsConfirmedLeak(e.ExpectedRoute, e.ObservedRoute, e.DestinationKind) => "\u0423\u0442\u0435\u0447\u043a\u0430",
        _ when e.Outcome == RoutingTraceOutcome.Connected && e.ObservedRoute == RoutingTraceObservedRoute.Vpn => "OK",
        _ when e.ObservedRoute == RoutingTraceObservedRoute.Local => "\u041b\u043e\u043a\u0430\u043b\u044c\u043d\u043e",
        _ when e.ObservedRoute == RoutingTraceObservedRoute.Unknown => "\u041d\u0435\u0438\u0437\u0432\u0435\u0441\u0442\u043d\u043e",
        _ => "OK",
    };
}
