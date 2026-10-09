namespace SelectiveVpnRouter.Core.BrowserRouting;

/// <summary>Builds the App observability snapshot from existing browser integration runtime sources (no duplicate state).</summary>
public sealed class BrowserIntegrationSnapshotProvider(
    BrowserRoutingStateStore stateStore,
    IBrowserProxyReadiness proxyReadiness,
    IVpnTunnelEgressReadiness vpnTunnelEgressReadiness,
    BrowserClientTracker browserClientTracker,
    IBrowserIntegrationServiceVersion serviceVersion,
    IVpnInterfaceNameLookup interfaceNameLookup) : IBrowserIntegrationSnapshotProvider
{
    public BrowserIntegrationSnapshot Create()
    {
        BrowserClientSnapshot client = browserClientTracker.GetSnapshot();
        BrowserProxyStatus proxy = proxyReadiness.GetStatus();
        int ruleCount = stateStore.Current?.RuleCount ?? 0;

        return new BrowserIntegrationSnapshot
        {
            IntegrationApiVersion = BrowserIntegrationContract.IntegrationApiVersion,
            ServiceVersion = serviceVersion.ServiceVersion,
            BrowserClient = new BrowserIntegrationClientSnapshot(client.Status, client.LastSeenUtc),
            BrowserProxy = ToProxySnapshot(proxy),
            VpnEgress = ToVpnEgressSnapshot(),
            RuleCount = ruleCount,
        };
    }

    private BrowserIntegrationProxySnapshot ToProxySnapshot(BrowserProxyStatus proxy)
    {
        if (proxy.Status == BrowserProxyStatus.Ready &&
            IsLoopback(proxy.EndpointHost) &&
            proxy.EndpointPort is >= 1 and <= 65535)
        {
            return new BrowserIntegrationProxySnapshot(
                BrowserProxyStatus.Ready,
                new BrowserIntegrationEndpointSnapshot(proxy.EndpointHost!, proxy.EndpointPort.Value));
        }

        return new BrowserIntegrationProxySnapshot(BrowserProxyStatus.Unavailable, null);
    }

    private BrowserIntegrationVpnEgressSnapshot ToVpnEgressSnapshot()
    {
        if (vpnTunnelEgressReadiness.TryGetTunnelInterfaceIndex(out int interfaceIndex))
        {
            return new BrowserIntegrationVpnEgressSnapshot(
                BrowserIntegrationContract.VpnEgressStatus.Ready,
                interfaceIndex,
                interfaceNameLookup.TryGetInterfaceName(interfaceIndex));
        }

        return new BrowserIntegrationVpnEgressSnapshot(
            BrowserIntegrationContract.VpnEgressStatus.Unavailable,
            null,
            null);
    }

    private static bool IsLoopback(string? host)
    {
        if (string.IsNullOrEmpty(host))
            return false;
        if (!System.Net.IPAddress.TryParse(host, out var address))
            return false;
        return System.Net.IPAddress.IsLoopback(address);
    }
}
