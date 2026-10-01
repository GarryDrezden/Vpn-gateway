namespace SelectiveVpnRouter.Core;

/// <summary>
/// Ensures async dispose body runs at most once across concurrent callers.
/// </summary>
public struct AsyncDisposeGate
{
    private int _entered;

    public bool TryEnter() => Interlocked.CompareExchange(ref _entered, 1, 0) == 0;

    public bool HasEntered => Volatile.Read(ref _entered) != 0;
}