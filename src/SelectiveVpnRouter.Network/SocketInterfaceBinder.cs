using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace SelectiveVpnRouter.Network;

public static class SocketInterfaceBinder
{
    private const int IpProtoIp = 0;
    private const int IpProtoIpv6 = 41;
    private const int IpUnicastIf = 31;
    private const int Ipv6UnicastIf = 31;

    [DllImport("ws2_32.dll", SetLastError = true)]
    private static extern int setsockopt(IntPtr s, int level, int optname, in int optval, int optlen);

    [DllImport("ws2_32.dll")]
    private static extern int WSAGetLastError();

    public static void BindIpv4UnicastIf(Socket socket, int interfaceIndex)
    {
        int networkOrder = IPAddress.HostToNetworkOrder(interfaceIndex);
        WithHandle(socket, h =>
        {
            if (setsockopt(h, IpProtoIp, IpUnicastIf, in networkOrder, sizeof(int)) != 0)
            {
                throw new SocketException(Wsa());
            }
        });
    }

    public static void BindIpv6UnicastIf(Socket socket, int interfaceIndex)
    {
        WithHandle(socket, h =>
        {
            if (setsockopt(h, IpProtoIpv6, Ipv6UnicastIf, in interfaceIndex, sizeof(int)) != 0)
            {
                throw new SocketException(Wsa());
            }
        });
    }

    private static int Wsa()
    {
        int e = Marshal.GetLastWin32Error();
        return e == 0 ? WSAGetLastError() : e;
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
}
