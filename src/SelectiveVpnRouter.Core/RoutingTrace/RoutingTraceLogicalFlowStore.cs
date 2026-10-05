namespace SelectiveVpnRouter.Core.RoutingTrace;

public sealed record RoutingTraceFlowUpsertResult(
    RoutingTraceEvent Event,
    bool Created,
    bool Updated);

public sealed class RoutingTraceLogicalFlowStore
{
    private readonly Dictionary<string, RoutingTraceEvent> _flowsByKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _aliasToCanonical = new(StringComparer.Ordinal);
    private long _nextSequence;

    public int DroppedEventCount { get; private set; }
    public int MaxFlows { get; init; } = 10_000;

    public IReadOnlyList<RoutingTraceEvent> SnapshotOrdered()
        => _flowsByKey.Values.OrderBy(e => e.SequenceId).ToArray();

    public RoutingTraceFlowUpsertResult Upsert(
        RoutingTraceEvent draft,
        string primaryKey,
        string weakKey,
        IReadOnlyList<string>? sourceAliases = null)
    {
        List<string> aliases = CollectAliases(draft, sourceAliases);
        string canonical = ResolveCanonicalKey(primaryKey, weakKey, aliases, draft);
        if (_flowsByKey.TryGetValue(canonical, out RoutingTraceEvent? existing))
        {
            DateTimeOffset lastSeen = ResolveLastSeenUtc(existing, draft);
            RoutingTraceEvent merged = MergeEvents(existing, draft);
            merged = merged with
            {
                FlowId = existing.FlowId,
                FlowCorrelationKey = canonical,
                SequenceId = existing.SequenceId,
                UpdateCount = existing.UpdateCount,
                FirstSeenUtc = existing.FirstSeenUtc == default ? draft.FirstSeenUtc : existing.FirstSeenUtc,
                LastSeenUtc = lastSeen,
                PreExistingAtTraceStart = existing.PreExistingAtTraceStart || draft.PreExistingAtTraceStart,
                ProcessStartUtcTicks = existing.ProcessStartUtcTicks != 0 ? existing.ProcessStartUtcTicks : draft.ProcessStartUtcTicks,
                SourceProxyFlowId = existing.SourceProxyFlowId ?? draft.SourceProxyFlowId,
            };
            merged = RoutingTraceCoverageClassifier.Apply(merged);

            if (!RoutingTraceSemanticChange.HasMeaningfulChange(existing, merged))
            {
                RoutingTraceEvent refreshed = existing with { LastSeenUtc = lastSeen };
                _flowsByKey[canonical] = refreshed;
                RegisterAliases(canonical, primaryKey, weakKey, aliases);
                return new RoutingTraceFlowUpsertResult(refreshed, Created: false, Updated: false);
            }

            merged = merged with
            {
                SequenceId = ++_nextSequence,
                UpdateCount = existing.UpdateCount + 1,
            };
            _flowsByKey[canonical] = merged;
            RegisterAliases(canonical, primaryKey, weakKey, aliases);
            return new RoutingTraceFlowUpsertResult(merged, Created: false, Updated: true);
        }

        if (_flowsByKey.Count >= MaxFlows)
        {
            DroppedEventCount++;
            return new RoutingTraceFlowUpsertResult(draft, Created: false, Updated: false);
        }

        var created = draft with
        {
            FlowId = Guid.NewGuid(),
            FlowCorrelationKey = canonical,
            SequenceId = ++_nextSequence,
            UpdateCount = 0,
            FirstSeenUtc = draft.FirstSeenUtc == default ? draft.Timestamp : draft.FirstSeenUtc,
            LastSeenUtc = draft.LastSeenUtc == default ? draft.Timestamp : draft.LastSeenUtc,
        };
        created = RoutingTraceCoverageClassifier.Apply(created);
        _flowsByKey[canonical] = created;
        RegisterAliases(canonical, primaryKey, weakKey, aliases);
        return new RoutingTraceFlowUpsertResult(created, Created: true, Updated: false);
    }

    public void Clear()
    {
        _flowsByKey.Clear();
        _aliasToCanonical.Clear();
        _nextSequence = 0;
        DroppedEventCount = 0;
    }

    private static List<string> CollectAliases(RoutingTraceEvent draft, IReadOnlyList<string>? sourceAliases)
    {
        var aliases = new List<string>();
        if (sourceAliases is not null)
        {
            aliases.AddRange(sourceAliases.Where(a => !string.IsNullOrWhiteSpace(a)));
        }

        if (draft.SourceProxyFlowId is Guid proxyId && proxyId != Guid.Empty)
        {
            aliases.Add(RoutingTraceSourceAliases.ProxyFlow(proxyId));
        }

        return aliases;
    }

    private string ResolveCanonicalKey(
        string primaryKey,
        string weakKey,
        IReadOnlyList<string> aliases,
        RoutingTraceEvent draft)
    {
        foreach (string alias in aliases)
        {
            if (_aliasToCanonical.TryGetValue(alias, out string? byAlias))
            {
                return byAlias;
            }
        }

        if (_flowsByKey.ContainsKey(primaryKey))
        {
            return primaryKey;
        }

        if (_aliasToCanonical.TryGetValue(primaryKey, out string? byPrimary))
        {
            return byPrimary;
        }

        if (string.Equals(primaryKey, weakKey, StringComparison.Ordinal))
        {
            if (_aliasToCanonical.TryGetValue(weakKey, out string? byWeak))
            {
                return byWeak;
            }

            if (_flowsByKey.ContainsKey(weakKey))
            {
                return weakKey;
            }

            string? temporal = TryFindWeakTemporalMatch(draft, weakKey);
            if (temporal is not null)
            {
                return temporal;
            }
        }

        return primaryKey.StartsWith("strong|", StringComparison.Ordinal)
            ? primaryKey
            : weakKey;
    }

    private string? TryFindWeakTemporalMatch(RoutingTraceEvent draft, string weakKey)
    {
        if (!weakKey.StartsWith("weak|", StringComparison.Ordinal))
        {
            return null;
        }

        if (draft.LocalPort > 0 && !string.IsNullOrWhiteSpace(draft.LocalAddress))
        {
            return null;
        }

        DateTimeOffset incomingStart = draft.FirstSeenUtc != default ? draft.FirstSeenUtc : draft.Timestamp;
        DateTimeOffset incomingEnd = draft.LastSeenUtc != default ? draft.LastSeenUtc : incomingStart;
        string? singleMatch = null;

        foreach (RoutingTraceEvent existing in _flowsByKey.Values)
        {
            if (existing.LocalPort > 0 && !string.IsNullOrWhiteSpace(existing.LocalAddress))
            {
                continue;
            }

            string existingWeak = RoutingTraceFlowCorrelation.BuildWeakKey(
                existing.ProcessId,
                existing.ProcessStartUtcTicks,
                existing.Protocol,
                existing.AddressFamily,
                existing.RemoteAddress,
                existing.RemotePort);
            if (!string.Equals(existingWeak, weakKey, StringComparison.Ordinal))
            {
                continue;
            }

            if (draft.SourceProxyFlowId is Guid incomingProxy
                && existing.SourceProxyFlowId is Guid existingProxy
                && incomingProxy != Guid.Empty
                && existingProxy != Guid.Empty
                && incomingProxy != existingProxy)
            {
                continue;
            }

            DateTimeOffset existingStart = existing.FirstSeenUtc != default ? existing.FirstSeenUtc : existing.Timestamp;
            DateTimeOffset existingEnd = existing.LastSeenUtc != default ? existing.LastSeenUtc : existingStart;
            if (!TemporalWindowsOverlap(incomingStart, incomingEnd, existingStart, existingEnd))
            {
                continue;
            }

            if (singleMatch is not null)
            {
                return null;
            }

            singleMatch = existing.FlowCorrelationKey;
        }

        return singleMatch;
    }

    private static bool TemporalWindowsOverlap(
        DateTimeOffset aStart,
        DateTimeOffset aEnd,
        DateTimeOffset bStart,
        DateTimeOffset bEnd)
    {
        TimeSpan window = RoutingTraceFlowCorrelation.WeakTemporalMergeWindow;
        DateTimeOffset aLo = aStart - window;
        DateTimeOffset aHi = aEnd + window;
        return bStart <= aHi && bStart >= aLo || bEnd <= aHi && bEnd >= aLo || (bStart <= aStart && bEnd >= aEnd);
    }

    private void RegisterAliases(string canonical, string primaryKey, string weakKey, IReadOnlyList<string> aliases)
    {
        _aliasToCanonical[canonical] = canonical;
        if (!string.IsNullOrWhiteSpace(primaryKey))
        {
            _aliasToCanonical[primaryKey] = canonical;
        }

        if (!string.IsNullOrWhiteSpace(weakKey))
        {
            _aliasToCanonical[weakKey] = canonical;
        }

        foreach (string alias in aliases)
        {
            if (!string.IsNullOrWhiteSpace(alias))
            {
                _aliasToCanonical[alias] = canonical;
            }
        }
    }

    private static DateTimeOffset ResolveLastSeenUtc(RoutingTraceEvent existing, RoutingTraceEvent draft)
    {
        DateTimeOffset candidate = draft.LastSeenUtc != default ? draft.LastSeenUtc : draft.Timestamp;
        if (candidate == default)
        {
            return existing.LastSeenUtc;
        }

        return candidate >= existing.LastSeenUtc ? candidate : existing.LastSeenUtc;
    }

    private static RoutingTraceEvent MergeEvents(RoutingTraceEvent existing, RoutingTraceEvent incoming)
    {
        if (RoutingTraceEventMerger.TryMergeIntoExisting(existing, incoming, out RoutingTraceEvent merged))
        {
            return merged with
            {
                LocalAddress = string.IsNullOrWhiteSpace(existing.LocalAddress) ? incoming.LocalAddress : existing.LocalAddress,
                LocalPort = existing.LocalPort > 0 ? existing.LocalPort : incoming.LocalPort,
            };
        }

        return incoming;
    }
}
