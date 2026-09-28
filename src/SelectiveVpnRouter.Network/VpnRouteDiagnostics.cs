using System.Net;
using SelectiveVpnRouter.Core;

namespace SelectiveVpnRouter.Network;

public static class VpnRouteDiagnostics
{
    public static string Summarize(IPAddress target, int? vpnIfIndex, string? vpnGateway, bool ownedDefaultPresent)
    {
        IReadOnlyList<RouteRow> rows = RouteTable.IPv4();
        RouteRow? owned = rows.FirstOrDefault(r =>
            ownedDefaultPresent
            && vpnIfIndex is int idx
            && r.InterfaceIndex == idx
            && r.Destination.Equals(IPAddress.Any)
            && r.Mask.Equals(IPAddress.Any));
        RouteRow? hostRoute = rows
            .Where(r => vpnIfIndex is int idx && r.InterfaceIndex == idx && RouteTable.Contains(target, r))
            .OrderBy(r => r.Metric)
            .FirstOrDefault();
        RouteRow? best = rows
            .Where(r => RouteTable.Contains(target, r))
            .OrderBy(r => r.Metric)
            .FirstOrDefault();

        return
            $"target={target} vpnIf={vpnIfIndex?.ToString() ?? "—"} vpnGateway={vpnGateway ?? "—"} " +
            $"ownedDefaultPresent={ownedDefaultPresent} ownedDefaultMetric={owned?.Metric.ToString() ?? "—"} " +
            $"vpnHostRoute={FormatRoute(hostRoute)} bestRoute={FormatRoute(best)}";
    }

    private static string FormatRoute(RouteRow? row) =>
        row is null
            ? "—"
            : $"if={row.InterfaceIndex} gw={row.NextHop} metric={row.Metric}";
}