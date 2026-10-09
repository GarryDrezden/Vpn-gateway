using System.Net;

namespace SelectiveVpnRouter.Network;

/// <summary>IPv4 DNS servers negotiated for the current VPN Route OpenVPN session (from PUSH_REPLY).</summary>
public interface IVpnSessionDnsServers
{
    IReadOnlyList<IPAddress> GetIpv4DnsServers();
}
