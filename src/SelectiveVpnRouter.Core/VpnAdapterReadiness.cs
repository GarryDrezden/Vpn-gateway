using System.Net;
using System.Net.NetworkInformation;

namespace SelectiveVpnRouter.Core;

public enum Ipv4DadState
{
    Unknown = 0,
    Invalid = 1,
    Tentative = 2,
    Duplicate = 3,
    Deprecated = 4,
    Preferred = 5,
}

public sealed record Ipv4TunnelAddress(string Address, int PrefixLength, Ipv4DadState DadState);

public sealed record VpnAdapterCandidateDiagnostic(
    int? IfIndex,
    string Name,
    string Status,
    IReadOnlyList<string> Ipv4Summary,
    string Reason);

public sealed record VpnAdapterSelection
{
    public required string AdapterId { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required int IfIndex { get; init; }
    public required Ipv4TunnelAddress SelectedAddress { get; init; }
    public required string Gateway { get; init; }
    public required string SelectionReason { get; init; }

    public string FormatLogLine() =>
        "VPN adapter selected: if=" + IfIndex
        + " name=" + Name
        + " ipv4=" + SelectedAddress.Address + "/" + SelectedAddress.PrefixLength
        + " state=" + SelectedAddress.DadState
        + " gateway=" + Gateway
        + " reason=" + SelectionReason;
}

public static class VpnTunnelIpv4Rules
{
    public static bool IsApipa(IPAddress address)
    {
        byte[] bytes = address.GetAddressBytes();
        return bytes.Length == 4 && bytes[0] == 169 && bytes[1] == 254;
    }

    public static bool IsBlockedAddress(IPAddress address)
    {
        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return true;
        }

        byte[] bytes = address.GetAddressBytes();
        if (bytes.Length != 4)
        {
            return true;
        }

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any))
        {
            return true;
        }

        return IsApipa(address);
    }

    public static bool IsUsableDadState(Ipv4DadState state) =>
        state is Ipv4DadState.Preferred or Ipv4DadState.Deprecated or Ipv4DadState.Unknown;

    public static bool IsUsableTunnelAddress(Ipv4TunnelAddress address)
    {
        if (!IPAddress.TryParse(address.Address, out IPAddress? ip))
        {
            return false;
        }

        if (IsBlockedAddress(ip))
        {
            return false;
        }

        return IsUsableDadState(address.DadState);
    }

    public static int PrefixLengthFromMask(string maskOrHost)
    {
        if (!IPAddress.TryParse(maskOrHost, out IPAddress? mask))
        {
            return 32;
        }

        byte[] bytes = mask.GetAddressBytes();
        if (bytes.Length != 4)
        {
            return 32;
        }

        if (bytes[0] == 255 && bytes[1] == 255 && bytes[2] == 255 && bytes[3] == 255)
        {
            return 32;
        }

        uint value = BitConverter.ToUInt32(bytes, 0);
        int bits = 0;
        for (int i = 31; i >= 0; i--)
        {
            if ((value & (1u << i)) != 0)
            {
                bits++;
            }
            else if (bits > 0)
            {
                break;
            }
        }

        return bits > 0 ? bits : 32;
    }

    public static bool IsGatewayCompatible(Ipv4TunnelAddress tunnel, string gateway)
    {
        if (!IPAddress.TryParse(tunnel.Address, out IPAddress? local)
            || !IPAddress.TryParse(gateway, out IPAddress? gw))
        {
            return false;
        }

        uint mask = PrefixToMask(tunnel.PrefixLength);
        return (ToUInt(local) & mask) == (ToUInt(gw) & mask);
    }

    public static bool HasOnLinkGatewayRoute(int ifIndex, string gateway, IEnumerable<(IPAddress Destination, IPAddress Mask, IPAddress NextHop, int InterfaceIndex)> routes)
    {
        if (!IPAddress.TryParse(gateway, out IPAddress? gw))
        {
            return false;
        }

        foreach (var route in routes)
        {
            if (route.InterfaceIndex != ifIndex)
            {
                continue;
            }

            if (Contains(route.Destination, route.Mask, gw))
            {
                return route.NextHop.Equals(IPAddress.Any) || route.NextHop.Equals(gw);
            }
        }

        return false;
    }

    private static bool Contains(IPAddress network, IPAddress mask, IPAddress target)
    {
        return (ToUInt(network) & ToUInt(mask)) == (ToUInt(target) & ToUInt(mask));
    }

    private static uint PrefixToMask(int prefixLength)
    {
        if (prefixLength <= 0)
        {
            return 0;
        }

        if (prefixLength >= 32)
        {
            return uint.MaxValue;
        }

        return uint.MaxValue << (32 - prefixLength);
    }

    private static uint ToUInt(IPAddress address)
    {
        byte[] bytes = address.GetAddressBytes();
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
    }
}

public sealed record VpnAdapterReadinessCandidate(
    string AdapterId,
    string Name,
    string Description,
    OperationalStatus Status,
    int? Ipv4Index,
    IReadOnlyList<Ipv4TunnelAddress> Ipv4Addresses,
    bool NewSinceBefore,
    bool ChangedSinceBefore,
    bool LooksVpn);

public sealed record VpnAdapterReadinessEvaluation(
    VpnAdapterReadinessCandidate Candidate,
    Ipv4TunnelAddress SelectedAddress,
    int Score,
    IReadOnlyList<string> Reasons);

public static class VpnAdapterReadiness
{
    public static string NotReadyHeadline(int readinessTimeoutSeconds) =>
        "VPN tunnel adapter is not ready after " + readinessTimeoutSeconds + "s.";

    public const string NotReadyDetail =
        "OpenVPN connected, but no usable tunnel IPv4 became available.";

    public static IReadOnlyList<VpnAdapterCandidateDiagnostic> DescribeCandidates(IEnumerable<VpnAdapterReadinessCandidate> candidates)
    {
        return candidates.Select(c =>
        {
            string summary = c.Ipv4Addresses.Count == 0
                ? "(none)"
                : string.Join(", ", c.Ipv4Addresses.Select(a => a.Address + " " + a.DadState));
            string reason = ExplainNotReady(c);
            return new VpnAdapterCandidateDiagnostic(c.Ipv4Index, c.Name, c.Status.ToString(), [summary], reason);
        }).ToArray();
    }

    public static string FormatNotReadyDiagnostics(
        IEnumerable<VpnAdapterReadinessCandidate> candidates,
        string? routeGateway,
        string? tunnelLocalIpv4,
        int readinessTimeoutSeconds = VpnConnectBudget.VpnAdapterReadinessMs / 1000)
    {
        var lines = new List<string> { NotReadyHeadline(readinessTimeoutSeconds), NotReadyDetail };
        if (!string.IsNullOrWhiteSpace(routeGateway))
        {
            lines.Add("routeGateway=" + routeGateway);
        }

        if (!string.IsNullOrWhiteSpace(tunnelLocalIpv4))
        {
            lines.Add("openVpnTunnelLocal=" + tunnelLocalIpv4);
        }

        foreach (VpnAdapterCandidateDiagnostic diag in DescribeCandidates(candidates.Where(c => c.LooksVpn || c.NewSinceBefore || c.ChangedSinceBefore)))
        {
            lines.Add("candidate if=" + (diag.IfIndex?.ToString() ?? "?") + " name=" + diag.Name + " ipv4=[" + string.Join(", ", diag.Ipv4Summary) + "] reason=" + diag.Reason);
        }

        return string.Join(Environment.NewLine, lines);
    }

    public static bool TrySelectBest(
        IEnumerable<VpnAdapterReadinessCandidate> candidates,
        string? openVpnRouteGateway,
        string? openVpnTunnelLocalIpv4,
        IEnumerable<(IPAddress Destination, IPAddress Mask, IPAddress NextHop, int InterfaceIndex)> routes,
        out VpnAdapterSelection? selection,
        out IReadOnlyList<VpnAdapterReadinessEvaluation> ranked)
    {
        var evaluations = new List<VpnAdapterReadinessEvaluation>();
        foreach (VpnAdapterReadinessCandidate candidate in candidates)
        {
            if (candidate.Status != OperationalStatus.Up || candidate.Ipv4Index is not int ifIndex)
            {
                continue;
            }

            foreach (Ipv4TunnelAddress address in candidate.Ipv4Addresses.Where(VpnTunnelIpv4Rules.IsUsableTunnelAddress))
            {
                int score = 0;
                var reasons = new List<string>();
                if (!string.IsNullOrWhiteSpace(openVpnTunnelLocalIpv4)
                    && string.Equals(address.Address, openVpnTunnelLocalIpv4, StringComparison.OrdinalIgnoreCase))
                {
                    score += 1000;
                    reasons.Add("matched OpenVPN tunnel address");
                }

                string gateway = openVpnRouteGateway ?? GuessGateway(address);
                if (!string.IsNullOrWhiteSpace(openVpnRouteGateway))
                {
                    if (VpnTunnelIpv4Rules.IsGatewayCompatible(address, openVpnRouteGateway))
                    {
                        score += 500;
                        reasons.Add("route-gateway on-link with tunnel IPv4");
                    }
                    else
                    {
                        continue;
                    }
                }

                if (VpnTunnelIpv4Rules.HasOnLinkGatewayRoute(ifIndex, gateway, routes))
                {
                    score += 200;
                    reasons.Add("on-link route for gateway");
                }

                if (candidate.NewSinceBefore)
                {
                    score += 80;
                    reasons.Add("new adapter since connect");
                }
                else if (candidate.ChangedSinceBefore)
                {
                    score += 60;
                    reasons.Add("IPv4 changed since connect");
                }

                if (candidate.LooksVpn)
                {
                    score += 10;
                    reasons.Add("vpn-like adapter name");
                }

                if (string.IsNullOrWhiteSpace(openVpnRouteGateway)
                    && string.IsNullOrWhiteSpace(openVpnTunnelLocalIpv4)
                    && !candidate.NewSinceBefore
                    && !candidate.ChangedSinceBefore)
                {
                    continue;
                }

                evaluations.Add(new VpnAdapterReadinessEvaluation(candidate, address, score, reasons));
            }
        }

        ranked = evaluations
            .OrderByDescending(e => e.Score)
            .ThenByDescending(e => e.SelectedAddress.DadState)
            .ToArray();

        VpnAdapterReadinessEvaluation? best = ranked.FirstOrDefault();
        if (best is null)
        {
            selection = null;
            return false;
        }

        string selectedGateway = openVpnRouteGateway ?? GuessGateway(best.SelectedAddress);
        selection = new VpnAdapterSelection
        {
            AdapterId = best.Candidate.AdapterId,
            Name = best.Candidate.Name,
            Description = best.Candidate.Description,
            IfIndex = best.Candidate.Ipv4Index!.Value,
            SelectedAddress = best.SelectedAddress,
            Gateway = selectedGateway,
            SelectionReason = string.Join("; ", best.Reasons),
        };
        return true;
    }

    public static void ValidateOwnedRouteOrThrow(VpnAdapterSelection selection)
    {
        if (!VpnTunnelIpv4Rules.IsUsableTunnelAddress(selection.SelectedAddress))
        {
            throw new InvalidOperationException("Refusing owned route: selected interface has no usable tunnel IPv4.");
        }

        if (!VpnTunnelIpv4Rules.IsGatewayCompatible(selection.SelectedAddress, selection.Gateway))
        {
            throw new InvalidOperationException(
                "Refusing owned route: gateway " + selection.Gateway
                + " is not on-link with selected tunnel IPv4 "
                + selection.SelectedAddress.Address + "/" + selection.SelectedAddress.PrefixLength + ".");
        }

        if (IPAddress.TryParse(selection.SelectedAddress.Address, out IPAddress? selectedIp)
            && VpnTunnelIpv4Rules.IsApipa(selectedIp))
        {
            throw new InvalidOperationException("Refusing owned route: selected interface has only APIPA addresses.");
        }
    }

    public static bool TryValidateOwnedRoute(VpnAdapterSelection selection, out string? error)
    {
        try
        {
            ValidateOwnedRouteOrThrow(selection);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string GuessGateway(Ipv4TunnelAddress address)
    {
        if (!IPAddress.TryParse(address.Address, out IPAddress? ip))
        {
            return address.Address;
        }

        byte[] bytes = ip.GetAddressBytes();
        if (bytes.Length == 4 && bytes[3] != 1)
        {
            bytes[3] = 1;
            return new IPAddress(bytes).ToString();
        }

        return address.Address;
    }

    private static string ExplainNotReady(VpnAdapterReadinessCandidate candidate)
    {
        if (candidate.Status != OperationalStatus.Up)
        {
            return "adapter not Up";
        }

        if (candidate.Ipv4Addresses.Count == 0)
        {
            return "no IPv4";
        }

        if (candidate.Ipv4Addresses.All(a => IPAddress.TryParse(a.Address, out IPAddress? ip) && VpnTunnelIpv4Rules.IsApipa(ip)))
        {
            return "only APIPA";
        }

        if (candidate.Ipv4Addresses.All(a => a.DadState == Ipv4DadState.Tentative))
        {
            return "only Tentative";
        }

        if (!candidate.Ipv4Addresses.Any(VpnTunnelIpv4Rules.IsUsableTunnelAddress))
        {
            return "no usable tunnel IPv4";
        }

        return "not selected";
    }
}