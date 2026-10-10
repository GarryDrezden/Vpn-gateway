using System.Threading.Channels;

namespace SelectiveVpnRouter.Core.BrowserRouting;

/// <summary>Lightweight in-process notifications after committed browser-routing mutations.</summary>
public sealed class BrowserRoutingChangeNotifier
{
    public sealed record ChangeEvent(string Type, string StateGeneration, long Revision);

    public const string BrowserRoutingChanged = "browserRoutingChanged";
    public const string ServiceAvailable = "serviceAvailable";

    private readonly object _lock = new();
    private readonly List<Action<ChangeEvent>> _subscribers = [];
    private readonly Channel<ChangeEvent> _queue = Channel.CreateUnbounded<ChangeEvent>(
        new UnboundedChannelOptions { SingleReader = true, AllowSynchronousContinuations = false });

    public BrowserRoutingChangeNotifier()
    {
        _ = Task.Run(DispatchLoopAsync);
    }

    public void PublishCommitted(string stateGeneration, long revision)
    {
        if (string.IsNullOrEmpty(stateGeneration))
            return;
        Publish(new ChangeEvent(BrowserRoutingChanged, stateGeneration, revision));
    }

    public void PublishServiceAvailable(string stateGeneration, long revision) =>
        Publish(new ChangeEvent(ServiceAvailable, stateGeneration, revision));

    public IDisposable Subscribe(Action<ChangeEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_lock)
        {
            _subscribers.Add(handler);
        }
        return new Subscription(this, handler);
    }

    private void Publish(ChangeEvent evt)
    {
        _queue.Writer.TryWrite(evt);
    }

    private async Task DispatchLoopAsync()
    {
        try
        {
            await foreach (ChangeEvent evt in _queue.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                Action<ChangeEvent>[] copy;
                lock (_lock)
                {
                    copy = _subscribers.ToArray();
                }
                foreach (var sub in copy)
                {
                    try { sub(evt); }
                    catch { /* subscriber fault must not break store */ }
                }
            }
        }
        catch (ChannelClosedException)
        {
        }
    }

    private void Unsubscribe(Action<ChangeEvent> handler)
    {
        lock (_lock)
        {
            _subscribers.Remove(handler);
        }
    }

    private sealed class Subscription(BrowserRoutingChangeNotifier owner, Action<ChangeEvent> handler) : IDisposable
    {
        public void Dispose() => owner.Unsubscribe(handler);
    }
}
