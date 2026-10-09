using SelectiveVpnRouter.Core.BrowserRouting;
using SelectiveVpnRouter.Proxy.BrowserExplicit;

namespace SelectiveVpnRouter.Service;

internal static class BrowserExplicitProxyLifecycle
{
    internal static readonly TimeSpan RestartDelay = TimeSpan.FromSeconds(5);

    internal static async Task RunUntilStoppedAsync(
        IBrowserExplicitSocksProxyRuntime proxy,
        RuntimeBrowserProxyReadiness readiness,
        Action<string> log,
        CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                log("browser-explicit-proxy starting");
                await proxy.StartAsync(stoppingToken).ConfigureAwait(false);
                readiness.SetReady("127.0.0.1", proxy.Port);
                log("browser-explicit-proxy ready endpoint=127.0.0.1:" + proxy.Port);
                await proxy.WaitForRuntimeAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                readiness.SetUnavailable();
                log("browser-explicit-proxy unavailable reason=" + ex.GetType().Name);
                try
                {
                    await proxy.StopListeningAsync().ConfigureAwait(false);
                }
                catch (Exception stopEx)
                {
                    log("browser-explicit-proxy stop-after-failure: " + stopEx.GetType().Name);
                }

                try
                {
                    log("browser-explicit-proxy restarting delay=" + RestartDelay.TotalSeconds.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "s");
                    await Task.Delay(RestartDelay, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        readiness.SetUnavailable();
        try
        {
            await proxy.StopListeningAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            log("browser-explicit-proxy stop: " + ex.GetType().Name);
        }

        log("browser-explicit-proxy stopped");
    }
}
