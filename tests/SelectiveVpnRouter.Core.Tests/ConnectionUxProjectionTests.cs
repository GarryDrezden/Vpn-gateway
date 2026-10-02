using SelectiveVpnRouter.Core;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public class ConnectionUxProjectionTests
{
    private static FlowEvent Flow(
        string exe,
        string dest,
        FlowRoute route,
        string status,
        DateTimeOffset updatedAt,
        string? ruleName = null) =>
        new()
        {
            ProcessPath = exe,
            Destination = dest,
            Port = 443,
            Route = route,
            Status = status,
            UpdatedAt = updatedAt,
            RuleName = ruleName,
        };

    [Fact]
    public void Groups_multiple_flows_by_application_path()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string exe = @"C:\Apps\Telegram.exe";
        var flows = new[]
        {
            Flow(exe, "149.154.1.1", FlowRoute.Vpn, FlowLifecycle.Connected, now),
            Flow(exe, "149.154.1.2", FlowRoute.Vpn, FlowLifecycle.Relaying, now.AddSeconds(-1)),
        };

        ConnectionsBoardProjection board = ConnectionUxProjection.Build(flows, vpnRoutingReady: true, ConnectionRouteFilterKind.All, search: null, now);
        ConnectionAppGroupProjection group = Assert.Single(board.ActiveGroups);
        Assert.Equal(2, group.ActiveConnectionCount);
        Assert.Equal(AppRoutePresentation.Vpn, group.RouteSummary);
    }

    [Fact]
    public void Detects_mixed_route_group()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string exe = @"C:\Apps\Chrome.exe";
        var flows = new[]
        {
            Flow(exe, "1.1.1.1", FlowRoute.Vpn, FlowLifecycle.Connected, now),
            Flow(exe, "2.2.2.2", FlowRoute.Direct, FlowLifecycle.Connected, now),
        };

        ConnectionsBoardProjection board = ConnectionUxProjection.Build(flows, true, ConnectionRouteFilterKind.All, null, now);
        ConnectionAppGroupProjection group = Assert.Single(board.ActiveGroups);
        Assert.Equal(AppRoutePresentation.Mixed, group.RouteSummary);
        Assert.Contains("VPN: 1", group.MixedBreakdown, StringComparison.Ordinal);
        Assert.Contains("\u041d\u0430\u043f\u0440\u044f\u043c\u0443: 1", group.MixedBreakdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Filters_vpn_only_groups()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var flows = new[]
        {
            Flow(@"C:\a.exe", "1.1.1.1", FlowRoute.Vpn, FlowLifecycle.Connected, now),
            Flow(@"C:\b.exe", "2.2.2.2", FlowRoute.Direct, FlowLifecycle.Connected, now),
        };

        ConnectionsBoardProjection board = ConnectionUxProjection.Build(flows, true, ConnectionRouteFilterKind.Vpn, null, now);
        Assert.Single(board.ActiveGroups);
        Assert.Equal("a", Path.GetFileNameWithoutExtension(board.ActiveGroups[0].ProcessPath), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Search_matches_remote_address()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var flows = new[]
        {
            Flow(@"C:\a.exe", "149.154.10.20", FlowRoute.Vpn, FlowLifecycle.Connected, now),
            Flow(@"C:\b.exe", "8.8.8.8", FlowRoute.Direct, FlowLifecycle.Connected, now),
        };

        ConnectionsBoardProjection board = ConnectionUxProjection.Build(flows, true, ConnectionRouteFilterKind.All, "149.154", now);
        Assert.Single(board.ActiveGroups);
    }

    [Fact]
    public void Recent_closed_flows_go_to_recent_section_when_no_active()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var flows = new[]
        {
            Flow(@"C:\a.exe", "1.1.1.1", FlowRoute.Vpn, FlowLifecycle.Closed, now.AddSeconds(-5)),
        };

        ConnectionsBoardProjection board = ConnectionUxProjection.Build(flows, true, ConnectionRouteFilterKind.All, null, now);
        Assert.Empty(board.ActiveGroups);
        ConnectionAppGroupProjection recent = Assert.Single(board.RecentOnlyGroups);
        Assert.Single(recent.RecentFlows);
    }

    [Fact]
    public void Disconnected_state_when_routing_not_ready()
    {
        ConnectionsBoardProjection board = ConnectionUxProjection.Build([], false, ConnectionRouteFilterKind.All, null);
        Assert.True(board.ShowDisconnectedEmptyState);
        Assert.Equal("VPN Route \u043e\u0442\u043a\u043b\u044e\u0447\u0451\u043d", board.RoutingStatusLine);
    }

    [Fact]
    public void Classifies_failed_and_connecting_states()
    {
        Assert.Equal(ConnectionFlowDisplayState.Failed, ConnectionUxProjection.ClassifyDisplayState(FlowLifecycle.Error));
        Assert.Equal(ConnectionFlowDisplayState.Connecting, ConnectionUxProjection.ClassifyDisplayState(FlowLifecycle.Connecting));
        Assert.Equal(ConnectionFlowDisplayState.Active, ConnectionUxProjection.ClassifyDisplayState(FlowLifecycle.Connected));
    }

    [Fact]
    public void Null_path_handled_in_group_key()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var flow = new FlowEvent
        {
            ProcessPath = "",
            Destination = "1.1.1.1",
            Port = 443,
            Route = FlowRoute.Direct,
            Status = FlowLifecycle.Connected,
            UpdatedAt = now,
        };

        Assert.False(FlowPresentationHelper.IsUserVisibleFlow(flow));
    }

    [Fact]
    public void Collapses_identical_active_endpoints()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string exe = @"C:\Apps\Telegram.exe";
        var flows = new FlowEvent[8];
        for (int i = 0; i < 8; i++)
        {
            flows[i] = Flow(exe, "149.154.167.35", FlowRoute.Vpn, FlowLifecycle.Connected, now.AddSeconds(-i));
        }

        ConnectionsBoardProjection board = ConnectionUxProjection.Build(flows, true, ConnectionRouteFilterKind.All, null, now);
        ConnectionAppGroupProjection group = Assert.Single(board.ActiveGroups);
        ConnectionEndpointAggregateProjection row = Assert.Single(group.ActiveEndpointAggregates);
        Assert.Equal(8, row.Count);
        Assert.Equal("\u00d78", row.CountSuffix);
        Assert.Equal(8, group.ActiveFlows.Count);
    }

    [Fact]
    public void Does_not_collapse_different_routes()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string exe = @"C:\Apps\Chrome.exe";
        var flows = new[]
        {
            Flow(exe, "149.154.1.1", FlowRoute.Vpn, FlowLifecycle.Connected, now),
            Flow(exe, "149.154.1.1", FlowRoute.Direct, FlowLifecycle.Connected, now),
        };

        ConnectionsBoardProjection board = ConnectionUxProjection.Build(flows, true, ConnectionRouteFilterKind.All, null, now);
        ConnectionAppGroupProjection group = Assert.Single(board.ActiveGroups);
        Assert.Equal(2, group.ActiveEndpointAggregates.Count);
    }

    [Fact]
    public void Uses_rule_name_for_display_name()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var flows = new[]
        {
            Flow(@"C:\Users\X\AppData\Telegram Desktop\Telegram.exe", "1.1.1.1", FlowRoute.Vpn, FlowLifecycle.Connected, now, ruleName: "Telegram"),
        };

        ConnectionsBoardProjection board = ConnectionUxProjection.Build(flows, true, ConnectionRouteFilterKind.All, null, now);
        Assert.Equal("Telegram", board.ActiveGroups[0].ApplicationName);
    }
}
