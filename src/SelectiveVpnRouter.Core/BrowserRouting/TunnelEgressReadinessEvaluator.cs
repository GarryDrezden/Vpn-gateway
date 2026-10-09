namespace SelectiveVpnRouter.Core.BrowserRouting;

/// <summary>
/// Pure evaluation of browser tunnel egress readiness from authoritative live OpenVPN status and adapter selection.
/// </summary>
public static class TunnelEgressReadinessEvaluator
{
    public static bool TryGetTunnelInterfaceIndex(
        OpenVpnLiveStatus live,
        VpnAdapterSelection? adapterSelection,
        bool hasVpnAdapter,
        out int interfaceIndex)
    {
        interfaceIndex = 0;
        if (adapterSelection is null || adapterSelection.IfIndex <= 0)
        {
            return false;
        }

        if (!hasVpnAdapter)
        {
            return false;
        }

        if (!live.Running || !live.Connected)
        {
            return false;
        }

        interfaceIndex = adapterSelection.IfIndex;
        return true;
    }
}
