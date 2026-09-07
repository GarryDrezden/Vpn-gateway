using System.Net.NetworkInformation;

namespace SelectiveVpn.V0.Network;

internal sealed class NetworkSnapshot
{
    public required DateTime TakenAtUtc { get; init; }
    public required IReadOnlyList<DefaultRouteEntry> IPv4DefaultRoutes { get; init; }
    public required IReadOnlyList<AdapterInfo> Adapters { get; init; }
    public required IReadOnlyList<DnsBinding> Dns { get; init; }
}

internal sealed class DefaultRouteEntry : IEquatable<DefaultRouteEntry>
{
    public required int InterfaceIndex { get; init; }
    public required string InterfaceName { get; init; }
    public required string NextHop { get; init; }
    public required uint RouteMetric { get; init; }

    public bool Equals(DefaultRouteEntry? other)
    {
        if (other is null)
        {
            return false;
        }

        return InterfaceIndex == other.InterfaceIndex
            && string.Equals(NextHop, other.NextHop, StringComparison.OrdinalIgnoreCase)
            && RouteMetric == other.RouteMetric;
    }

    public override bool Equals(object? obj) => Equals(obj as DefaultRouteEntry);

    public override int GetHashCode() => HashCode.Combine(InterfaceIndex, NextHop.ToUpperInvariant(), RouteMetric);

    public override string ToString()
        => $"{InterfaceName} (idx {InterfaceIndex}) → {NextHop}, metric {RouteMetric}";
}

internal sealed class AdapterInfo
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required OperationalStatus Status { get; init; }
    public int? Ipv4Index { get; init; }
    public int? Ipv6Index { get; init; }
    public required IReadOnlyList<string> UnicastIpv4 { get; init; }
    public required IReadOnlyList<string> UnicastIpv6 { get; init; }

    public IEnumerable<string> AllUnicast => UnicastIpv4.Concat(UnicastIpv6);
}

internal sealed class DnsBinding
{
    public required string AdapterId { get; init; }
    public required string AdapterName { get; init; }
    public required IReadOnlyList<string> Servers { get; init; }
}

internal sealed class Ipv4RouteEntry
{
    public required System.Net.IPAddress Destination { get; init; }
    public required System.Net.IPAddress Mask { get; init; }
    public required System.Net.IPAddress NextHop { get; init; }
    public required int InterfaceIndex { get; init; }
    public required string InterfaceName { get; init; }
    public required uint RouteMetric { get; init; }
    public required uint ForwardProto { get; init; }

    public string Prefix => $"{Destination}/{PrefixLength(Mask)}";

    public bool IsDefault => Destination.Equals(System.Net.IPAddress.Any) && Mask.Equals(System.Net.IPAddress.Any);

    public bool IsSplitDefaultHalf
    {
        get
        {
            var slashOne = System.Net.IPAddress.Parse("128.0.0.0");
            return Mask.Equals(slashOne)
                && (Destination.Equals(System.Net.IPAddress.Any) || Destination.Equals(slashOne));
        }
    }

    public bool MatchesHost(System.Net.IPAddress host)
        => Destination.Equals(host) && Mask.Equals(System.Net.IPAddress.Broadcast);

    public override string ToString()
        => $"{Prefix} via {NextHop} if {InterfaceName} (idx {InterfaceIndex}) metric {RouteMetric}";

    private static int PrefixLength(System.Net.IPAddress mask)
    {
        byte[] bytes = mask.GetAddressBytes();
        int bits = 0;
        foreach (byte b in bytes)
        {
            bits += System.Numerics.BitOperations.PopCount(b);
        }

        return bits;
    }
}

internal sealed class NetworkDiff
{
    public required bool DefaultRouteUnchanged { get; init; }
    public required bool DnsUnchanged { get; init; }
    public required IReadOnlyList<string> DefaultRouteNotes { get; init; }
    public required IReadOnlyList<string> DnsNotes { get; init; }
}

internal static class NetworkDiffs
{
    public static NetworkDiff Compare(NetworkSnapshot before, NetworkSnapshot after)
    {
        var routeNotes = new List<string>();
        var beforeRoutes = NormalizeRoutes(before.IPv4DefaultRoutes);
        var afterRoutes = NormalizeRoutes(after.IPv4DefaultRoutes);

        if (beforeRoutes.Count != afterRoutes.Count)
        {
            routeNotes.Add($"Default route count {beforeRoutes.Count} → {afterRoutes.Count}.");
        }

        foreach (DefaultRouteEntry lost in beforeRoutes.Where(b => afterRoutes.All(a => !a.Equals(b))))
        {
            routeNotes.Add($"Lost: {lost}");
        }

        foreach (DefaultRouteEntry added in afterRoutes.Where(a => beforeRoutes.All(b => !b.Equals(a))))
        {
            routeNotes.Add($"Added: {added}");
        }

        var dnsNotes = new List<string>();
        Dictionary<string, DnsBinding> beforeDns = before.Dns.ToDictionary(d => d.AdapterId, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, AdapterInfo> beforeAdapters = before.Adapters.ToDictionary(a => a.Id, StringComparer.OrdinalIgnoreCase);

        foreach (DnsBinding afterDns in after.Dns)
        {
            if (!beforeDns.TryGetValue(afterDns.AdapterId, out DnsBinding? previous))
            {
                // DNS on a brand-new adapter (the tunnel) is not "system DNS replaced".
                continue;
            }

            if (!beforeAdapters.TryGetValue(afterDns.AdapterId, out AdapterInfo? nic) || nic.Status != OperationalStatus.Up)
            {
                continue;
            }

            string beforeJoined = string.Join(", ", previous.Servers);
            string afterJoined = string.Join(", ", afterDns.Servers);
            if (!previous.Servers.SequenceEqual(afterDns.Servers, StringComparer.OrdinalIgnoreCase))
            {
                dnsNotes.Add($"{afterDns.AdapterName}: [{beforeJoined}] → [{afterJoined}]");
            }
        }

        foreach (DnsBinding previous in before.Dns)
        {
            if (!after.Dns.Any(d => string.Equals(d.AdapterId, previous.AdapterId, StringComparison.OrdinalIgnoreCase))
                && previous.Servers.Count > 0)
            {
                if (beforeAdapters.TryGetValue(previous.AdapterId, out AdapterInfo? nic)
                    && nic.Status == OperationalStatus.Up)
                {
                    dnsNotes.Add($"{previous.AdapterName}: DNS binding disappeared.");
                }
            }
        }

        return new NetworkDiff
        {
            DefaultRouteUnchanged = routeNotes.Count == 0,
            DnsUnchanged = dnsNotes.Count == 0,
            DefaultRouteNotes = routeNotes,
            DnsNotes = dnsNotes,
        };
    }

    private static List<DefaultRouteEntry> NormalizeRoutes(IReadOnlyList<DefaultRouteEntry> routes)
        => routes
            .OrderBy(r => r.InterfaceIndex)
            .ThenBy(r => r.NextHop, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.RouteMetric)
            .ToList();
}
