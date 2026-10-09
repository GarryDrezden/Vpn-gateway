using System.Net;
using System.Net.Sockets;

namespace SelectiveVpnRouter.Core;

/// <summary>Parses IPv4 DNS servers from OpenVPN PUSH_REPLY negotiation log lines.</summary>
public static class OpenVpnPushReplyDnsParser
{
    public const string PushReplyToken = "PUSH_REPLY";
    private const string DhcpOptionDnsPrefix = "dhcp-option DNS ";

    public static IReadOnlyList<IPAddress> ParseIpv4DnsServersFromLogLine(string? logLine)
    {
        if (string.IsNullOrWhiteSpace(logLine)
            || !logLine.Contains(PushReplyToken, StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        string payload = ExtractPushPayload(logLine);
        if (payload.Length == 0)
        {
            return [];
        }

        return ParsePushPayloadIpv4Dns(payload);
    }

    internal static IReadOnlyList<IPAddress> ParsePushPayloadIpv4Dns(string pushPayload)
    {
        var ordered = new List<IPAddress>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (string segment in pushPayload.Split(','))
        {
            string token = segment.Trim();
            if (!token.StartsWith(DhcpOptionDnsPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string ipText = token[DhcpOptionDnsPrefix.Length..].Trim();
            if (!IPAddress.TryParse(ipText, out IPAddress? address)
                || address.AddressFamily != AddressFamily.InterNetwork)
            {
                continue;
            }

            string key = address.ToString();
            if (seen.Add(key))
            {
                ordered.Add(address);
            }
        }

        return ordered;
    }

    private static string ExtractPushPayload(string logLine)
    {
        int pushIdx = logLine.IndexOf(PushReplyToken, StringComparison.OrdinalIgnoreCase);
        if (pushIdx < 0)
        {
            return string.Empty;
        }

        string afterPush = logLine[(pushIdx + PushReplyToken.Length)..].TrimStart();
        if (afterPush.StartsWith(','))
        {
            afterPush = afterPush[1..];
        }

        int quoteStart = afterPush.IndexOf('\'');
        if (quoteStart >= 0)
        {
            int quoteEnd = afterPush.IndexOf('\'', quoteStart + 1);
            if (quoteEnd > quoteStart)
            {
                return afterPush[(quoteStart + 1)..quoteEnd];
            }
        }

        return afterPush.Trim().Trim('\'', '"');
    }
}
