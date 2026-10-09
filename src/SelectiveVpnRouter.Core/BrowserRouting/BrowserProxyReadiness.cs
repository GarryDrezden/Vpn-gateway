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

/// <summary>Stub for tests; production Service uses <see cref="RuntimeBrowserProxyReadiness"/>.</summary>
public sealed class UnavailableBrowserProxyReadiness : IBrowserProxyReadiness
{
    public BrowserProxyStatus GetStatus() => BrowserProxyStatus.NotAvailable;
}
