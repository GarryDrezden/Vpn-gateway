using SelectiveVpnRouter.Core;
using SelectiveVpnRouter.Core.RoutingTrace;
using SelectiveVpnRouter.Network;
using SelectiveVpnRouter.Proxy;

namespace SelectiveVpnRouter.Service;

public sealed class RoutingTraceHost : IDisposable
{
    private sealed record CompletedRoutingTraceState(
        RoutingTraceSession Session,
        IReadOnlyList<RoutingTraceEvent> Flows,
        RoutingTraceRingBuffer EventLog);

    private readonly object _gate = new();
    private readonly IPackagedApplicationPathResolver _packagedResolver;
    private RoutingTraceSession? _activeSession;
    private RoutingTraceLogicalFlowStore? _flowStore;
    private RoutingTraceRingBuffer? _eventStream;
    private RoutingTraceProcessScope? _scope;
    private RoutingTraceBaselineStore? _baseline;
    private CompletedRoutingTraceState? _lastCompleted;
    private Timer? _pollTimer;
    private Action<FlowEvent>? _flowHandler;
    private TransparentTcpProxy? _subscribedProxy;
    private Func<AppConfiguration>? _configProvider;

    public RoutingTraceHost(IPackagedApplicationPathResolver packagedResolver)
    {
        _packagedResolver = packagedResolver;
    }

    public void BindConfigProvider(Func<AppConfiguration> configProvider) => _configProvider = configProvider;

    public RoutingTraceStatus GetStatus()
    {
        lock (_gate)
        {
            return new RoutingTraceStatus
            {
                Active = _activeSession is { State: RoutingTraceSessionState.Running },
                ActiveSessionId = _activeSession?.SessionId,
                ActiveStartedAt = _activeSession?.StartedAt,
                ActiveTargetName = _activeSession?.Target.DisplayName,
                HasCompletedSession = _lastCompleted is not null,
                CompletedSessionId = _lastCompleted?.Session.SessionId,
                CompletedStartedAt = _lastCompleted?.Session.StartedAt,
                CompletedStoppedAt = _lastCompleted?.Session.StoppedAt,
                CompletedTargetName = _lastCompleted?.Session.Target.DisplayName,
            };
        }
    }

    public RoutingTraceSession Start(StartRoutingTraceRequest request, TransparentTcpProxy? proxy)
    {
        lock (_gate)
        {
            StopInternal(proxy, finalize: false);
            _lastCompleted = null;

            AppConfiguration config = _configProvider?.Invoke() ?? new AppConfiguration();
            RoutingTraceOptions options = request.Options ?? new RoutingTraceOptions();
            RoutingTraceTarget target = RoutingTraceTargetFactory.Resolve(request, config, _packagedResolver);
            _scope = new RoutingTraceProcessScope(target, options.FollowChildProcesses, options.IncludePackagedHelpers);
            _flowStore = new RoutingTraceLogicalFlowStore { MaxFlows = Math.Clamp(options.MaxEvents, 1000, 20_000) };
            _baseline = new RoutingTraceBaselineStore();
            _eventStream = new RoutingTraceRingBuffer(Math.Clamp(options.MaxEvents, 1000, 20_000));

            var session = new RoutingTraceSession
            {
                SessionId = Guid.NewGuid(),
                StartedAt = DateTimeOffset.UtcNow,
                State = RoutingTraceSessionState.Running,
                Target = target,
                Options = options,
            };
            _activeSession = session;
            SeedRunningProcesses();
            CaptureTraceStartBaseline(options);
            SubscribeProxy(proxy);
            _pollTimer = new Timer(_ => PollSafe(proxy), null, TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(400));
            return session;
        }
    }

    public RoutingTraceSession? Stop(TransparentTcpProxy? proxy)
    {
        lock (_gate)
        {
            return StopInternal(proxy, finalize: true);
        }
    }

    public bool TryClear(out string? errorCode)
    {
        lock (_gate)
        {
            if (_activeSession is { State: RoutingTraceSessionState.Running })
            {
                errorCode = "trace_active";
                return false;
            }

            _lastCompleted = null;
            errorCode = null;
            return true;
        }
    }

    public RoutingTraceSnapshot GetSnapshot(int recentLimit = 80)
    {
        lock (_gate)
        {
            RoutingTraceSession? session = _activeSession ?? _lastCompleted?.Session;
            IReadOnlyList<RoutingTraceEvent>? allFlows = GetCurrentFlowsLocked();
            if (session is null || allFlows is null)
            {
                return new RoutingTraceSnapshot();
            }

            IReadOnlyList<RoutingTraceEvent> recent = allFlows
                .OrderBy(e => e.SequenceId)
                .TakeLast(recentLimit)
                .ToArray();

            TimeSpan duration = (session.StoppedAt ?? DateTimeOffset.UtcNow) - session.StartedAt;
            RoutingTraceSummary summary = RoutingTraceSummaryBuilder.Build(session.Target, duration, allFlows);
            RoutingTraceLiveCounters live = RoutingTraceSummaryBuilder.BuildLiveCounters(allFlows);

            int dropped = (_flowStore?.DroppedEventCount ?? 0) + (_eventStream?.DroppedEventCount ?? _lastCompleted?.EventLog.DroppedEventCount ?? 0);
            long lastSeq = _eventStream?.LastSequenceId ?? _lastCompleted?.EventLog.LastSequenceId ?? allFlows.Max(e => e.SequenceId);

            RoutingTraceSession updated = session with
            {
                Summary = summary,
                LiveCounters = live,
                DroppedEventCount = dropped,
                LastEventSequenceId = lastSeq,
            };

            if (_activeSession is not null)
            {
                _activeSession = updated;
            }
            else if (_lastCompleted is not null)
            {
                _lastCompleted = _lastCompleted with { Session = updated };
            }

            return new RoutingTraceSnapshot { Session = updated, RecentEvents = recent };
        }
    }

    public RoutingTraceEventsPage GetEvents(GetRoutingTraceEventsRequest request)
    {
        lock (_gate)
        {
            RoutingTraceRingBuffer? log = null;
            Guid? boundSessionId = null;

            if (_activeSession is not null && _activeSession.SessionId == request.SessionId)
            {
                log = _eventStream;
                boundSessionId = _activeSession.SessionId;
            }
            else if (_lastCompleted is not null && _lastCompleted.Session.SessionId == request.SessionId)
            {
                log = _lastCompleted.EventLog;
                boundSessionId = _lastCompleted.Session.SessionId;
            }

            if (log is null || boundSessionId is null)
            {
                return new RoutingTraceEventsPage
                {
                    SessionId = request.SessionId,
                    LastSequenceId = request.AfterSequence,
                    SessionFound = false,
                };
            }

            IReadOnlyList<RoutingTraceEvent> ordered = log.SnapshotOrdered();
            List<RoutingTraceEvent> page = ordered
                .Where(e => e.SequenceId > request.AfterSequence)
                .Take(Math.Max(1, request.Limit))
                .ToList();
            long last = page.Count > 0 ? page[^1].SequenceId : request.AfterSequence;
            bool hasMore = ordered.Any(e => e.SequenceId > last);
            return new RoutingTraceEventsPage
            {
                SessionId = boundSessionId.Value,
                Events = page,
                LastSequenceId = last,
                HasMore = hasMore,
                SessionFound = true,
            };
        }
    }

    public bool TryGetExportData(out RoutingTraceSession? session, out IReadOnlyList<RoutingTraceEvent> events)
    {
        lock (_gate)
        {
            IReadOnlyList<RoutingTraceEvent>? flows = GetCurrentFlowsLocked();
            session = _activeSession ?? _lastCompleted?.Session;
            if (session is null || flows is null)
            {
                events = Array.Empty<RoutingTraceEvent>();
                return false;
            }

            events = flows;
            return true;
        }
    }

    private IReadOnlyList<RoutingTraceEvent>? GetCurrentFlowsLocked()
    {
        if (_flowStore is not null)
        {
            return _flowStore.SnapshotOrdered();
        }

        return _lastCompleted?.Flows;
    }

    private RoutingTraceSession? StopInternal(TransparentTcpProxy? proxy, bool finalize)
    {
        _pollTimer?.Dispose();
        _pollTimer = null;
        UnsubscribeProxy(proxy);

        RoutingTraceSession? session = _activeSession;
        if (session is null)
        {
            return null;
        }

        if (finalize)
        {
            IReadOnlyList<RoutingTraceEvent> all = _flowStore?.SnapshotOrdered() ?? Array.Empty<RoutingTraceEvent>();
            TimeSpan duration = DateTimeOffset.UtcNow - session.StartedAt;
            RoutingTraceSummary summary = RoutingTraceSummaryBuilder.Build(session.Target, duration, all);
            session = session with
            {
                StoppedAt = DateTimeOffset.UtcNow,
                State = RoutingTraceSessionState.Completed,
                Summary = summary,
                DroppedEventCount = (_flowStore?.DroppedEventCount ?? 0) + (_eventStream?.DroppedEventCount ?? 0),
                LastEventSequenceId = _eventStream?.LastSequenceId ?? (all.Count > 0 ? all.Max(e => e.SequenceId) : 0),
                LiveCounters = RoutingTraceSummaryBuilder.BuildLiveCounters(all),
            };

            _lastCompleted = new CompletedRoutingTraceState(
                session,
                all.ToArray(),
                _eventStream ?? new RoutingTraceRingBuffer(1));
        }

        _activeSession = null;
        _flowStore = null;
        _eventStream = null;
        _scope = null;
        _baseline = null;

        return finalize ? session : null;
    }

    private void SubscribeProxy(TransparentTcpProxy? proxy)
    {
        if (proxy is null)
        {
            return;
        }

        _subscribedProxy = proxy;
        _flowHandler = flow => IngestProxyFlow(flow);
        proxy.FlowChanged += _flowHandler;
    }

    private void UnsubscribeProxy(TransparentTcpProxy? proxy)
    {
        if (_subscribedProxy is not null && _flowHandler is not null)
        {
            _subscribedProxy.FlowChanged -= _flowHandler;
        }

        _subscribedProxy = null;
        _flowHandler = null;
    }


    private void CaptureTraceStartBaseline(RoutingTraceOptions opt)
    {
        if (_scope is null || _baseline is null || _activeSession is null)
        {
            return;
        }

        IngestBaselineTable(IpOwnerTables.SnapshotTcp(RoutingTraceAddressFamily.IPv4), opt);
        if (opt.IncludeIpv6)
        {
            IngestBaselineTable(IpOwnerTables.SnapshotTcp(RoutingTraceAddressFamily.IPv6), opt);
        }

        if (opt.IncludeUdp)
        {
            IngestBaselineTable(IpOwnerTables.SnapshotUdp(RoutingTraceAddressFamily.IPv4), opt);
            if (opt.IncludeIpv6)
            {
                IngestBaselineTable(IpOwnerTables.SnapshotUdp(RoutingTraceAddressFamily.IPv6), opt);
            }
        }
    }

    private void IngestBaselineTable(IReadOnlyList<PassiveSocketObservation> observations, RoutingTraceOptions opt)
    {
        foreach (PassiveSocketObservation obs in observations)
        {
            if (obs.RemotePort == 0 && obs.Protocol == RoutingTraceProtocol.Udp)
            {
                continue;
            }

            if (!opt.IncludeLoopback && RoutingTraceDestinationClassifier.Classify(obs.RemoteAddress, obs.AddressFamily) == RoutingTraceDestinationKind.Loopback)
            {
                continue;
            }

            string? path = ProcessPathResolver.TryGetPath(obs.Pid);
            if (_scope!.ShouldIncludeObservation(obs.Pid, path, out _))
            {
                long ticks = GetProcessStartTicks(obs.Pid);
                (string primaryKey, string weakKey) = RoutingTraceFlowCorrelation.FromPassiveObservation(obs, ticks);
                _baseline!.Add(primaryKey, weakKey);
            }
        }
    }

    private void PollSafe(TransparentTcpProxy? proxy)
    {
        try
        {
            Poll(proxy);
        }
        catch (Exception)
        {
        }
    }

    private void Poll(TransparentTcpProxy? proxy)
    {
        lock (_gate)
        {
            if (_activeSession?.State != RoutingTraceSessionState.Running || _scope is null || _flowStore is null)
            {
                return;
            }

            RefreshProcessScope();
            RoutingTraceOptions opt = _activeSession.Options;
            IngestTable(IpOwnerTables.SnapshotTcp(RoutingTraceAddressFamily.IPv4), opt);
            if (opt.IncludeIpv6)
            {
                IngestTable(IpOwnerTables.SnapshotTcp(RoutingTraceAddressFamily.IPv6), opt);
            }

            if (opt.IncludeUdp)
            {
                IngestTable(IpOwnerTables.SnapshotUdp(RoutingTraceAddressFamily.IPv4), opt);
                if (opt.IncludeIpv6)
                {
                    IngestTable(IpOwnerTables.SnapshotUdp(RoutingTraceAddressFamily.IPv6), opt);
                }
            }

            if (proxy is not null)
            {
                foreach (FlowEvent flow in proxy.Flows)
                {
                    IngestProxyFlowCore(flow);
                }
            }
        }
    }

    private void IngestTable(IReadOnlyList<PassiveSocketObservation> observations, RoutingTraceOptions opt)
    {
        foreach (PassiveSocketObservation obs in observations)
        {
            if (obs.RemotePort == 0 && obs.Protocol == RoutingTraceProtocol.Udp)
            {
                continue;
            }

            if (!opt.IncludeLoopback && RoutingTraceDestinationClassifier.Classify(obs.RemoteAddress, obs.AddressFamily) == RoutingTraceDestinationKind.Loopback)
            {
                continue;
            }

            string? path = ProcessPathResolver.TryGetPath(obs.Pid);
            if (!_scope!.ShouldIncludeObservation(obs.Pid, path, out RoutingTraceAttribution attribution))
            {
                continue;
            }

            attribution = attribution with { ProcessPath = path ?? attribution.ProcessPath };
            long startTicks = GetProcessStartTicks(obs.Pid);
            (string primaryKey, string weakKey) = RoutingTraceFlowCorrelation.FromPassiveObservation(obs, startTicks);
            RoutingTraceEvent draft = RoutingTraceSocketObservationMapper.FromPassiveObservation(
                obs,
                _activeSession!.Target,
                attribution,
                sequenceId: 0,
                processStartUtcTicks: startTicks);
            if (_baseline?.IsPreExisting(primaryKey, weakKey) == true)
            {
                draft = draft with { PreExistingAtTraceStart = true };
            }

            StoreEvent(draft, primaryKey, weakKey);
        }
    }

    private void IngestProxyFlow(FlowEvent flow)
    {
        lock (_gate)
        {
            IngestProxyFlowCore(flow);
        }
    }

    private void IngestProxyFlowCore(FlowEvent flow)
    {
        if (_activeSession?.State != RoutingTraceSessionState.Running || _scope is null || _flowStore is null)
        {
            return;
        }

        _scope.ObserveProcess(
            flow.Pid,
            null,
            flow.ProcessPath,
            default,
            RoutingTraceProcessStartSource.ConnectionTelemetry);
        if (!_scope.ShouldIncludeObservation(flow.Pid, flow.ProcessPath, out RoutingTraceAttribution attribution))
        {
            return;
        }

        attribution = attribution with { ProcessPath = flow.ProcessPath };
        long startTicks = GetProcessStartTicks(flow.Pid);
        (string primaryKey, string weakKey) = RoutingTraceFlowCorrelation.FromProxyFlow(flow, startTicks);
        RoutingTraceEvent draft = RoutingTraceFlowEventMapper.FromProxyFlow(
            flow,
            _activeSession.Target,
            attribution,
            sequenceId: 0,
            processStartUtcTicks: startTicks);
        if (_baseline?.IsPreExisting(primaryKey, weakKey) == true)
        {
            draft = draft with { PreExistingAtTraceStart = true };
        }

        StoreEvent(
            draft,
            primaryKey,
            weakKey,
            [RoutingTraceSourceAliases.ProxyFlow(flow.FlowId)]);
    }

    private long GetProcessStartTicks(int pid)
        => _scope!.TryGetProcessStartUtcTicks(pid, out long ticks) ? ticks : 0L;

    private void StoreEvent(
        RoutingTraceEvent draft,
        string primaryKey,
        string weakKey,
        IReadOnlyList<string>? sourceAliases = null)
    {
        if (_flowStore is null || _eventStream is null)
        {
            return;
        }

        RoutingTraceFlowUpsertResult result = _flowStore.Upsert(draft, primaryKey, weakKey, sourceAliases);
        if (result.Created || result.Updated)
        {
            _eventStream.Add(result.Event);
        }
    }

    private void SeedRunningProcesses()
    {
        foreach (System.Diagnostics.Process p in System.Diagnostics.Process.GetProcesses())
        {
            try
            {
                string? path = ProcessPathResolver.TryGetPath(p.Id);
                if (path is null)
                {
                    continue;
                }

                if (_scope!.ShouldIncludeObservation(p.Id, path, out _))
                {
                    _scope.ObserveProcess(p.Id, ProcessParentExtensions.TryGetParentProcessId(p), path, p.StartTime.ToUniversalTime());
                }
            }
            catch (Exception)
            {
            }
            finally
            {
                p.Dispose();
            }
        }
    }

    private void RefreshProcessScope()
    {
        foreach (System.Diagnostics.Process p in System.Diagnostics.Process.GetProcesses())
        {
            try
            {
                string? path = ProcessPathResolver.TryGetPath(p.Id);
                _scope!.ObserveProcess(p.Id, ProcessParentExtensions.TryGetParentProcessId(p), path, p.StartTime.ToUniversalTime());
            }
            catch (Exception)
            {
            }
            finally
            {
                p.Dispose();
            }
        }
    }

    public void Dispose()
    {
        _pollTimer?.Dispose();
    }
}

internal static class ProcessParentExtensions
{
    public static int? TryGetParentProcessId(System.Diagnostics.Process process)
    {
        try
        {
            System.Reflection.PropertyInfo? parentProp = typeof(System.Diagnostics.Process).GetProperty("Parent");
            if (parentProp?.GetValue(process) is System.Diagnostics.Process parent)
            {
                return parent.Id;
            }
        }
        catch (Exception)
        {
        }

        return null;
    }
}
