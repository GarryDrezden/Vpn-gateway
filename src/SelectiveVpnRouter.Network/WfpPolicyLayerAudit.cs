namespace SelectiveVpnRouter.Network;

public static class WfpPolicyLayerAudit
{
    public const string ConnectRedirectV4 = "FWPM_LAYER_ALE_CONNECT_REDIRECT_V4 (TCP IPv4 per-process redirect callout)";
    public const string AuthConnectV6Block = "FWPM_LAYER_ALE_AUTH_CONNECT_V6 (optional IPv6 TCP block for leak prevention)";

    public static IReadOnlyList<string> InstalledLayerDescriptions(bool driverPresent, bool ipv6Block) =>
    [
        driverPresent ? ConnectRedirectV4 : "(driver not loaded — no connect-redirect filter)",
        ipv6Block ? AuthConnectV6Block : "(IPv6 TCP block not installed for current policy)",
    ];

    public static string Summary(bool driverPresent, bool ipv6Block) =>
        string.Join("; ", InstalledLayerDescriptions(driverPresent, ipv6Block)) +
        ". No UDP/DNS/name-resolution WFP layers are installed by application rules.";
}