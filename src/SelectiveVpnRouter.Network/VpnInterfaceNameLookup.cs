using System.Net.NetworkInformation;
using SelectiveVpnRouter.Core.BrowserRouting;

namespace SelectiveVpnRouter.Network;

public sealed class NetworkInterfaceVpnNameLookup : IVpnInterfaceNameLookup
{
    public string? TryGetInterfaceName(int interfaceIndex)
    {
        foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up)
                continue;

            try
            {
                if (nic.GetIPProperties().GetIPv4Properties().Index != interfaceIndex)
                    continue;

                return nic.Name;
            }
            catch (NetworkInformationException)
            {
            }
        }

        return null;
    }
}
