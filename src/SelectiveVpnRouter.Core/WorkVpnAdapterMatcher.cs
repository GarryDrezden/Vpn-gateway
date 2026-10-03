namespace SelectiveVpnRouter.Core;

public sealed record WorkVpnAdapterSnapshot(
    string AdapterId,
    int? Ipv4Index,
    IReadOnlyList<string> Ipv4Addresses,
    bool Newborn,
    bool ChangedSinceBefore);

public static class WorkVpnAdapterMatcher
{
    public static string? TrySelectAdapterId(
        IReadOnlyList<WorkVpnAdapterSnapshot> beforeConnect,
        IReadOnlyList<WorkVpnAdapterSnapshot> afterConnect,
        string? tunnelLocalIpv4,
        string? excludeAdapterId,
        int? excludeInterfaceIndex)
    {
        if (string.IsNullOrWhiteSpace(tunnelLocalIpv4))
        {
            return null;
        }

        var beforeById = beforeConnect.ToDictionary(a => a.AdapterId, StringComparer.OrdinalIgnoreCase);
        WorkVpnAdapterSnapshot? best = null;
        int bestScore = int.MinValue;

        foreach (WorkVpnAdapterSnapshot after in afterConnect)
        {
            if (excludeInterfaceIndex is int exIf && after.Ipv4Index == exIf)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(excludeAdapterId)
                && string.Equals(after.AdapterId, excludeAdapterId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!after.Ipv4Addresses.Any(a =>
                    string.Equals(a, tunnelLocalIpv4, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            beforeById.TryGetValue(after.AdapterId, out WorkVpnAdapterSnapshot? before);
            bool unchangedExternal = before is not null
                && !after.Newborn
                && !after.ChangedSinceBefore
                && SequenceEqual(before.Ipv4Addresses, after.Ipv4Addresses);
            if (unchangedExternal)
            {
                continue;
            }

            int score = 0;
            if (after.Newborn)
            {
                score += 100;
            }

            if (after.ChangedSinceBefore)
            {
                score += 50;
            }

            if (before is null)
            {
                score += 10;
            }

            if (best is null || score > bestScore)
            {
                best = after;
                bestScore = score;
            }
        }

        return best?.AdapterId;
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
