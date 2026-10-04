namespace SelectiveVpnRouter.Core;

/// <summary>
/// Pure policy plan for per-app WFP filters (no Windows API).
/// </summary>
public static class WfpVpnAppFilterPlanner
{
    public const ulong LoopbackPermitFilterWeight = 0xFFFF;
    public const ulong RedirectCalloutFilterWeight = 0x1000;

    public static IReadOnlyList<WfpFilterRole> RequiredFiltersForVpnRoutedApp() =>
        [WfpFilterRole.LoopbackPermitV4, WfpFilterRole.RedirectCallout];

    public static IReadOnlyList<WfpFilterRole> RequiredFiltersForDirectRoutedApp() => [];

    public static bool LoopbackPermitHasHigherPriorityThanRedirect() =>
        LoopbackPermitFilterWeight > RedirectCalloutFilterWeight;

    public static bool LoopbackRemoteAddressMatches(string ipv4Host) =>
        WfpLoopbackIpv4.MatchesPermitRange(ipv4Host);

    public static bool ExternalRemoteAddressMatchesLoopbackPermit(string ipv4Host) =>
        !LoopbackRemoteAddressMatches(ipv4Host);

    public static bool RedirectCalloutShouldApplyToRemote(string ipv4Host) =>
        ExternalRemoteAddressMatchesLoopbackPermit(ipv4Host);
}

public static class WfpLoopbackIpv4
{
    public const uint PermitNetworkAddress = 0x7F000000;
    public const uint PermitNetworkMask = 0xFF000000;

    public static bool MatchesPermitRange(string ipv4Host) =>
        MatchesPermitRange(ParseNetworkOrderUInt32(ipv4Host));

    public static bool MatchesPermitRange(uint remoteAddressNetworkOrder) =>
        (remoteAddressNetworkOrder & PermitNetworkMask) == PermitNetworkAddress;

    public static uint ParseNetworkOrderUInt32(string ipv4Host)
    {
        byte[] b = System.Net.IPAddress.Parse(ipv4Host).GetAddressBytes();
        return ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
    }
}