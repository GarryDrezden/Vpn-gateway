using System.Net;
using System.Net.Sockets;

namespace SelectiveVpnRouter.Core;

public static class DomainWildcard
{
    public static bool Matches(string pattern, string host)
    {
        string p = NormalizeHost(pattern);
        string h = NormalizeHost(host);
        if (p.Length == 0 || h.Length == 0)
        {
            return false;
        }

        if (p.StartsWith("*.", StringComparison.Ordinal))
        {
            string suffix = p[1..]; // ".example.com"
            string apex = p[2..];
            return h.Equals(apex, StringComparison.Ordinal)
                || h.EndsWith(suffix, StringComparison.Ordinal);
        }

        return h.Equals(p, StringComparison.Ordinal);
    }

    public static string NormalizeHost(string value)
    {
        string v = value.Trim().ToLowerInvariant();
        if (v.StartsWith("http://", StringComparison.Ordinal) || v.StartsWith("https://", StringComparison.Ordinal))
        {
            if (Uri.TryCreate(v, UriKind.Absolute, out Uri? uri))
            {
                v = uri.Host;
            }
        }

        return v.Trim().TrimEnd('.').TrimStart('.');
    }
}

public static class CidrMatcher
{
    public static bool TryParse(string text, out IPNetwork network)
    {
        network = default;
        string t = text.Trim();
        if (IPNetwork.TryParse(t, out network))
        {
            return true;
        }

        if (IPAddress.TryParse(t, out IPAddress? ip))
        {
            int prefix = ip.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
            network = new IPNetwork(ip, prefix);
            return true;
        }

        return false;
    }

    public static bool Contains(string cidrOrIp, IPAddress address)
        => TryParse(cidrOrIp, out IPNetwork network) && network.Contains(address);
}
