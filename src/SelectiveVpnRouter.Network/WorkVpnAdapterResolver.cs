using SelectiveVpnRouter.Core;

namespace SelectiveVpnRouter.Network;

public static class WorkVpnAdapterResolver
{
    public static async Task<AdapterView?> WaitForAdapterAsync(
        IReadOnlyList<AdapterView> beforeConnect,
        WorkOpenVpnController workVpn,
        string? excludeAdapterId,
        int? excludeInterfaceIndex,
        TimeSpan timeout,
        CancellationToken ct)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            AdapterView? match = TryResolve(beforeConnect, AdapterCatalog.All(), workVpn, excludeAdapterId, excludeInterfaceIndex);
            if (match is not null)
            {
                return match;
            }

            await Task.Delay(200, ct).ConfigureAwait(false);
        }

        return null;
    }

    public static AdapterView? TryResolve(
        IReadOnlyList<AdapterView> beforeConnect,
        IReadOnlyList<AdapterView> current,
        WorkOpenVpnController workVpn,
        string? excludeAdapterId,
        int? excludeInterfaceIndex)
    {
        if (string.IsNullOrWhiteSpace(workVpn.TunnelLocalIpv4))
        {
            return null;
        }

        IReadOnlyList<WorkVpnAdapterSnapshot> before = BuildSnapshots(beforeConnect, beforeConnect);
        IReadOnlyList<WorkVpnAdapterSnapshot> after = BuildSnapshots(beforeConnect, current);
        string? selectedId = WorkVpnAdapterMatcher.TrySelectAdapterId(
            before,
            after,
            workVpn.TunnelLocalIpv4,
            excludeAdapterId,
            excludeInterfaceIndex);
        if (selectedId is null)
        {
            return null;
        }

        return current.FirstOrDefault(a => string.Equals(a.Id, selectedId, StringComparison.OrdinalIgnoreCase));
    }

    internal static IReadOnlyList<WorkVpnAdapterSnapshot> BuildSnapshots(
        IReadOnlyList<AdapterView> beforeConnect,
        IReadOnlyList<AdapterView> current)
    {
        var list = new List<WorkVpnAdapterSnapshot>();
        foreach (AdapterView nic in current)
        {
            AdapterView? prev = beforeConnect.FirstOrDefault(b => string.Equals(b.Id, nic.Id, StringComparison.OrdinalIgnoreCase));
            bool newborn = prev is null;
            bool ipChanged = prev is not null && !SequenceEqual(prev.Ipv4, nic.Ipv4);
            bool cameUp = prev is not null && prev.Status != System.Net.NetworkInformation.OperationalStatus.Up
                && nic.Status == System.Net.NetworkInformation.OperationalStatus.Up;
            list.Add(new WorkVpnAdapterSnapshot(
                nic.Id,
                nic.Ipv4Index,
                nic.Ipv4,
                newborn,
                newborn || cameUp || ipChanged));
        }

        return list;
    }

    private static bool SequenceEqual(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (int i = 0; i < left.Count; i++)
        {
            if (!string.Equals(left[i], right[i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }
}