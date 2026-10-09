using System.Net;
using SelectiveVpnRouter.Core;

namespace SelectiveVpnRouter.Network;

/// <summary>Mutable per-session DNS snapshot; cleared on disconnect and replaced on each PUSH_REPLY.</summary>
public sealed class VpnSessionDnsStore : IVpnSessionDnsServers
{
    private readonly object _gate = new();
    private IReadOnlyList<IPAddress> _ipv4 = [];

    public IReadOnlyList<IPAddress> GetIpv4DnsServers()
    {
        lock (_gate)
        {
            return _ipv4;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _ipv4 = [];
        }
    }

    internal void ApplyFromOpenVpnLogLine(string? logLine)
    {
        if (string.IsNullOrWhiteSpace(logLine)
            || !logLine.Contains(OpenVpnPushReplyDnsParser.PushReplyToken, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        IReadOnlyList<IPAddress> parsed = OpenVpnPushReplyDnsParser.ParseIpv4DnsServersFromLogLine(logLine);
        lock (_gate)
        {
            _ipv4 = parsed.Count == 0 ? [] : parsed.ToArray();
        }
    }
}
