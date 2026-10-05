namespace SelectiveVpnRouter.Core.RoutingTrace;

public sealed class RoutingTraceRingBuffer
{
    private readonly int _capacity;
    private readonly List<RoutingTraceEvent> _events = new();
    private long _sequence;

    public RoutingTraceRingBuffer(int capacity)
    {
        _capacity = Math.Max(1, capacity);
    }

    public int DroppedEventCount { get; private set; }
    public long LastSequenceId => _sequence;

    public RoutingTraceEvent Add(RoutingTraceEvent draft)
    {
        long seq = draft.SequenceId > 0 ? draft.SequenceId : ++_sequence;
        if (draft.SequenceId <= 0)
        {
            _sequence = seq;
        }
        else if (seq > _sequence)
        {
            _sequence = seq;
        }

        RoutingTraceEvent stored = draft with { SequenceId = seq };
        _events.Add(stored);
        while (_events.Count > _capacity)
        {
            _events.RemoveAt(0);
            DroppedEventCount++;
        }

        return stored;
    }

    public IReadOnlyList<RoutingTraceEvent> SnapshotOrdered()
        => _events.OrderBy(e => e.SequenceId).ToArray();

    public RoutingTraceEventsPage GetPage(long afterSequence, int limit)
    {
        IReadOnlyList<RoutingTraceEvent> ordered = SnapshotOrdered();
        List<RoutingTraceEvent> page = ordered
            .Where(e => e.SequenceId > afterSequence)
            .Take(Math.Max(1, limit))
            .ToList();
        long last = page.Count > 0 ? page[^1].SequenceId : afterSequence;
        bool hasMore = ordered.Any(e => e.SequenceId > last);
        return new RoutingTraceEventsPage
        {
            Events = page,
            LastSequenceId = last,
            HasMore = hasMore,
        };
    }

    public void Clear()
    {
        _events.Clear();
        _sequence = 0;
        DroppedEventCount = 0;
    }
}
