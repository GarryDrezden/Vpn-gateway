using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace SelectiveVpnRouter.Network;

/// <summary>
/// IPv4 DNS servers configured on a specific network interface index (VPN adapter).
/// </summary>
public interface IVpnAdapterDnsServers
{
    IReadOnlyList<IPAddress> GetIpv4DnsServers(int interfaceIndex);
}

public sealed class NetworkInterfaceVpnAdapterDnsServers : IVpnAdapterDnsServers
{
    public IReadOnlyList<IPAddress> GetIpv4DnsServers(int interfaceIndex) =>
        VpnAdapterDnsServersLookup.GetIpv4DnsServers(interfaceIndex);
}

internal static class VpnAdapterDnsServersLookup
{
    internal static IReadOnlyList<IPAddress> GetIpv4DnsServers(int interfaceIndex)
    {
        var list = new List<IPAddress>();
        foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up)
                continue;

            try
            {
                IPInterfaceProperties props = nic.GetIPProperties();
                if (props.GetIPv4Properties().Index != interfaceIndex)
                    continue;

                foreach (IPAddress dns in props.DnsAddresses)
                {
                    if (dns.AddressFamily == AddressFamily.InterNetwork)
                        list.Add(dns);
                }

                return list;
            }
            catch (NetworkInformationException)
            {
            }
        }

        return list;
    }
}
