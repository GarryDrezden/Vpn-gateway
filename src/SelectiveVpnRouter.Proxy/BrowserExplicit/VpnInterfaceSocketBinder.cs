using System.Net.Sockets;
using SelectiveVpnRouter.Network;

namespace SelectiveVpnRouter.Proxy.BrowserExplicit;

internal interface IVpnInterfaceSocketBinder
{
    void BindIpv4(Socket socket, int interfaceIndex);

    void BindIpv6(Socket socket, int interfaceIndex);
}

internal sealed class ProductionVpnInterfaceSocketBinder : IVpnInterfaceSocketBinder
{
    public void BindIpv4(Socket socket, int interfaceIndex) =>
        SocketInterfaceBinder.BindIpv4UnicastIf(socket, interfaceIndex);

    public void BindIpv6(Socket socket, int interfaceIndex) =>
        SocketInterfaceBinder.BindIpv6UnicastIf(socket, interfaceIndex);
}
