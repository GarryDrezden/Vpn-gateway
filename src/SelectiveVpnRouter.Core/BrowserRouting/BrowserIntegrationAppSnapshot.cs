namespace SelectiveVpnRouter.Core.BrowserRouting;

/// <summary>Read-only browser integration observability for the main App control IPC snapshot.</summary>
public sealed record BrowserIntegrationSnapshot
{
    public int IntegrationApiVersion { get; init; }
    public string? ServiceVersion { get; init; }
    public required BrowserIntegrationClientSnapshot BrowserClient { get; init; }
    public required BrowserIntegrationProxySnapshot BrowserProxy { get; init; }
    public required BrowserIntegrationVpnEgressSnapshot VpnEgress { get; init; }
    public int RuleCount { get; init; }
}

public sealed record BrowserIntegrationClientSnapshot(string Status, DateTimeOffset? LastSeenUtc);

public sealed record BrowserIntegrationEndpointSnapshot(string Host, int Port);

public sealed record BrowserIntegrationProxySnapshot(string Status, BrowserIntegrationEndpointSnapshot? Endpoint);

public sealed record BrowserIntegrationVpnEgressSnapshot(string Status, int? InterfaceIndex, string? InterfaceName);

public interface IBrowserIntegrationSnapshotProvider
{
    BrowserIntegrationSnapshot Create();
}
