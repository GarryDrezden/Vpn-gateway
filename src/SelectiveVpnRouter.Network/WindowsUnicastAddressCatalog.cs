using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using SelectiveVpnRouter.Core;

namespace SelectiveVpnRouter.Network;

internal static class WindowsUnicastAddressCatalog
{
    public static IReadOnlyDictionary<int, IReadOnlyList<Ipv4TunnelAddress>> ReadByInterfaceIndex()
    {
        var map = new Dictionary<int, List<Ipv4TunnelAddress>>();
        foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            TryAddInterface(map, nic);
        }

        return map.ToDictionary(k => k.Key, v => (IReadOnlyList<Ipv4TunnelAddress>)v.Value);
    }

    internal static IReadOnlyList<Ipv4TunnelAddress> ReadManagedUnicast(IPInterfaceProperties props)
    {
        var list = new List<Ipv4TunnelAddress>();
        foreach (UnicastIPAddressInformation addr in props.UnicastAddresses)
        {
            if (addr.Address.AddressFamily != AddressFamily.InterNetwork)
            {
                continue;
            }

            list.Add(new Ipv4TunnelAddress(
                addr.Address.ToString(),
                ReadPrefixLength(addr),
                MapDadState(addr)));
        }

        return list;
    }

    internal static Ipv4DadState MapDadState(UnicastIPAddressInformation addr)
    {
        try
        {
            return MapDuplicateAddressDetectionState(addr.DuplicateAddressDetectionState);
        }
        catch (NetworkInformationException)
        {
            return Ipv4DadState.Unknown;
        }
    }

    internal static Ipv4DadState MapDuplicateAddressDetectionState(DuplicateAddressDetectionState state) =>
        state switch
        {
            DuplicateAddressDetectionState.Preferred => Ipv4DadState.Preferred,
            DuplicateAddressDetectionState.Deprecated => Ipv4DadState.Deprecated,
            DuplicateAddressDetectionState.Tentative => Ipv4DadState.Tentative,
            DuplicateAddressDetectionState.Duplicate => Ipv4DadState.Duplicate,
            DuplicateAddressDetectionState.Invalid => Ipv4DadState.Invalid,
            _ => Ipv4DadState.Unknown,
        };

    private static void TryAddInterface(Dictionary<int, List<Ipv4TunnelAddress>> map, NetworkInterface nic)
    {
        try
        {
            IPInterfaceProperties props = nic.GetIPProperties();
            int ifIndex = props.GetIPv4Properties().Index;
            IReadOnlyList<Ipv4TunnelAddress> addresses = ReadManagedUnicast(props);
            if (addresses.Count == 0)
            {
                return;
            }

            map[ifIndex] = addresses.ToList();
        }
        catch (NetworkInformationException)
        {
        }
        catch (SocketException)
        {
        }
    }

    private static int ReadPrefixLength(UnicastIPAddressInformation addr)
    {
        try
        {
            int prefix = addr.PrefixLength;
            return prefix > 0 ? prefix : 32;
        }
        catch (NetworkInformationException)
        {
        }

        if (addr.IPv4Mask is IPAddress mask)
        {
            return CountPrefixBits(mask);
        }

        return 32;
    }

    private static int CountPrefixBits(IPAddress mask)
    {
        byte[] bytes = mask.GetAddressBytes();
        int bits = 0;
        foreach (byte b in bytes)
        {
            for (int i = 7; i >= 0; i--)
            {
                if ((b & (1 << i)) != 0)
                {
                    bits++;
                }
                else if (bits > 0)
                {
                    return bits;
                }
            }
        }

        return bits > 0 ? bits : 32;
    }
}
