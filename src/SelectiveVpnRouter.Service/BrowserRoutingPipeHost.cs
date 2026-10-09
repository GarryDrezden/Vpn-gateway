using Microsoft.Extensions.Hosting;
using SelectiveVpnRouter.Core.BrowserRouting;

namespace SelectiveVpnRouter.Service;

/// <summary>
/// Serves the authoritative browser routing state on its own read-only pipe
/// (<see cref="BrowserRoutingIpcProtocol.PipeName"/>). Independent of <see cref="RouterEngine"/>:
/// it never connects VPN, never touches routes/WFP and never reads config.json.
/// </summary>
public sealed class BrowserRoutingPipeHost(
    RouterEngine engine,
    BrowserRoutingStateStore store,
    IBrowserProxyReadiness proxyReadiness,
    IVpnTunnelEgressReadiness vpnTunnelEgressReadiness,
    BrowserClientTracker browserClientTracker,
    IBrowserIntegrationServiceVersion serviceVersion,
    IVpnInterfaceNameLookup interfaceNameLookup) : BackgroundService
{
    private static readonly TimeSpan RestartDelay = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var status = store.Status == BrowserRoutingStoreStatus.NotLoaded ? store.Load() : store.Status;
        engine.Log(status == BrowserRoutingStoreStatus.Available
            ? $"browser-routing state loaded: revision {store.Current!.Revision}, {store.Current.RuleCount} rules"
            : $"browser-routing state unavailable: {store.UnavailableReason}");

        var dispatcher = new BrowserRoutingIpcDispatcher(
            () => store.Current,
            proxyReadiness,
            vpnTunnelEgressReadiness,
            browserClientTracker,
            serviceVersion,
            interfaceNameLookup);
        var server = new BrowserRoutingPipeServer(BrowserRoutingIpcProtocol.PipeName, dispatcher, engine.Log);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await server.RunAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                engine.Log("browser-routing pipe: " + ex.GetType().Name);
                await Task.Delay(RestartDelay, stoppingToken).ConfigureAwait(false);
            }
        }
    }
}
