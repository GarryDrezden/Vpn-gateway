namespace SelectiveVpnRouter.Core;

/// <summary>
/// Single-owner CTS: cancel before dispose, safe for repeated/concurrent dispose attempts.
/// </summary>
public sealed class OwnedCancellationTokenSource : IDisposable
{
    private readonly CancellationTokenSource _inner = new();
    private int _disposed;

    public CancellationToken Token => _inner.Token;

    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    public void Cancel()
    {
        if (IsDisposed)
        {
            return;
        }

        try
        {
            _inner.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        try
        {
            if (!_inner.IsCancellationRequested)
            {
                _inner.Cancel();
            }
        }
        catch (ObjectDisposedException)
        {
        }

        try
        {
            _inner.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }
    }
}