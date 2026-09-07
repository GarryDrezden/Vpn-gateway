namespace SelectiveVpnRouter.Core;

/// <summary>
/// Desired owned routes vs currently recorded owned routes.
/// Only routes listed here may be created or deleted by the app.
/// </summary>
public static class RouteReconciler
{
    public static RoutePlan Plan(IReadOnlyList<OwnedRoute> desired, IReadOnlyList<OwnedRoute> actual)
    {
        var add = new List<OwnedRoute>();
        var remove = new List<OwnedRoute>();

        foreach (OwnedRoute want in desired)
        {
            if (!actual.Any(a => Same(a, want)))
            {
                add.Add(want);
            }
        }

        foreach (OwnedRoute have in actual)
        {
            if (!desired.Any(d => Same(d, have)))
            {
                remove.Add(have);
            }
        }

        return new RoutePlan(add, remove);
    }

    public static bool Same(OwnedRoute a, OwnedRoute b)
        => string.Equals(a.DestinationPrefix, b.DestinationPrefix, StringComparison.OrdinalIgnoreCase)
            && a.InterfaceIndex == b.InterfaceIndex
            && string.Equals(a.NextHop, b.NextHop, StringComparison.OrdinalIgnoreCase);

    public static OwnedRoute TransportDefault(int vpnIfIndex, string vpnGateway, uint metric = 9000)
        => new()
        {
            Id = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001"),
            DestinationPrefix = "0.0.0.0/0",
            InterfaceIndex = vpnIfIndex,
            NextHop = vpnGateway,
            Metric = metric,
            Reason = "vpn-transport-high-metric",
        };

    public static OwnedRoute HostRoute(string ipv4, int vpnIfIndex, string vpnGateway, string reason)
        => new()
        {
            Id = Guid.NewGuid(),
            DestinationPrefix = IPPrefix(ipv4),
            InterfaceIndex = vpnIfIndex,
            NextHop = vpnGateway,
            Metric = 1,
            Reason = reason,
        };

    private static string IPPrefix(string ipv4)
        => ipv4.Contains('/', StringComparison.Ordinal) ? ipv4 : ipv4 + "/32";
}

public sealed record RoutePlan(IReadOnlyList<OwnedRoute> ToAdd, IReadOnlyList<OwnedRoute> ToRemove);
