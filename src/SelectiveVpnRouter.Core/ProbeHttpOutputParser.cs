using System.Net;
using System.Net.Sockets;

namespace SelectiveVpnRouter.Core;

/// <summary>
/// Parses SelectiveVpnRouter.Probe --http console output for diagnostics.
/// </summary>
public static class ProbeHttpOutputParser
{
    public static string? TryParsePublicIpv4(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        foreach (string rawLine in output.Split('\n', '\r'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("public IP ", StringComparison.OrdinalIgnoreCase))
            {
                string rest = line["public IP ".Length..].Trim();
                if (TryParseStandaloneIpv4Token(rest, out string? legacyIp))
                {
                    return legacyIp;
                }

                continue;
            }

            if (TryParseStandaloneIpv4Line(line, out string? ip))
            {
                return ip;
            }
        }

        return null;
    }

    private static bool TryParseStandaloneIpv4Line(string line, out string? ip)
    {
        ip = null;
        if (line.StartsWith("http OK", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (line.Contains(':'))
        {
            return false;
        }

        return TryParseStandaloneIpv4Token(line, out ip);
    }

    private static bool TryParseStandaloneIpv4Token(string token, out string? ip)
    {
        ip = null;
        if (!IPAddress.TryParse(token, out IPAddress? parsed)
            || parsed.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        ip = parsed.ToString();
        return ip is not null;
    }
}