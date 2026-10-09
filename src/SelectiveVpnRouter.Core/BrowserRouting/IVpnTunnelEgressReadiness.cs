namespace SelectiveVpnRouter.Core.BrowserRouting;

/// <summary>
/// Authoritative VPN tunnel interface for browser explicit proxy egress.
/// Implemented by production RouterEngine using runtime OpenVPN and adapter selection state only.
/// </summary>
public interface IVpnTunnelEgressReadiness
{
    /// <summary>
    /// Returns true and the tunnel interface index when OpenVPN is running and connected
    /// and a valid VPN adapter selection is present; otherwise false.
    /// </summary>
    bool TryGetTunnelInterfaceIndex(out int interfaceIndex);
}
