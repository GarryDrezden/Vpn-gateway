using System.Net;
using System.Net.Sockets;

namespace SelectiveVpnRouter.Network;

/// <summary>
/// Loopback detection for ALE CONNECT_REDIRECT_V4 remote addresses (SOCKADDR_IN layout).
/// Must stay aligned with <c>SvrIsIpv4LoopbackDestination</c> in the callout driver.
/// </summary>
public static class WfpAleConnectRedirectLoopback
{
    public static bool IsIpv4LoopbackSockAddrIn(uint sinAddrSUnSAddr)
        => (sinAddrSUnSAddr & 0xFF) == 127;

    public static uint ToSinAddrSUnSAddr(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new ArgumentException("IPv4 address required.", nameof(address));
        }

        byte[] bytes = address.GetAddressBytes();
        if (bytes.Length != 4)
        {
            throw new ArgumentException("IPv4 address must be 4 bytes.", nameof(address));
        }

        return (uint)bytes[0]
            | ((uint)bytes[1] << 8)
            | ((uint)bytes[2] << 16)
            | ((uint)bytes[3] << 24);
    }

    public static bool ShouldBypassAleConnectRedirect(IPAddress remoteAddress) =>
        remoteAddress.AddressFamily == AddressFamily.InterNetwork
            ? IsIpv4LoopbackSockAddrIn(ToSinAddrSUnSAddr(remoteAddress))
            : IPAddress.IsLoopback(remoteAddress);
}
