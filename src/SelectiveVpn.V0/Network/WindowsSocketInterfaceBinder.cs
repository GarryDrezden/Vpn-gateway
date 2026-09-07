using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace SelectiveVpn.V0.Network;

/// <summary>
/// Isolated Winsock interop for per-socket outgoing interface selection.
///
/// Microsoft Learn (IPPROTO_IP / IP_UNICAST_IF): the SET value is the IPv4
/// interface index as a DWORD in network byte order. GET returns host byte order.
/// Microsoft Learn (IPPROTO_IPV6 / IPV6_UNICAST_IF): both SET and GET use host byte order.
///
/// This option chooses an outgoing interface. It does NOT install a route.
/// If the routing table has no path to the destination via that interface,
/// connect() typically fails with WSAENETUNREACH. That is a valid V0 PARTIAL
/// result — V0 must not "fix" it with 0.0.0.0/0, 0.0.0.0/1, or 128.0.0.0/1.
///
/// SocketOptionName in dotnet/runtime has no UnicastInterface member, so this
/// class calls setsockopt/getsockopt directly instead of inventing an enum name.
/// Bind() to the VPN address is intentionally not used as a substitute.
/// </summary>
internal static class WindowsSocketInterfaceBinder
{
    // ws2def.h / ws2ipdef.h (Windows SDK 10.0.22621)
    private const int IpProtoIp = 0;
    private const int IpProtoIpv6 = 41;
    private const int IpUnicastIf = 31;
    private const int Ipv6UnicastIf = 31;
    private const int SocketError = -1;

    [DllImport("ws2_32.dll", SetLastError = true)]
    private static extern int setsockopt(IntPtr socket, int level, int optname, in int optval, int optlen);

    [DllImport("ws2_32.dll", SetLastError = true)]
    private static extern int getsockopt(IntPtr socket, int level, int optname, out int optval, ref int optlen);

    [DllImport("ws2_32.dll")]
    private static extern int WSAGetLastError();

    public static int SetIpv4UnicastInterface(Socket socket, int interfaceIndex)
    {
        if (interfaceIndex <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(interfaceIndex), "Interface index must be positive.");
        }

        // Network byte order: HostToNetworkOrder(12) becomes 0x0C000000, which
        // Winsock treats as 0.0.0.12 in the 0.x.x.x interface-index encoding.
        int networkOrderIndex = IPAddress.HostToNetworkOrder(interfaceIndex);
        Set(socket, IpProtoIp, IpUnicastIf, networkOrderIndex);
        return interfaceIndex;
    }

    public static bool TryGetIpv4UnicastInterface(Socket socket, out int interfaceIndex)
    {
        interfaceIndex = 0;
        if (!TryGet(socket, IpProtoIp, IpUnicastIf, out int value))
        {
            return false;
        }

        // GET is host byte order per Microsoft Learn.
        interfaceIndex = value;
        return true;
    }

    public static int SetIpv6UnicastInterface(Socket socket, int interfaceIndex)
    {
        if (interfaceIndex <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(interfaceIndex), "Interface index must be positive.");
        }

        Set(socket, IpProtoIpv6, Ipv6UnicastIf, interfaceIndex);
        return interfaceIndex;
    }

    private static void Set(Socket socket, int level, int optname, int value)
    {
        WithHandle(socket, handle =>
        {
            int rc = setsockopt(handle, level, optname, in value, sizeof(int));
            if (rc == SocketError)
            {
                throw CreateWinsockException("setsockopt");
            }
        });
    }

    private static bool TryGet(Socket socket, int level, int optname, out int value)
    {
        int local = 0;
        bool ok = false;
        WithHandle(socket, handle =>
        {
            int len = sizeof(int);
            int rc = getsockopt(handle, level, optname, out local, ref len);
            ok = rc != SocketError;
        });
        value = local;
        return ok;
    }

    private static void WithHandle(Socket socket, Action<IntPtr> action)
    {
        SafeHandle handle = socket.SafeHandle;
        bool added = false;
        handle.DangerousAddRef(ref added);
        try
        {
            action(handle.DangerousGetHandle());
        }
        finally
        {
            if (added)
            {
                handle.DangerousRelease();
            }
        }
    }

    private static SocketException CreateWinsockException(string api)
    {
        int error = Marshal.GetLastWin32Error();
        if (error == 0)
        {
            error = WSAGetLastError();
        }

        return new SocketException(error)
        {
            HelpLink = api,
        };
    }
}
