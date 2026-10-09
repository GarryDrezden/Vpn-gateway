using Microsoft.Extensions.DependencyInjection;
using SelectiveVpnRouter.Core.BrowserRouting;
using SelectiveVpnRouter.Network;
using SelectiveVpnRouter.Proxy.BrowserExplicit;

namespace SelectiveVpnRouter.Service;

public static class BrowserExplicitServiceCollectionExtensions
{
    public static IServiceCollection AddBrowserExplicitProxyRuntime(this IServiceCollection services)
    {
        services.AddSingleton<RuntimeBrowserProxyReadiness>();
        services.AddSingleton<IBrowserProxyReadiness>(sp => sp.GetRequiredService<RuntimeBrowserProxyReadiness>());
        services.AddSingleton<IBrowserIntegrationServiceVersion, EntryAssemblyBrowserIntegrationServiceVersion>();
        services.AddSingleton<BrowserClientTracker>();
        services.AddSingleton<BrowserRoutingChangeNotifier>();
        services.AddSingleton(sp =>
        {
            var store = new BrowserRoutingStateStore(
                BrowserRoutingStateStore.DefaultPath,
                changeNotifier: sp.GetRequiredService<BrowserRoutingChangeNotifier>());
            store.Load();
            return store;
        });
        services.AddSingleton<IBrowserIntegrationSnapshotProvider, BrowserIntegrationSnapshotProvider>();
        services.AddSingleton<IVpnInterfaceNameLookup, NetworkInterfaceVpnNameLookup>();
        services.AddSingleton<VpnSessionDnsStore>();
        services.AddSingleton<IVpnSessionDnsServers>(sp => sp.GetRequiredService<VpnSessionDnsStore>());
        services.AddSingleton<IVpnTunnelEgressReadiness>(sp => sp.GetRequiredService<RouterEngine>());
        services.AddSingleton<VpnInterfaceDnsResolver>();
        services.AddSingleton<IVpnTcpEgress, VpnBoundTcpEgress>();
        services.AddSingleton<BrowserExplicitSocksProxy>();
        services.AddSingleton<IBrowserExplicitSocksProxyRuntime>(sp => sp.GetRequiredService<BrowserExplicitSocksProxy>());
        services.AddHostedService<BrowserExplicitProxyHost>();
        return services;
    }
}
