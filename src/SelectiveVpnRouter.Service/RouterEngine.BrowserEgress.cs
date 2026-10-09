using SelectiveVpnRouter.Core;
using SelectiveVpnRouter.Core.BrowserRouting;

namespace SelectiveVpnRouter.Service;

public sealed partial class RouterEngine : IVpnTunnelEgressReadiness
{
    public bool TryGetTunnelInterfaceIndex(out int interfaceIndex)
    {
        interfaceIndex = 0;
        lock (_gate)
        {
            OpenVpnLiveStatus live = _vpn?.Live() ?? new OpenVpnLiveStatus();
            return TunnelEgressReadinessEvaluator.TryGetTunnelInterfaceIndex(
                live,
                _vpnAdapterSelection,
                _vpnAdapter is not null,
                out interfaceIndex);
        }
    }
}
