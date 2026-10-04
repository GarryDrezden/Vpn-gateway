namespace SelectiveVpnRouter.Core;

public enum AppRoutePresentation
{
    Vpn,
    Direct,
    Mixed,
    Unknown,
}

public enum ConnectionRouteFilterKind
{
    All,
    Vpn,
    Direct,
    Mixed,
    Errors,
}

public enum ConnectionFlowDisplayState
{
    Active,
    Connecting,
    Closing,
    Failed,
    Unknown,
}

public sealed record ConnectionFlowProjection(
    Guid FlowId,
    string ApplicationName,
    string ProcessPath,
    int Pid,
    string RemoteEndpoint,
    string RouteLabel,
    FlowRoute Route,
    ConnectionFlowDisplayState DisplayState,
    string RawStatus,
    DateTimeOffset UpdatedAt,
    bool WfpRedirect,
    bool ProxyAccepted,
    string? LocalEndpoint,
    string? RuleName)
{
    public string UpdatedAtLocalText => UpdatedAt.ToLocalTime().ToString("HH:mm:ss");
    public string DisplayStateLabel => ConnectionUxProjection.FormatDisplayState(DisplayState);
    public string RouteBadgeKey => ConnectionUxProjection.RouteBadgeKey(Route, DisplayState);
}

public sealed record ConnectionEndpointAggregateProjection(
    string AggregateKey,
    string RemoteEndpoint,
    string RouteLabel,
    FlowRoute Route,
    ConnectionFlowDisplayState DisplayState,
    int Count,
    ConnectionFlowProjection SampleFlow,
    IReadOnlyList<ConnectionFlowProjection> Flows)
{
    public string DisplayStateLabel => ConnectionUxProjection.FormatDisplayState(DisplayState);
    public string RouteBadgeKey => ConnectionUxProjection.RouteBadgeKey(Route, DisplayState);
    public string CountSuffix => Count > 1 ? $"×{Count}" : "";
}

public sealed record ConnectionAppGroupProjection(
    string GroupKey,
    string ApplicationName,
    string ProcessPath,
    AppRoutePresentation RouteSummary,
    string RouteSummaryLabel,
    int ActiveConnectionCount,
    int VpnFlowCount,
    int DirectFlowCount,
    int BlockedFlowCount,
    string StateSummary,
    DateTimeOffset? FirstActivityUtc,
    DateTimeOffset? LastActivityUtc,
    IReadOnlyList<ConnectionFlowProjection> ActiveFlows,
    IReadOnlyList<ConnectionFlowProjection> RecentFlows,
    IReadOnlyList<ConnectionEndpointAggregateProjection> ActiveEndpointAggregates)
{
    public int TotalVisibleFlows => ActiveFlows.Count + RecentFlows.Count;
    public string RouteBadgeKey => ConnectionUxProjection.AppRouteBadgeKey(RouteSummary);
    public string ActiveCountLabel => ConnectionUxProjection.FormatActiveCountLabel(ActiveConnectionCount);
    public string MixedBreakdown =>
        RouteSummary == AppRoutePresentation.Mixed
            ? $"VPN: {VpnFlowCount} · Напряму: {DirectFlowCount}"
            : "";
}

public sealed record ConnectionsSummaryHeader(
    int ApplicationCount,
    int VpnApplicationCount,
    int DirectApplicationCount,
    int MixedApplicationCount,
    int TotalConnectionCount,
    int ActiveConnectionCount)
{
    public string ApplicationsLine =>
        $"Активных приложений: {ApplicationCount}  ·  VPN: {VpnApplicationCount} · Напряму: {DirectApplicationCount}" +
        (MixedApplicationCount > 0 ? $" · Смешано: {MixedApplicationCount}" : "") +
        $" · Соединений: {TotalConnectionCount}";
}

public sealed record ConnectionsBoardProjection(
    bool VpnRoutingReady,
    string RoutingStatusLine,
    ConnectionsSummaryHeader Summary,
    IReadOnlyList<ConnectionAppGroupProjection> ActiveGroups,
    IReadOnlyList<ConnectionAppGroupProjection> RecentOnlyGroups,
    bool ShowDisconnectedEmptyState,
    bool ShowNoTrafficEmptyState)
{
    public IReadOnlyList<ConnectionAppGroupProjection> AllDisplayGroups =>
        ActiveGroups.Concat(RecentOnlyGroups).ToList();
}

public static class ConnectionUxProjection
{
    public static readonly TimeSpan DefaultRecentClosedWindow = TimeSpan.FromMinutes(2);

    public static ConnectionsBoardProjection Build(
        IEnumerable<FlowEvent> flows,
        bool vpnRoutingReady,
        ConnectionRouteFilterKind routeFilter,
        string? search,
        DateTimeOffset? utcNow = null,
        TimeSpan? recentClosedWindow = null,
        PackagedRoutingTargetIndex? packagedRoutingIndex = null)
    {
        DateTimeOffset now = utcNow ?? DateTimeOffset.UtcNow;
        TimeSpan recentWindow = recentClosedWindow ?? DefaultRecentClosedWindow;

        IEnumerable<FlowEvent> visible = FlowPresentationHelper.SelectUserFlows(flows);
        if (routeFilter == ConnectionRouteFilterKind.Errors)
        {
            visible = visible.Where(f => FlowStatusHelper.IsError(f.Status) || FlowStatusHelper.IsCancelled(f.Status));
        }

        List<ConnectionFlowProjection> projections = visible
            .Select(f => ToFlowProjection(f, now, recentWindow, packagedRoutingIndex))
            .ToList();

        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.Trim();
            projections = projections.Where(p =>
                p.ApplicationName.Contains(term, StringComparison.OrdinalIgnoreCase)
                || p.ProcessPath.Contains(term, StringComparison.OrdinalIgnoreCase)
                || p.RemoteEndpoint.Contains(term, StringComparison.OrdinalIgnoreCase)
                || p.RawStatus.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        var grouped = projections
            .GroupBy(p => NormalizeGroupKey(p.ProcessPath, packagedRoutingIndex), StringComparer.OrdinalIgnoreCase)
            .Select(g => BuildGroup(g.Key, g.ToList(), now, recentWindow))
            .Where(g => g.TotalVisibleFlows > 0)
            .ToList();

        grouped = ApplyRouteFilter(grouped, routeFilter).ToList();

        List<ConnectionAppGroupProjection> activeGroups = grouped
            .Where(g => g.ActiveFlows.Count > 0)
            .OrderByDescending(g => g.LastActivityUtc)
            .ThenBy(g => g.ApplicationName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        List<ConnectionAppGroupProjection> recentOnly = grouped
            .Where(g => g.ActiveFlows.Count == 0 && g.RecentFlows.Count > 0)
            .OrderByDescending(g => g.LastActivityUtc)
            .ThenBy(g => g.ApplicationName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        ConnectionsSummaryHeader summary = BuildSummary(activeGroups, recentOnly);

        string routingLine = vpnRoutingReady
            ? "Маршрутизация активна"
            : "VPN Route отключён";

        bool showDisconnected = !vpnRoutingReady;
        bool showNoTraffic = vpnRoutingReady && activeGroups.Count == 0 && recentOnly.Count == 0;

        return new ConnectionsBoardProjection(
            vpnRoutingReady,
            routingLine,
            summary,
            activeGroups,
            recentOnly,
            showDisconnected,
            showNoTraffic);
    }

    public static ConnectionFlowProjection ToFlowProjection(
        FlowEvent flow,
        DateTimeOffset utcNow,
        TimeSpan recentClosedWindow,
        PackagedRoutingTargetIndex? packagedRoutingIndex = null)
    {
        ConnectionFlowDisplayState displayState = ClassifyDisplayState(flow.Status);
        bool recentClosed = displayState == ConnectionFlowDisplayState.Closing
            && utcNow - flow.UpdatedAt <= recentClosedWindow;

        if (displayState == ConnectionFlowDisplayState.Closing && !recentClosed)
        {
            displayState = ConnectionFlowDisplayState.Unknown;
        }

        return new ConnectionFlowProjection(
            flow.FlowId,
            ApplicationRulesHelper.ResolveFlowApplicationDisplayName(flow, packagedRoutingIndex),
            flow.ProcessPath,
            flow.Pid,
            $"{flow.Destination}:{flow.Port}",
            RouteLabel(flow.Route),
            flow.Route,
            displayState,
            flow.Status,
            flow.UpdatedAt,
            flow.WfpRedirect,
            flow.ProxyAccepted,
            flow.OutboundLocalEndpoint,
            flow.RuleName);
    }

    public static ConnectionFlowDisplayState ClassifyDisplayState(string status)
    {
        if (FlowStatusHelper.IsError(status) || FlowStatusHelper.IsCancelled(status))
        {
            return ConnectionFlowDisplayState.Failed;
        }

        if (status == FlowLifecycle.Connecting)
        {
            return ConnectionFlowDisplayState.Connecting;
        }

        if (status is FlowLifecycle.Connected or FlowLifecycle.Relaying)
        {
            return ConnectionFlowDisplayState.Active;
        }

        if (status == FlowLifecycle.Closed)
        {
            return ConnectionFlowDisplayState.Closing;
        }

        if (!FlowStatusHelper.IsTerminal(status))
        {
            return ConnectionFlowDisplayState.Connecting;
        }

        if (FlowStatusHelper.IsBypass(status) || FlowStatusHelper.IsRejected(status))
        {
            return ConnectionFlowDisplayState.Failed;
        }

        return ConnectionFlowDisplayState.Unknown;
    }

    public static bool IsActiveFlow(ConnectionFlowProjection flow) =>
        flow.DisplayState is ConnectionFlowDisplayState.Active or ConnectionFlowDisplayState.Connecting;

    public static bool IsRecentClosedFlow(ConnectionFlowProjection flow, DateTimeOffset utcNow, TimeSpan window) =>
        flow.DisplayState == ConnectionFlowDisplayState.Closing && utcNow - flow.UpdatedAt <= window;

    public static string FormatDisplayState(ConnectionFlowDisplayState state) => state switch
    {
        ConnectionFlowDisplayState.Active => "Активно",
        ConnectionFlowDisplayState.Connecting => "Подключение…",
        ConnectionFlowDisplayState.Closing => "Закрыто",
        ConnectionFlowDisplayState.Failed => "Ошибка",
        _ => "Неизвестно",
    };

    public static string FormatActiveCountLabel(int count) =>
        count == 1 ? "1 активное" : $"{count} активных";

    public static string RouteLabel(FlowRoute route) => route switch
    {
        FlowRoute.Vpn => "VPN",
        FlowRoute.Direct => "Напряму",
        FlowRoute.Blocked => "BLOCKED",
        _ => "UNKNOWN",
    };

    public static string AppRouteSummaryLabel(AppRoutePresentation summary) => summary switch
    {
        AppRoutePresentation.Vpn => "VPN",
        AppRoutePresentation.Direct => "Напряму",
        AppRoutePresentation.Mixed => "Смешано",
        _ => "UNKNOWN",
    };

    public static string RouteBadgeKey(FlowRoute route, ConnectionFlowDisplayState displayState)
    {
        if (displayState == ConnectionFlowDisplayState.Failed)
        {
            return "RouteBadgeWarning";
        }

        return route switch
        {
            FlowRoute.Vpn => "RouteBadgeVpn",
            FlowRoute.Direct => "RouteBadgeDirect",
            FlowRoute.Blocked => "RouteBadgeUnknown",
            _ => "RouteBadgeUnknown",
        };
    }

    public static string AppRouteBadgeKey(AppRoutePresentation summary) => summary switch
    {
        AppRoutePresentation.Vpn => "RouteBadgeVpn",
        AppRoutePresentation.Direct => "RouteBadgeDirect",
        AppRoutePresentation.Mixed => "RouteBadgeMixed",
        _ => "RouteBadgeUnknown",
    };

    internal static string NormalizeGroupKey(string processPath, PackagedRoutingTargetIndex? packagedRoutingIndex = null)
    {
        if (string.IsNullOrWhiteSpace(processPath))
        {
            return "(unknown)";
        }

        if (packagedRoutingIndex is not null)
        {
            return packagedRoutingIndex.NormalizeConnectionsGroupKey(processPath);
        }

        try
        {
            return Path.GetFullPath(processPath);
        }
        catch
        {
            return processPath.Trim();
        }
    }

    private static ConnectionAppGroupProjection BuildGroup(
        string groupKey,
        IReadOnlyList<ConnectionFlowProjection> flows,
        DateTimeOffset utcNow,
        TimeSpan recentWindow)
    {
        List<ConnectionFlowProjection> active = flows.Where(IsActiveFlow).OrderByDescending(f => f.UpdatedAt).ToList();
        List<ConnectionFlowProjection> recent = flows
            .Where(f => IsRecentClosedFlow(f, utcNow, recentWindow))
            .OrderByDescending(f => f.UpdatedAt)
            .ToList();

        AppRoutePresentation routeSummary = SummarizeRoutes(flows.Select(f => f.Route));
        int vpnCount = flows.Count(f => f.Route == FlowRoute.Vpn);
        int directCount = flows.Count(f => f.Route == FlowRoute.Direct);
        int blockedCount = flows.Count(f => f.Route == FlowRoute.Blocked);

        DateTimeOffset? first = flows.Count > 0 ? flows.Min(f => f.UpdatedAt) : null;
        DateTimeOffset? last = flows.Count > 0 ? flows.Max(f => f.UpdatedAt) : null;

        string stateSummary = SummarizeGroupState(active, recent);
        IReadOnlyList<ConnectionEndpointAggregateProjection> aggregates = AggregateActiveEndpoints(active);

        return new ConnectionAppGroupProjection(
            groupKey,
            flows[0].ApplicationName,
            flows[0].ProcessPath,
            routeSummary,
            AppRouteSummaryLabel(routeSummary),
            active.Count,
            vpnCount,
            directCount,
            blockedCount,
            stateSummary,
            first,
            last,
            active,
            recent,
            aggregates);
    }

    private static IEnumerable<ConnectionAppGroupProjection> ApplyRouteFilter(
        IEnumerable<ConnectionAppGroupProjection> groups,
        ConnectionRouteFilterKind filter)
    {
        return filter switch
        {
            ConnectionRouteFilterKind.Vpn => groups.Where(g => g.RouteSummary == AppRoutePresentation.Vpn),
            ConnectionRouteFilterKind.Direct => groups.Where(g => g.RouteSummary == AppRoutePresentation.Direct),
            ConnectionRouteFilterKind.Mixed => groups.Where(g => g.RouteSummary == AppRoutePresentation.Mixed),
            _ => groups,
        };
    }

    private static ConnectionsSummaryHeader BuildSummary(
        IReadOnlyList<ConnectionAppGroupProjection> activeGroups,
        IReadOnlyList<ConnectionAppGroupProjection> recentOnly)
    {
        var all = activeGroups.Concat(recentOnly).ToList();
        int vpnApps = all.Count(g => g.RouteSummary == AppRoutePresentation.Vpn);
        int directApps = all.Count(g => g.RouteSummary == AppRoutePresentation.Direct);
        int mixedApps = all.Count(g => g.RouteSummary == AppRoutePresentation.Mixed);
        int totalConnections = all.Sum(g => g.TotalVisibleFlows);
        int activeConnections = activeGroups.Sum(g => g.ActiveConnectionCount);

        return new ConnectionsSummaryHeader(
            activeGroups.Count,
            vpnApps,
            directApps,
            mixedApps,
            totalConnections,
            activeConnections);
    }

    private static AppRoutePresentation SummarizeRoutes(IEnumerable<FlowRoute> routes)
    {
        bool vpn = routes.Any(r => r == FlowRoute.Vpn);
        bool direct = routes.Any(r => r == FlowRoute.Direct);
        if (vpn && direct)
        {
            return AppRoutePresentation.Mixed;
        }

        if (vpn)
        {
            return AppRoutePresentation.Vpn;
        }

        if (direct)
        {
            return AppRoutePresentation.Direct;
        }

        return AppRoutePresentation.Unknown;
    }

    private static string SummarizeGroupState(
        IReadOnlyList<ConnectionFlowProjection> active,
        IReadOnlyList<ConnectionFlowProjection> recent)
    {
        if (active.Count > 0)
        {
            if (active.Any(f => f.DisplayState == ConnectionFlowDisplayState.Connecting))
            {
                return "Подключение…";
            }

            return "Активно";
        }

        return recent.Count > 0 ? "Закрыто" : "Неактивно";
    }

    public static IReadOnlyList<ConnectionEndpointAggregateProjection> AggregateActiveEndpoints(
        IReadOnlyList<ConnectionFlowProjection> activeFlows)
    {
        return activeFlows
            .GroupBy(FormEndpointAggregateKey)
            .Select(g =>
            {
                List<ConnectionFlowProjection> list = g.OrderByDescending(f => f.UpdatedAt).ToList();
                ConnectionFlowProjection sample = list[0];
                return new ConnectionEndpointAggregateProjection(
                    g.Key,
                    sample.RemoteEndpoint,
                    sample.RouteLabel,
                    sample.Route,
                    sample.DisplayState,
                    list.Count,
                    sample,
                    list);
            })
            .OrderBy(a => a.RemoteEndpoint, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.RouteLabel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.DisplayState)
            .ToList();
    }

    public static string FormEndpointAggregateKey(ConnectionFlowProjection flow) =>
        $"{flow.RemoteEndpoint}|{flow.Route}|{flow.DisplayState}";
}
