using System.Net;
using System.Net.Sockets;

namespace SelectiveVpnRouter.Core.RoutingTrace;

public static class RoutingTraceDestinationClassifier
{
    public static RoutingTraceDestinationKind Classify(string address, RoutingTraceAddressFamily family)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return RoutingTraceDestinationKind.Unknown;
        }

        if (!IPAddress.TryParse(address, out IPAddress? ip))
        {
            return RoutingTraceDestinationKind.Unknown;
        }

        if (IPAddress.IsLoopback(ip))
        {
            return RoutingTraceDestinationKind.Loopback;
        }

        if (ip.IsIPv6LinkLocal || ip.IsIPv6UniqueLocal)
        {
            return RoutingTraceDestinationKind.LinkLocal;
        }

        if (IsPrivateLan(ip))
        {
            return RoutingTraceDestinationKind.PrivateLan;
        }

        return RoutingTraceDestinationKind.External;
    }

    private static bool IsPrivateLan(IPAddress ip)
    {
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            byte[] b = ip.GetAddressBytes();
            return b[0] == 10
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168);
        }

        return false;
    }
}
