using Microsoft.Extensions.Hosting;
using SelectiveVpnRouter.Core.BrowserRouting;
using SelectiveVpnRouter.Proxy.BrowserExplicit;

namespace SelectiveVpnRouter.Service;

/// <summary>Owns the browser explicit SOCKS listener lifecycle (separate from transparent WFP proxy).</summary>
public sealed class BrowserExplicitProxyHost(
    IBrowserExplicitSocksProxyRuntime proxy,
    RuntimeBrowserProxyReadiness readiness,
    RouterEngine engine) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        BrowserExplicitProxyLifecycle.RunUntilStoppedAsync(proxy, readiness, engine.Log, stoppingToken);
}
