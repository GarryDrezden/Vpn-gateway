using Microsoft.Extensions.Hosting;
using SelectiveVpnRouter.Core.BrowserRouting;

namespace SelectiveVpnRouter.Service;

/// <summary>
/// Serves browser routing push events on <see cref="BrowserRoutingEventsIpcProtocol.PipeName"/>.
/// Independent of the request/response browser routing pipe.
/// </summary>
public sealed class BrowserRoutingEventsPipeHost(
    RouterEngine engine,
    BrowserRoutingStateStore store,
    BrowserRoutingChangeNotifier changeNotifier) : BackgroundService
{
    private static readonly TimeSpan RestartDelay = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (store.Status == BrowserRoutingStoreStatus.NotLoaded)
            store.Load();

        var server = new BrowserRoutingEventsPipeServer(
            BrowserRoutingEventsIpcProtocol.PipeName,
            () => store.Current,
            changeNotifier,
            engine.Log);
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
                engine.Log("browser-routing-events pipe: " + ex.GetType().Name);
                await Task.Delay(RestartDelay, stoppingToken).ConfigureAwait(false);
            }
        }
    }
}
