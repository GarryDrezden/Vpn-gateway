using SelectiveVpnRouter.Network;

namespace SelectiveVpnRouter.Proxy.BrowserExplicit;

public interface IVpnBrowserHostnameResolver
{
    Task<VpnDnsResolutionResult> ResolveIpv4Async(string hostname, CancellationToken cancellationToken = default);
}

public sealed class VpnInterfaceDnsResolverHostnameResolver(VpnInterfaceDnsResolver inner) : IVpnBrowserHostnameResolver
{
    public Task<VpnDnsResolutionResult> ResolveIpv4Async(string hostname, CancellationToken cancellationToken = default) =>
        inner.ResolveIpv4Async(hostname, cancellationToken);
}
