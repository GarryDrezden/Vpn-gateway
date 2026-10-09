namespace SelectiveVpnRouter.Core.BrowserRouting;

/// <summary>Thread-safe runtime readiness for the loopback browser explicit SOCKS listener.</summary>
public sealed class RuntimeBrowserProxyReadiness : IBrowserProxyReadiness
{
    private readonly object _gate = new();
    private BrowserProxyStatus _status = BrowserProxyStatus.NotAvailable;

    public BrowserProxyStatus GetStatus()
    {
        lock (_gate)
            return _status;
    }

    public void SetReady(int port) => SetReady("127.0.0.1", port);

    public void SetReady(string host, int port)
    {
        lock (_gate)
            _status = new BrowserProxyStatus(BrowserProxyStatus.Ready, host, port);
    }

    public void SetUnavailable()
    {
        lock (_gate)
            _status = BrowserProxyStatus.NotAvailable;
    }
}
