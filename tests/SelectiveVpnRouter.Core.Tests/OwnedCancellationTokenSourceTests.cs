using System.Threading;
using SelectiveVpnRouter.Core;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public class OwnedCancellationTokenSourceTests
{
    [Fact]
    public void Dispose_once_succeeds()
    {
        var cts = new OwnedCancellationTokenSource();
        cts.Dispose();
        Assert.True(cts.IsDisposed);
    }

    [Fact]
    public void Dispose_twice_sequential_no_exception()
    {
        var cts = new OwnedCancellationTokenSource();
        cts.Dispose();
        cts.Dispose();
    }

    [Fact]
    public void Dispose_concurrent_no_exception()
    {
        var cts = new OwnedCancellationTokenSource();
        Parallel.Invoke(
            () => cts.Dispose(),
            () => cts.Dispose(),
            () => cts.Dispose());
        Assert.True(cts.IsDisposed);
    }

    [Fact]
    public void Cancel_before_dispose_then_dispose_no_exception()
    {
        var cts = new OwnedCancellationTokenSource();
        cts.Cancel();
        Assert.True(cts.Token.IsCancellationRequested);
        cts.Dispose();
    }

    [Fact]
    public void Cancel_after_dispose_no_exception()
    {
        var cts = new OwnedCancellationTokenSource();
        cts.Dispose();
        cts.Cancel();
    }
}

public class AsyncDisposeGateTests
{
    [Fact]
    public void TryEnter_only_first_succeeds()
    {
        var gate = new AsyncDisposeGate();
        Assert.True(gate.TryEnter());
        Assert.False(gate.TryEnter());
    }

    [Fact]
    public void Concurrent_try_enter_only_one_wins()
    {
        var gate = new AsyncDisposeGate();
        int wins = 0;
        Parallel.For(0, 8, _ =>
        {
            if (gate.TryEnter())
            {
                Interlocked.Increment(ref wins);
            }
        });
        Assert.Equal(1, wins);
    }
}