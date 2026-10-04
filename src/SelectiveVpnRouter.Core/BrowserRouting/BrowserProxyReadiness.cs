namespace SelectiveVpnRouter.Core.BrowserRouting;

/// <summary>Readiness of the explicit browser proxy that a browser PAC would point VPN routes at.</summary>
public sealed record BrowserProxyStatus(string Status, string? EndpointHost, int? EndpointPort)
{
    public const string Unavailable = "Unavailable";
    public const string Ready = "Ready";

    public static BrowserProxyStatus NotAvailable { get; } = new(Unavailable, null, null);
}

public interface IBrowserProxyReadiness
{
    BrowserProxyStatus GetStatus();
}

/// <summary>
/// The production explicit browser proxy does not exist yet: the Service always reports Unavailable,
/// so no browser applies a PAC with an endpoint nobody listens on. The transparent app-routing proxy
/// (<c>ServiceSnapshot.ProxyPort</c>) is a different component and is never reported here.
/// </summary>
public sealed class UnavailableBrowserProxyReadiness : IBrowserProxyReadiness
{
    public BrowserProxyStatus GetStatus() => BrowserProxyStatus.NotAvailable;
}
