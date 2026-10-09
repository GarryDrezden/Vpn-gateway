namespace SelectiveVpnRouter.Core.BrowserRouting;

public static class BrowserClientRecentlySeenTtl
{
    public static readonly TimeSpan Value = TimeSpan.FromSeconds(120);
}

public sealed record BrowserClientSnapshot(string Status, DateTimeOffset? LastSeenUtc);

public sealed class BrowserClientTracker(TimeProvider timeProvider)
{
    private readonly object _gate = new();
    private DateTimeOffset? _lastSeenUtc;
    private string? _lastExtensionVersion;
    private string? _lastNativeHostVersion;

    public BrowserClientTracker()
        : this(TimeProvider.System)
    {
    }

    internal string? LastExtensionVersion
    {
        get
        {
            lock (_gate)
                return _lastExtensionVersion;
        }
    }

    internal string? LastNativeHostVersion
    {
        get
        {
            lock (_gate)
                return _lastNativeHostVersion;
        }
    }

    public void Touch(string extensionVersion, string nativeHostVersion)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        lock (_gate)
        {
            _lastSeenUtc = now;
            _lastExtensionVersion = extensionVersion;
            _lastNativeHostVersion = nativeHostVersion;
        }
    }

    public BrowserClientSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            if (_lastSeenUtc is null)
                return new BrowserClientSnapshot(BrowserIntegrationContract.BrowserClientStatus.NeverSeen, null);

            DateTimeOffset now = timeProvider.GetUtcNow();
            TimeSpan elapsed = now >= _lastSeenUtc.Value
                ? now - _lastSeenUtc.Value
                : TimeSpan.Zero;

            string status = elapsed <= BrowserClientRecentlySeenTtl.Value
                ? BrowserIntegrationContract.BrowserClientStatus.RecentlySeen
                : BrowserIntegrationContract.BrowserClientStatus.Stale;
            return new BrowserClientSnapshot(status, _lastSeenUtc);
        }
    }
}
