using System.Threading;
using SelectiveVpnRouter.Core.Startup;

namespace SelectiveVpnRouter.App.Startup;

internal sealed class GuiSingleInstanceCoordinator : IDisposable
{
    private readonly Mutex? _mutex;
    private readonly EventWaitHandle? _activateEvent;
    private readonly CancellationTokenSource _listenerCts = new();
    private bool _disposed;
    private bool _ownsMutex;

    public bool IsPrimary { get; }

    public GuiSingleInstanceCoordinator(bool singleInstanceEnabled)
    {
        if (!singleInstanceEnabled)
        {
            IsPrimary = true;
            return;
        }

        (string mutexName, string eventName) = GuiSingleInstanceIdentity.NamesForCurrentInteractiveUser();
        _mutex = new Mutex(initiallyOwned: true, mutexName, out bool createdNew);
        GuiSingleInstanceAttempt attempt = GuiSingleInstancePolicy.Evaluate(singleInstanceEnabled, createdNew);
        IsPrimary = attempt.IsPrimary;
        _ownsMutex = createdNew && IsPrimary;
        if (!IsPrimary)
        {
            if (attempt.ShouldSignalExistingInstance)
            {
                TrySignalExistingInstance(eventName);
            }

            return;
        }

        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, eventName);
    }

    public void StartActivationListener(Action onActivate)
    {
        if (!IsPrimary || _activateEvent is null)
        {
            return;
        }

        EventWaitHandle activateEvent = _activateEvent;
        CancellationToken token = _listenerCts.Token;
        _ = Task.Run(() =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (!activateEvent.WaitOne(TimeSpan.FromMilliseconds(500)))
                    {
                        continue;
                    }
                }
                catch (AbandonedMutexException)
                {
                    continue;
                }

                if (token.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    onActivate();
                }
                catch (Exception)
                {
                }
            }
        }, token);
    }

    private static void TrySignalExistingInstance(string eventName)
    {
        try
        {
            using EventWaitHandle existing = EventWaitHandle.OpenExisting(eventName);
            existing.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _listenerCts.Cancel();
        _activateEvent?.Dispose();
        if (_ownsMutex)
        {
            try
            {
                _mutex?.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }
        }

        _mutex?.Dispose();
        _listenerCts.Dispose();
    }
}
