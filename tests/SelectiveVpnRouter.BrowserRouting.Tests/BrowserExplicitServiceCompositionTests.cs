using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SelectiveVpnRouter.Core.BrowserRouting;
using SelectiveVpnRouter.Network;
using SelectiveVpnRouter.Proxy.BrowserExplicit;
using SelectiveVpnRouter.Service;
using Xunit;

namespace SelectiveVpnRouter.BrowserRouting.Tests;

public class BrowserExplicitServiceCompositionTests
{
    [Fact]
    public void P_tunnel_readiness_resolves_to_same_router_engine_singleton()
    {
        ServiceProvider sp = BuildProvider();
        var engine = sp.GetRequiredService<RouterEngine>();
        var readiness = sp.GetRequiredService<IVpnTunnelEgressReadiness>();
        Assert.Same(engine, readiness);
    }

    [Fact]
    public void Q_dns_resolver_uses_production_tunnel_readiness()
    {
        ServiceProvider sp = BuildProvider();
        var engine = sp.GetRequiredService<RouterEngine>();
        var dns = sp.GetRequiredService<VpnInterfaceDnsResolver>();
        Assert.NotNull(dns);
        Assert.False(engine.TryGetTunnelInterfaceIndex(out _));
    }

    [Fact]
    public void X_session_dns_store_is_single_source_for_router_and_resolver()
    {
        ServiceProvider sp = BuildProvider();
        var store = sp.GetRequiredService<VpnSessionDnsStore>();
        var sessionIface = sp.GetRequiredService<IVpnSessionDnsServers>();
        Assert.Same(store, sessionIface);
        Assert.Empty(store.GetIpv4DnsServers());
    }

    [Fact]
    public void R_vpn_bound_egress_uses_vpn_dns_and_readiness()
    {
        ServiceProvider sp = BuildProvider();
        var egress = sp.GetRequiredService<IVpnTcpEgress>();
        Assert.IsType<VpnBoundTcpEgress>(egress);
    }

    [Fact]
    public void S_browser_proxy_readiness_is_runtime_implementation()
    {
        ServiceProvider sp = BuildProvider();
        var runtime = sp.GetRequiredService<RuntimeBrowserProxyReadiness>();
        var readiness = sp.GetRequiredService<IBrowserProxyReadiness>();
        Assert.Same(runtime, readiness);
        Assert.IsNotType<UnavailableBrowserProxyReadiness>(readiness);
    }

    [Fact]
    public void T_browser_routing_pipe_host_wired_with_runtime_readiness()
    {
        ServiceProvider sp = BuildProvider();
        var runtime = sp.GetRequiredService<RuntimeBrowserProxyReadiness>();
        var readiness = sp.GetRequiredService<IBrowserProxyReadiness>();
        Assert.Same(runtime, readiness);
        Assert.NotNull(sp.GetServices<IHostedService>().OfType<BrowserRoutingPipeHost>().Single());
    }

    [Fact]
    public void U_only_one_browser_explicit_host_registered()
    {
        ServiceProvider sp = BuildProvider();
        Assert.Single(sp.GetServices<IHostedService>().OfType<BrowserExplicitProxyHost>());
    }

    [Fact]
    public void V_transparent_tcp_proxy_not_registered_as_browser_explicit()
    {
        ServiceProvider sp = BuildProvider();
        Assert.IsType<VpnBoundTcpEgress>(sp.GetRequiredService<IVpnTcpEgress>());
        Assert.DoesNotContain(sp.GetServices<IHostedService>(), s => s.GetType().Name.Contains("Transparent"));
    }

    [Fact]
    public void W_browser_client_tracker_is_singleton_for_ipc_and_app_snapshot()
    {
        ServiceProvider sp = BuildProvider();
        var trackerA = sp.GetRequiredService<BrowserClientTracker>();
        var trackerB = sp.GetRequiredService<BrowserClientTracker>();
        Assert.Same(trackerA, trackerB);
        var snapshotProvider = sp.GetRequiredService<IBrowserIntegrationSnapshotProvider>();
        trackerA.Touch("0.4.0", "0.5.0");
        Assert.Equal(BrowserIntegrationContract.BrowserClientStatus.RecentlySeen,
            snapshotProvider.Create().BrowserClient.Status);
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddBrowserExplicitProxyRuntime();
        services.AddSingleton(sp => new RouterEngine(
            new Lazy<IBrowserIntegrationSnapshotProvider>(() => sp.GetRequiredService<IBrowserIntegrationSnapshotProvider>()),
            sp.GetRequiredService<VpnSessionDnsStore>()));
        services.AddSingleton<IHostedService, BrowserRoutingPipeHost>();
        // BrowserRoutingPipeHost ctor deps satisfied by AddBrowserExplicitProxyRuntime.
        return services.BuildServiceProvider();
    }
}
