namespace SelectiveVpnRouter.Core.BrowserRouting;

public static class BrowserIntegrationContract
{
    public const int IntegrationApiVersion = 1;

    public const int ClientVersionMaxLength = 128;

    public static readonly string[] Capabilities =
    [
        "browserClientHeartbeat",
        "browserExplicitSocks",
        "browserRoutingState",
        "vpnEgressReadiness",
    ];

    public static class VpnEgressStatus
    {
        public const string Ready = "Ready";
        public const string Unavailable = "Unavailable";
    }

    public static class BrowserClientStatus
    {
        public const string NeverSeen = "NeverSeen";
        public const string RecentlySeen = "RecentlySeen";
        public const string Stale = "Stale";
    }
}

public interface IBrowserIntegrationServiceVersion
{
    string ServiceVersion { get; }
}

public interface IVpnInterfaceNameLookup
{
    string? TryGetInterfaceName(int interfaceIndex);
}
