using System.Net;
using System.Net.NetworkInformation;
using SelectiveVpnRouter.Core;

namespace SelectiveVpnRouter.Network;

public static class VpnAdapterSelector
{
    public static async Task<VpnAdapterSelection?> WaitForReadyAsync(
        IReadOnlyList<AdapterView> beforeConnect,
        OpenVpnController openVpn,
        TimeSpan timeout,
        CancellationToken ct)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        IReadOnlyList<VpnAdapterReadinessCandidate> lastCandidates = [];
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            IReadOnlyList<AdapterView> current = AdapterCatalog.All();
            lastCandidates = BuildCandidates(beforeConnect, current);
            if (!string.IsNullOrWhiteSpace(openVpn.TunnelLocalIpv4)
                && !lastCandidates.Any(c => c.Ipv4Addresses.Any(a =>
                    string.Equals(a.Address, openVpn.TunnelLocalIpv4, StringComparison.OrdinalIgnoreCase)
                    && VpnTunnelIpv4Rules.IsUsableTunnelAddress(a))))
            {
                await Task.Delay(200, ct).ConfigureAwait(false);
                continue;
            }

            var routes = RouteTable.IPv4()
                .Select(r => (r.Destination, r.Mask, r.NextHop, r.InterfaceIndex))
                .ToArray();
            if (VpnAdapterReadiness.TrySelectBest(
                lastCandidates,
                openVpn.RouteGateway,
                openVpn.TunnelLocalIpv4,
                routes,
                out VpnAdapterSelection? selection,
                out _))
            {
                VpnAdapterReadiness.ValidateOwnedRouteOrThrow(selection!);
                return selection;
            }

            await Task.Delay(200, ct).ConfigureAwait(false);
        }

        return null;
    }

    public static string FormatNotReadyMessage(
        IReadOnlyList<VpnAdapterReadinessCandidate> candidates,
        OpenVpnController openVpn)
        => VpnAdapterReadiness.FormatNotReadyDiagnostics(candidates, openVpn.RouteGateway, openVpn.TunnelLocalIpv4);

    public static AdapterView? FindAdapter(IReadOnlyList<AdapterView> adapters, VpnAdapterSelection selection)
        => adapters.FirstOrDefault(a => string.Equals(a.Id, selection.AdapterId, StringComparison.OrdinalIgnoreCase))
            ?? adapters.FirstOrDefault(a => a.Ipv4Index == selection.IfIndex);

    public static IReadOnlyList<VpnAdapterReadinessCandidate> BuildCandidates(
        IReadOnlyList<AdapterView> beforeConnect,
        IReadOnlyList<AdapterView> current)
    {
        var list = new List<VpnAdapterReadinessCandidate>();
        foreach (AdapterView nic in current)
        {
            AdapterView? prev = beforeConnect.FirstOrDefault(b => string.Equals(b.Id, nic.Id, StringComparison.OrdinalIgnoreCase));
            bool newborn = prev is null;
            bool cameUp = prev is not null && prev.Status != OperationalStatus.Up && nic.Status == OperationalStatus.Up;
            bool ipChanged = prev is not null && !SequenceEqual(prev.Ipv4TunnelAddresses, nic.Ipv4TunnelAddresses);
            bool looks = AdapterCatalog.LooksVpn(nic.Name) || AdapterCatalog.LooksVpn(nic.Description);
            if (!looks && !newborn && !cameUp && !ipChanged)
            {
                continue;
            }

            list.Add(new VpnAdapterReadinessCandidate(
                nic.Id,
                nic.Name,
                nic.Description,
                nic.Status,
                nic.Ipv4Index,
                nic.Ipv4TunnelAddresses,
                newborn,
                newborn || cameUp || ipChanged,
                looks));
        }

        return list;
    }

    private static bool SequenceEqual(IReadOnlyList<Ipv4TunnelAddress> left, IReadOnlyList<Ipv4TunnelAddress> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (int i = 0; i < left.Count; i++)
        {
            if (!string.Equals(left[i].Address, right[i].Address, StringComparison.OrdinalIgnoreCase)
                || left[i].DadState != right[i].DadState
                || left[i].PrefixLength != right[i].PrefixLength)
            {
                return false;
            }
        }

        return true;
    }
}