namespace SelectiveVpnRouter.Proxy.BrowserExplicit;

/// <summary>Minimal surface for Service lifecycle ownership (start/stop listener).</summary>
public interface IBrowserExplicitSocksProxyRuntime
{
    int Port { get; }

    Task StartAsync(CancellationToken cancellationToken);

    Task StopListeningAsync();

    /// <summary>Waits until the accept loop stops. Throws if it stops before <paramref name="stoppingToken"/>.</summary>
    Task WaitForRuntimeAsync(CancellationToken stoppingToken);
}
