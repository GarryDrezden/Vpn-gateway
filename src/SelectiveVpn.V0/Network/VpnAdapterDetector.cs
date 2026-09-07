using System.Net.NetworkInformation;

namespace SelectiveVpn.V0.Network;

internal sealed class VpnAdapterCandidate
{
    public required AdapterInfo Adapter { get; init; }
    public required int Score { get; init; }
    public required IReadOnlyList<string> Reasons { get; init; }
}

internal static class VpnAdapterDetector
{
    public static VpnAdapterCandidate? Detect(
        NetworkSnapshot before,
        NetworkSnapshot after,
        IEnumerable<string> openVpnLogLines,
        out IReadOnlyList<VpnAdapterCandidate> ranked)
    {
        string[] log = openVpnLogLines
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToArray();

        var scored = new List<VpnAdapterCandidate>();
        foreach (AdapterInfo nic in after.Adapters)
        {
            AdapterInfo? previous = before.Adapters.FirstOrDefault(b =>
                string.Equals(b.Id, nic.Id, StringComparison.OrdinalIgnoreCase));

            var reasons = new List<string>();
            int score = 0;

            if (previous is null)
            {
                score += 4;
                reasons.Add("new adapter since BEFORE snapshot");
            }
            else
            {
                if (previous.Status != OperationalStatus.Up && nic.Status == OperationalStatus.Up)
                {
                    score += 3;
                    reasons.Add($"OperationalStatus {previous.Status} → {nic.Status}");
                }

                if (!previous.UnicastIpv4.SequenceEqual(nic.UnicastIpv4) && nic.UnicastIpv4.Count > 0)
                {
                    score += 3;
                    reasons.Add("IPv4 unicast address appeared or changed");
                }

                if (!previous.UnicastIpv6.SequenceEqual(nic.UnicastIpv6)
                    && nic.UnicastIpv6.Any(a => !a.StartsWith("fe80:", StringComparison.OrdinalIgnoreCase)))
                {
                    score += 1;
                    reasons.Add("non-link-local IPv6 appeared or changed");
                }
            }

            if (LooksLikeVpnName(nic.Name) || LooksLikeVpnName(nic.Description))
            {
                score += 2;
                reasons.Add("Name/Description resembles OpenVPN/Wintun/TAP/ovpn-dco");
            }

            if (nic.Status == OperationalStatus.Up && NameMentionedInLog(nic, log))
            {
                score += 2;
                reasons.Add("OpenVPN log mentions this adapter name/description");
            }

            if (score > 0 && nic.Status == OperationalStatus.Up)
            {
                scored.Add(new VpnAdapterCandidate
                {
                    Adapter = nic,
                    Score = score,
                    Reasons = reasons,
                });
            }
        }

        ranked = scored.OrderByDescending(c => c.Score).ThenBy(c => c.Adapter.Name).ToList();
        if (ranked.Count == 0)
        {
            return null;
        }

        VpnAdapterCandidate top = ranked[0];
        if (top.Score < 3)
        {
            return null;
        }

        if (ranked.Count > 1 && ranked[1].Score == top.Score)
        {
            return null;
        }

        return top;
    }

    private static bool LooksLikeVpnName(string text)
    {
        string t = text.ToLowerInvariant();
        return t.Contains("openvpn")
            || t.Contains("wintun")
            || t.Contains("ovpn-dco")
            || t.Contains("ovpn dco")
            || t.Contains("tap-windows")
            || t.Contains("tap-win")
            || t.Contains("tap-windows adapter");
    }

    private static bool NameMentionedInLog(AdapterInfo nic, IReadOnlyList<string> log)
    {
        foreach (string line in log)
        {
            if (nic.Name.Length >= 4 && line.Contains(nic.Name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (nic.Description.Length >= 8 && line.Contains(nic.Description, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
