using SelectiveVpnRouter.Core;
using SelectiveVpnRouter.Core.RoutingTrace;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public sealed class RoutingTraceTests
{
    private static RoutingTraceTarget VpnTarget() => new()
    {
        DisplayName = "Probe",
        PrimaryExecutablePath = @"C:\Apps\Probe.exe",
        RuleId = Guid.NewGuid(),
        ExpectedRoute = RoutingTraceExpectedRoute.Vpn,
    };

    [Fact]
    public void Vpn_expected_full_proxy_evidence_observed_vpn()
    {
        var flow = new FlowEvent
        {
            Pid = 10,
            ProcessPath = @"C:\Apps\Probe.exe",
            Destination = "104.18.1.2",
            Port = 443,
            Route = FlowRoute.Vpn,
            WfpRedirect = true,
            ProxyAccepted = true,
            VpnOutboundBound = true,
            VpnOutboundConnected = true,
            Status = FlowLifecycle.Connected,
        };
        var attr = new RoutingTraceAttribution { InheritsTargetExpectedRoute = true, ProcessPath = flow.ProcessPath };
        RoutingTraceEvent e = RoutingTraceFlowEventMapper.FromProxyFlow(flow, VpnTarget(), attr, 1, DateTimeOffset.UtcNow.UtcTicks);
        Assert.Equal(RoutingTraceObservedRoute.Vpn, e.ObservedRoute);
        Assert.Equal(RoutingTraceOutcome.Connected, e.Outcome);
    }

    [Fact]
    public void Vpn_expected_proven_direct_is_leak()
    {
        var flow = new FlowEvent
        {
            Pid = 10,
            ProcessPath = @"C:\Apps\Probe.exe",
            Destination = "104.18.1.2",
            Port = 443,
            Route = FlowRoute.Direct,
            WfpRedirect = false,
            ProxyAccepted = false,
        };
        var attr = new RoutingTraceAttribution { InheritsTargetExpectedRoute = true, ProcessPath = flow.ProcessPath };
        RoutingTraceEvent e = RoutingTraceFlowEventMapper.FromProxyFlow(flow, VpnTarget(), attr, 1, DateTimeOffset.UtcNow.UtcTicks);
        Assert.Equal(RoutingTraceObservedRoute.Direct, e.ObservedRoute);
        Assert.True(RoutingTraceObservedRouteClassifier.IsConfirmedLeak(e.ExpectedRoute, e.ObservedRoute, e.DestinationKind));
    }

    [Fact]
    public void Vpn_expected_udp_passive_is_uncovered_not_leak()
    {
        var obs = new PassiveSocketObservation(
            10,
            RoutingTraceProtocol.Udp,
            RoutingTraceAddressFamily.IPv4,
            "0.0.0.0",
            52341,
            "172.64.2.1",
            443,
            "UDP",
            DateTimeOffset.UtcNow);
        var attr = new RoutingTraceAttribution { InheritsTargetExpectedRoute = true, ProcessPath = @"C:\Apps\Probe.exe" };
        RoutingTraceEvent e = RoutingTraceSocketObservationMapper.FromPassiveObservation(obs, VpnTarget(), attr, 1, DateTimeOffset.UtcNow.UtcTicks);
        Assert.Equal(RoutingTraceObservedRoute.Uncovered, e.ObservedRoute);
        Assert.False(RoutingTraceObservedRouteClassifier.IsConfirmedLeak(e.ExpectedRoute, e.ObservedRoute, e.DestinationKind));
    }

    [Fact]
    public void Vpn_expected_ipv6_passive_is_uncovered()
    {
        var obs = new PassiveSocketObservation(
            10,
            RoutingTraceProtocol.Tcp,
            RoutingTraceAddressFamily.IPv6,
            "::1",
            50000,
            "2606:4700::6812:102",
            443,
            "ESTABLISHED",
            DateTimeOffset.UtcNow);
        var attr = new RoutingTraceAttribution { InheritsTargetExpectedRoute = true, ProcessPath = @"C:\Apps\Probe.exe" };
        RoutingTraceEvent e = RoutingTraceSocketObservationMapper.FromPassiveObservation(obs, VpnTarget(), attr, 1, DateTimeOffset.UtcNow.UtcTicks);
        Assert.Equal(RoutingTraceObservedRoute.Uncovered, e.ObservedRoute);
        Assert.True(e.EvidenceFlags.HasFlag(RoutingTraceEvidenceFlags.Ipv6));
    }

    [Fact]
    public void Loopback_is_local_and_expected_local()
    {
        var flow = new FlowEvent
        {
            Pid = 10,
            ProcessPath = @"C:\Apps\Probe.exe",
            Destination = "127.0.0.1",
            Port = 8080,
            Route = FlowRoute.Vpn,
            WfpRedirect = true,
            ProxyAccepted = false,
        };
        var attr = new RoutingTraceAttribution { InheritsTargetExpectedRoute = true, ProcessPath = flow.ProcessPath };
        RoutingTraceEvent e = RoutingTraceFlowEventMapper.FromProxyFlow(flow, VpnTarget(), attr, 1, DateTimeOffset.UtcNow.UtcTicks);
        Assert.Equal(RoutingTraceExpectedRoute.Local, e.ExpectedRoute);
        Assert.Equal(RoutingTraceObservedRoute.Local, e.ObservedRoute);
    }

    [Fact]
    public void Packaged_helper_inherits_logical_identity()
    {
        var target = VpnTarget() with
        {
            AssociatedExecutablePaths = [@"C:\Program Files\WindowsApps\app\helper.exe"],
        };
        var attr = new RoutingTraceAttribution
        {
            ProcessPath = target.AssociatedExecutablePaths[0],
            IsPackagedHelper = true,
            InheritsTargetExpectedRoute = true,
            LogicalRuleId = target.RuleId,
            LogicalApplicationName = target.DisplayName,
        };
        var flow = new FlowEvent
        {
            Pid = 22,
            ProcessPath = target.AssociatedExecutablePaths[0],
            Destination = "1.1.1.1",
            Port = 443,
            Route = FlowRoute.Vpn,
            WfpRedirect = true,
            ProxyAccepted = true,
            VpnOutboundBound = true,
        };
        RoutingTraceEvent e = RoutingTraceFlowEventMapper.FromProxyFlow(flow, target, attr, 1, DateTimeOffset.UtcNow.UtcTicks);
        Assert.Equal(target.DisplayName, e.LogicalApplicationName);
        Assert.True(e.EvidenceFlags.HasFlag(RoutingTraceEvidenceFlags.PackagedHelper));
    }

    [Fact]
    public void Unassociated_child_finding_generated()
    {
        var events = new List<RoutingTraceEvent>
        {
            new()
            {
                SequenceId = 1,
                ProcessId = 50,
                ProcessPath = @"C:\Apps\webview.exe",
                RemoteAddress = "8.8.8.8",
                RemotePort = 443,
                DestinationKind = RoutingTraceDestinationKind.External,
                ExpectedRoute = RoutingTraceExpectedRoute.DefaultDirect,
                ObservedRoute = RoutingTraceObservedRoute.Uncovered,
                Protocol = RoutingTraceProtocol.Udp,
                AddressFamily = RoutingTraceAddressFamily.IPv4,
            },
        };
        RoutingTraceSummary summary = RoutingTraceSummaryBuilder.Build(VpnTarget(), TimeSpan.FromMinutes(1), events);
        Assert.Contains(summary.Findings, f => f.Kind == RoutingTraceFindingKind.UnassociatedProcess);
    }

    [Fact]
    public void Pid_reuse_does_not_merge_different_start_times()
    {
        var scope = new RoutingTraceProcessScope(VpnTarget(), followChildren: true, includePackagedHelpers: true);
        scope.ObserveProcess(100, 1, @"C:\Apps\Probe.exe", new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero));
        scope.ObserveProcess(100, 1, @"C:\Apps\Other.exe", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        Assert.True(scope.ShouldIncludeObservation(100, @"C:\Apps\Other.exe", out _));
    }

    [Fact]
    public void Duplicate_telemetry_merges()
    {
        var a = new RoutingTraceEvent
        {
            SequenceId = 1,
            FlowCorrelationKey = "k",
            RemoteAddress = "1.1.1.1",
            RemotePort = 443,
            ObservedRoute = RoutingTraceObservedRoute.Unknown,
            EvidenceFlags = RoutingTraceEvidenceFlags.PassiveSocketTable,
        };
        var b = a with
        {
            ObservedRoute = RoutingTraceObservedRoute.Vpn,
            EvidenceFlags = RoutingTraceEvidenceFlags.ProxyAccepted | RoutingTraceEvidenceFlags.VpnBound,
        };
        Assert.True(RoutingTraceEventMerger.TryMergeIntoExisting(a, b, out RoutingTraceEvent merged));
        Assert.Equal(RoutingTraceObservedRoute.Vpn, merged.ObservedRoute);
    }

    [Fact]
    public void Udp443_sets_quic_candidate()
    {
        var obs = new PassiveSocketObservation(1, RoutingTraceProtocol.Udp, RoutingTraceAddressFamily.IPv4, "0.0.0.0", 443, "1.1.1.1", 443, "UDP", DateTimeOffset.UtcNow);
        var attr = new RoutingTraceAttribution { InheritsTargetExpectedRoute = true, ProcessPath = @"C:\Apps\Probe.exe" };
        RoutingTraceEvent e = RoutingTraceSocketObservationMapper.FromPassiveObservation(obs, VpnTarget(), attr, 1, DateTimeOffset.UtcNow.UtcTicks);
        Assert.True(e.EvidenceFlags.HasFlag(RoutingTraceEvidenceFlags.QuicCandidate));
    }

    [Fact]
    public void Unknown_is_not_counted_as_direct_in_quality()
    {
        var events = new List<RoutingTraceEvent>
        {
            new()
            {
                SequenceId = 1,
                RemoteAddress = "1.1.1.1",
                RemotePort = 443,
                DestinationKind = RoutingTraceDestinationKind.External,
                ExpectedRoute = RoutingTraceExpectedRoute.Vpn,
                ObservedRoute = RoutingTraceObservedRoute.Unknown,
                Protocol = RoutingTraceProtocol.Tcp,
                AddressFamily = RoutingTraceAddressFamily.IPv4,
                EvidenceFlags = RoutingTraceEvidenceFlags.PassiveSocketTable,
            },
        };
        RoutingTraceSummary summary = RoutingTraceSummaryBuilder.Build(VpnTarget(), TimeSpan.FromSeconds(1), events);
        Assert.Equal(1, summary.Quality.UnknownFlows);
        Assert.Equal(0, summary.ProtocolLines[0].Direct);
    }

    [Fact]
    public void Ring_buffer_drops_and_counts()
    {
        var ring = new RoutingTraceRingBuffer(2);
        ring.Add(new RoutingTraceEvent { RemoteAddress = "1.1.1.1", RemotePort = 1 });
        ring.Add(new RoutingTraceEvent { RemoteAddress = "1.1.1.1", RemotePort = 2 });
        ring.Add(new RoutingTraceEvent { RemoteAddress = "1.1.1.1", RemotePort = 3 });
        Assert.Equal(1, ring.DroppedEventCount);
        Assert.Equal(3, ring.LastSequenceId);
    }

    [Fact]
    public void Pagination_sequence_id()
    {
        var ring = new RoutingTraceRingBuffer(10);
        ring.Add(new RoutingTraceEvent { RemoteAddress = "1", RemotePort = 1 });
        ring.Add(new RoutingTraceEvent { RemoteAddress = "2", RemotePort = 2 });
        RoutingTraceEventsPage page = ring.GetPage(1, 10);
        Assert.Single(page.Events);
        Assert.Equal(2, page.LastSequenceId);
    }

    [Fact]
    public void Json_export_redacts_token_like_values()
    {
        var session = new RoutingTraceSession
        {
            SessionId = Guid.NewGuid(),
            StartedAt = DateTimeOffset.UtcNow,
            State = RoutingTraceSessionState.Completed,
            Target = VpnTarget(),
        };
        string json = RoutingTraceExport.ToRedactedJson(session, []);
        string redacted = RoutingTraceExport.Redact("authorization: Bearer abcdef123456");
        Assert.Contains("[REDACTED]", redacted);
        Assert.DoesNotContain("abcdef123456", redacted);
        Assert.Contains("Probe", json);
    }

    [Fact]
    public void Unicode_paths_normalize_in_target_factory()
    {
        string unicode = @"C:\Users\Тест\App\app.exe";
        var rule = RoutingRule.Create(RuleType.Application, "Unicode", unicode, RouteMode.Vpn);
        RoutingTraceTarget target = RoutingTraceTargetFactory.FromRule(rule, packagedResolver: null);
        Assert.Equal(ApplicationRulesHelper.NormalizeExePath(unicode), target.PrimaryExecutablePath);
    }

    [Fact]
    public void LogicalFlowStore_repeated_tcp_snapshot_is_one_flow()
    {
        var store = new RoutingTraceLogicalFlowStore();
        long ticks = DateTimeOffset.UtcNow.UtcTicks;
        var obs = new PassiveSocketObservation(10, RoutingTraceProtocol.Tcp, RoutingTraceAddressFamily.IPv4, "10.0.0.1", 50000, "104.18.19.125", 443, "ESTABLISHED", DateTimeOffset.UtcNow);
        var attr = new RoutingTraceAttribution { ProcessPath = @"C:\Apps\Probe.exe", InheritsTargetExpectedRoute = true };
        (string primary, string weak) = RoutingTraceFlowCorrelation.FromPassiveObservation(obs, ticks);
        RoutingTraceEvent draft = RoutingTraceSocketObservationMapper.FromPassiveObservation(obs, VpnTarget(), attr, 0, ticks);
        Assert.True(store.Upsert(draft, primary, weak).Created);
        Assert.False(store.Upsert(draft, primary, weak).Created);
        Assert.Equal(1, store.SnapshotOrdered().Count);
    }

    [Fact]
    public void LogicalFlowStore_proxy_and_table_merge_to_one_flow()
    {
        var store = new RoutingTraceLogicalFlowStore();
        long ticks = DateTimeOffset.UtcNow.UtcTicks;
        var obs = new PassiveSocketObservation(10, RoutingTraceProtocol.Tcp, RoutingTraceAddressFamily.IPv4, "10.0.0.1", 50000, "104.18.19.125", 443, "ESTABLISHED", DateTimeOffset.UtcNow);
        var attr = new RoutingTraceAttribution { ProcessPath = @"C:\Apps\Probe.exe", InheritsTargetExpectedRoute = true };
        (string pKey, string wKey) = RoutingTraceFlowCorrelation.FromPassiveObservation(obs, ticks);
        RoutingTraceEvent passive = RoutingTraceSocketObservationMapper.FromPassiveObservation(obs, VpnTarget(), attr, 0, ticks);
        store.Upsert(passive, pKey, wKey);

        var flow = new FlowEvent
        {
            Pid = 10,
            ProcessPath = @"C:\Apps\Probe.exe",
            Destination = "104.18.19.125",
            Port = 443,
            Route = FlowRoute.Vpn,
            WfpRedirect = true,
            ProxyAccepted = true,
            VpnOutboundBound = true,
            VpnOutboundConnected = true,
            Status = FlowLifecycle.Connected,
            OutboundLocalEndpoint = "10.0.0.1:50000",
        };
        (string pp, string pw) = RoutingTraceFlowCorrelation.FromProxyFlow(flow, ticks);
        RoutingTraceEvent proxyEvent = RoutingTraceFlowEventMapper.FromProxyFlow(flow, VpnTarget(), attr, 0, ticks);
        var result = store.Upsert(proxyEvent, pp, pw);
        Assert.True(result.Updated || result.Created);
        Assert.Equal(1, store.SnapshotOrdered().Count);
        Assert.Equal(RoutingTraceObservedRoute.Vpn, store.SnapshotOrdered()[0].ObservedRoute);
    }

    [Fact]
    public void LogicalFlowStore_different_local_ports_are_two_flows()
    {
        var store = new RoutingTraceLogicalFlowStore();
        long ticks = DateTimeOffset.UtcNow.UtcTicks;
        var attr = new RoutingTraceAttribution { ProcessPath = @"C:\Apps\Probe.exe", InheritsTargetExpectedRoute = true };
        var obs1 = new PassiveSocketObservation(10, RoutingTraceProtocol.Tcp, RoutingTraceAddressFamily.IPv4, "10.0.0.1", 50001, "104.18.19.125", 443, "ESTABLISHED", DateTimeOffset.UtcNow);
        var obs2 = obs1 with { LocalPort = 50002 };
        UpsertPassive(store, obs1, ticks, attr);
        UpsertPassive(store, obs2, ticks, attr);
        Assert.Equal(2, store.SnapshotOrdered().Count);
    }

    [Fact]
    public void LogicalFlowStore_pid_reuse_not_merged_when_start_ticks_differ()
    {
        var store = new RoutingTraceLogicalFlowStore();
        var attr = new RoutingTraceAttribution { ProcessPath = @"C:\Apps\Probe.exe", InheritsTargetExpectedRoute = true };
        long ticks1 = DateTimeOffset.UtcNow.AddHours(-1).UtcTicks;
        long ticks2 = DateTimeOffset.UtcNow.UtcTicks;
        var obs1 = new PassiveSocketObservation(10, RoutingTraceProtocol.Tcp, RoutingTraceAddressFamily.IPv4, "10.0.0.1", 50001, "104.18.19.125", 443, "ESTABLISHED", DateTimeOffset.UtcNow);
        var obs2 = obs1 with { LocalPort = 50002 };
        UpsertPassive(store, obs1, ticks1, attr);
        UpsertPassive(store, obs2, ticks2, attr);
        Assert.Equal(2, store.SnapshotOrdered().Count);
    }

    [Fact]
    public void LogicalFlowStore_update_bumps_sequence_for_ui()
    {
        var store = new RoutingTraceLogicalFlowStore();
        long ticks = DateTimeOffset.UtcNow.UtcTicks;
        var attr = new RoutingTraceAttribution { ProcessPath = @"C:\Apps\Probe.exe", InheritsTargetExpectedRoute = true };
        var obs = new PassiveSocketObservation(10, RoutingTraceProtocol.Tcp, RoutingTraceAddressFamily.IPv4, "10.0.0.1", 50000, "104.18.19.125", 443, "ESTABLISHED", DateTimeOffset.UtcNow);
        UpsertPassive(store, obs, ticks, attr);
        long firstSeq = store.SnapshotOrdered()[0].SequenceId;
        var flow = new FlowEvent
        {
            Pid = 10,
            ProcessPath = @"C:\Apps\Probe.exe",
            Destination = "104.18.19.125",
            Port = 443,
            Route = FlowRoute.Vpn,
            WfpRedirect = true,
            ProxyAccepted = true,
            VpnOutboundBound = true,
            VpnOutboundConnected = true,
            Status = FlowLifecycle.Connected,
            OutboundLocalEndpoint = "10.0.0.1:50000",
        };
        (string pp, string pw) = RoutingTraceFlowCorrelation.FromProxyFlow(flow, ticks);
        RoutingTraceEvent proxyEvent = RoutingTraceFlowEventMapper.FromProxyFlow(flow, VpnTarget(), attr, 0, ticks);
        store.Upsert(proxyEvent, pp, pw);
        long secondSeq = store.SnapshotOrdered()[0].SequenceId;
        Assert.True(secondSeq > firstSeq);
    }

    [Fact]
    public void Summary_counts_logical_flows_not_duplicate_observations()
    {
        var store = new RoutingTraceLogicalFlowStore();
        long ticks = DateTimeOffset.UtcNow.UtcTicks;
        var attr = new RoutingTraceAttribution { ProcessPath = @"C:\Apps\Probe.exe", InheritsTargetExpectedRoute = true };
        var obs = new PassiveSocketObservation(10, RoutingTraceProtocol.Tcp, RoutingTraceAddressFamily.IPv4, "10.0.0.1", 50000, "104.18.19.125", 443, "ESTABLISHED", DateTimeOffset.UtcNow);
        UpsertPassive(store, obs, ticks, attr);
        UpsertPassive(store, obs, ticks, attr);
        var flow = new FlowEvent
        {
            Pid = 10,
            ProcessPath = @"C:\Apps\Probe.exe",
            Destination = "104.18.19.125",
            Port = 443,
            Route = FlowRoute.Vpn,
            WfpRedirect = true,
            ProxyAccepted = true,
            VpnOutboundBound = true,
            VpnOutboundConnected = true,
            Status = FlowLifecycle.Connected,
            OutboundLocalEndpoint = "10.0.0.1:50000",
        };
        (string pp, string pw) = RoutingTraceFlowCorrelation.FromProxyFlow(flow, ticks);
        store.Upsert(RoutingTraceFlowEventMapper.FromProxyFlow(flow, VpnTarget(), attr, 0, ticks), pp, pw);
        var summary = RoutingTraceSummaryBuilder.Build(VpnTarget(), TimeSpan.FromMinutes(1), store.SnapshotOrdered());
        Assert.Equal(1, summary.TotalEvents);
        Assert.Equal(1, RoutingTraceSummaryBuilder.BuildLiveCounters(store.SnapshotOrdered()).TcpIpv4Vpn);
    }


    private static RoutingTraceEvent BaseVpnTcp4(
        bool preExisting = false,
        RoutingTraceObservedRoute observed = RoutingTraceObservedRoute.Unknown,
        RoutingTraceEvidenceFlags flags = RoutingTraceEvidenceFlags.PassiveSocketTable,
        RoutingTraceProtocol protocol = RoutingTraceProtocol.Tcp,
        RoutingTraceAddressFamily family = RoutingTraceAddressFamily.IPv4,
        int localPort = 50000)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new RoutingTraceEvent
        {
            ExpectedRoute = RoutingTraceExpectedRoute.Vpn,
            ObservedRoute = observed,
            Protocol = protocol,
            AddressFamily = family,
            DestinationKind = RoutingTraceDestinationKind.External,
            PreExistingAtTraceStart = preExisting,
            EvidenceFlags = flags,
            LocalAddress = "10.0.0.1",
            LocalPort = localPort,
            RemoteAddress = "18.205.74.143",
            RemotePort = 443,
            FirstSeenUtc = now,
            LastSeenUtc = now,
        };
    }

    private static RoutingTraceEvent Classify(RoutingTraceEvent draft) =>
        RoutingTraceCoverageClassifier.Apply(draft);

    [Fact]
    public void V12_pre_existing_tcp4_classified_historical_not_uncovered_gap()
    {
        RoutingTraceEvent e = Classify(BaseVpnTcp4(preExisting: true));
        Assert.Equal(RoutingTraceObservedRoute.Unknown, e.ObservedRoute);
        Assert.Equal(RoutingTraceCoverageReason.PreExistingAtTraceStart, e.CoverageReason);
        Assert.False(RoutingTraceObservedRouteClassifier.IsConfirmedLeak(e.ExpectedRoute, e.ObservedRoute, e.DestinationKind));
    }

    [Fact]
    public void V12_pre_existing_tcp4_without_proxy_is_not_leak()
    {
        RoutingTraceEvent e = Classify(BaseVpnTcp4(preExisting: true, flags: RoutingTraceEvidenceFlags.PassiveSocketTable));
        Assert.NotEqual(RoutingTraceObservedRoute.Uncovered, e.ObservedRoute);
        Assert.NotEqual(RoutingTraceObservedRoute.Direct, e.ObservedRoute);
    }

    [Fact]
    public void V12_new_tcp4_no_proxy_evidence_missing_proxy_not_uncovered()
    {
        RoutingTraceEvent e = Classify(BaseVpnTcp4(preExisting: false, flags: RoutingTraceEvidenceFlags.PassiveSocketTable));
        Assert.Equal(RoutingTraceObservedRoute.Unknown, e.ObservedRoute);
        Assert.Equal(RoutingTraceCoverageReason.MissingProxyEvidence, e.CoverageReason);
    }

    [Fact]
    public void V12_new_tcp4_vpn_evidence_observed_vpn()
    {
        RoutingTraceEvidenceFlags flags =
            RoutingTraceEvidenceFlags.PassiveSocketTable
            | RoutingTraceEvidenceFlags.WfpRedirectApplied
            | RoutingTraceEvidenceFlags.ProxyAccepted
            | RoutingTraceEvidenceFlags.VpnBound;
        RoutingTraceEvent e = Classify(BaseVpnTcp4(flags: flags));
        Assert.Equal(RoutingTraceObservedRoute.Vpn, e.ObservedRoute);
        Assert.Equal(RoutingTraceCoverageReason.None, e.CoverageReason);
    }

    [Fact]
    public void V12_new_tcp4_direct_proof_is_leak()
    {
        RoutingTraceEvent e = Classify(BaseVpnTcp4(flags: RoutingTraceEvidenceFlags.DirectObserved));
        Assert.Equal(RoutingTraceObservedRoute.Direct, e.ObservedRoute);
        Assert.Equal(RoutingTraceCoverageReason.ProvenDirect, e.CoverageReason);
        Assert.True(RoutingTraceObservedRouteClassifier.IsConfirmedLeak(e.ExpectedRoute, e.ObservedRoute, e.DestinationKind));
    }

    [Fact]
    public void V12_udp_expected_vpn_uncovered_unsupported_protocol()
    {
        RoutingTraceEvent e = Classify(BaseVpnTcp4(protocol: RoutingTraceProtocol.Udp));
        Assert.Equal(RoutingTraceObservedRoute.Uncovered, e.ObservedRoute);
        Assert.Equal(RoutingTraceCoverageReason.UnsupportedProtocol, e.CoverageReason);
    }

    [Fact]
    public void V12_ipv6_expected_vpn_uncovered_unsupported_family()
    {
        RoutingTraceEvent e = Classify(BaseVpnTcp4(family: RoutingTraceAddressFamily.IPv6));
        Assert.Equal(RoutingTraceObservedRoute.Uncovered, e.ObservedRoute);
        Assert.Equal(RoutingTraceCoverageReason.UnsupportedAddressFamily, e.CoverageReason);
    }

    [Fact]
    public void V12_baseline_store_strong_key_marks_pre_existing()
    {
        var baseline = new RoutingTraceBaselineStore();
        baseline.Add("strong-key", "weak-key");
        Assert.True(baseline.IsPreExisting("strong-key", "weak-key"));
        Assert.False(baseline.IsPreExisting("other", "weak-key"));
    }

    [Fact]
    public void V12_same_remote_different_local_ports_are_two_flows()
    {
        var store = new RoutingTraceLogicalFlowStore();
        long ticks = DateTimeOffset.UtcNow.UtcTicks;
        var attr = new RoutingTraceAttribution { ProcessPath = @"C:\Apps\Probe.exe", InheritsTargetExpectedRoute = true };
        var obs1 = new PassiveSocketObservation(10, RoutingTraceProtocol.Tcp, RoutingTraceAddressFamily.IPv4, "10.0.0.1", 50001, "18.205.74.143", 443, "ESTABLISHED", DateTimeOffset.UtcNow);
        var obs2 = obs1 with { LocalPort = 50002 };
        UpsertPassive(store, obs1, ticks, attr);
        UpsertPassive(store, obs2, ticks, attr);
        Assert.Equal(2, store.SnapshotOrdered().Count);
        Assert.Equal(2, store.SnapshotOrdered().Select(e => e.FlowId).Distinct().Count());
    }

    [Fact]
    public void V12_summary_total_logical_flows_equals_distinct_flow_ids()
    {
        var store = new RoutingTraceLogicalFlowStore();
        long ticks = DateTimeOffset.UtcNow.UtcTicks;
        var attr = new RoutingTraceAttribution { ProcessPath = @"C:\Apps\Probe.exe", InheritsTargetExpectedRoute = true };
        var obs1 = new PassiveSocketObservation(10, RoutingTraceProtocol.Tcp, RoutingTraceAddressFamily.IPv4, "10.0.0.1", 50001, "18.205.74.143", 443, "ESTABLISHED", DateTimeOffset.UtcNow);
        var obs2 = obs1 with { LocalPort = 50002 };
        UpsertPassive(store, obs1, ticks, attr);
        UpsertPassive(store, obs2, ticks, attr);
        IReadOnlyList<RoutingTraceEvent> events = store.SnapshotOrdered();
        RoutingTraceSummary summary = RoutingTraceSummaryBuilder.Build(VpnTarget(), TimeSpan.FromMinutes(1), events);
        Assert.Equal(events.Select(e => e.FlowId).Distinct().Count(), summary.TotalLogicalFlows);
    }

    [Fact]
    public void V12_text_export_includes_flow_id_local_endpoint_and_evidence()
    {
        RoutingTraceEvent e = Classify(BaseVpnTcp4());
        e = e with { FlowId = Guid.Parse("9f2a1b3c-4d5e-6f70-8192-a1b2c3d4e5f6"), SequenceId = 1, ProcessId = 1234 };
        var session = new RoutingTraceSession
        {
            SessionId = Guid.NewGuid(),
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            StoppedAt = DateTimeOffset.UtcNow,
            State = RoutingTraceSessionState.Completed,
            Target = VpnTarget(),
            Summary = RoutingTraceSummaryBuilder.Build(VpnTarget(), TimeSpan.FromMinutes(1), [e]),
        };
        string text = RoutingTraceExport.FormatTextReport(session, [e]);
        Assert.Contains("flow=9f2a1b3c-4d5e-6f70-8192-a1b2c3d4e5f6", text);
        Assert.Contains("local=10.0.0.1:50000", text);
        Assert.Contains("remote=18.205.74.143:443", text);
        Assert.Contains("evidence=", text);
    }

    [Fact]
    public void V12_json_export_preserves_diagnostic_flow_fields()
    {
        RoutingTraceEvent e = Classify(BaseVpnTcp4(preExisting: true));
        e = e with { FlowId = Guid.NewGuid(), SequenceId = 2, ProcessId = 99, ProcessStartUtcTicks = DateTimeOffset.UtcNow.UtcTicks };
        var session = new RoutingTraceSession
        {
            SessionId = Guid.NewGuid(),
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            StoppedAt = DateTimeOffset.UtcNow,
            State = RoutingTraceSessionState.Completed,
            Target = VpnTarget(),
        };
        string json = RoutingTraceExport.ToRedactedJson(session, [e]);
        Assert.Contains("\"PreExistingAtTraceStart\": true", json);
        Assert.Contains("\"CoverageReason\": 1", json);
        Assert.Contains("10.0.0.1:50000", json);
        Assert.Contains("\"Flows\"", json);
    }

    [Fact]
    public void V12_completed_export_flow_ids_are_unique()
    {
        var store = new RoutingTraceLogicalFlowStore();
        long ticks = DateTimeOffset.UtcNow.UtcTicks;
        var attr = new RoutingTraceAttribution { ProcessPath = @"C:\Apps\Probe.exe", InheritsTargetExpectedRoute = true };
        var obs1 = new PassiveSocketObservation(10, RoutingTraceProtocol.Tcp, RoutingTraceAddressFamily.IPv4, "10.0.0.1", 50001, "18.205.74.143", 443, "ESTABLISHED", DateTimeOffset.UtcNow);
        var obs2 = obs1 with { LocalPort = 50002 };
        UpsertPassive(store, obs1, ticks, attr);
        UpsertPassive(store, obs2, ticks, attr);
        IReadOnlyList<RoutingTraceEvent> events = store.SnapshotOrdered();
        Assert.Equal(events.Count, events.Select(x => x.FlowId).Distinct().Count());
    }

    [Fact]
    public void V12_russian_ui_formatters_do_not_contain_question_marks()
    {
        string active = RoutingTraceUiText.FormatActiveMonitoring(TimeSpan.FromSeconds(40), "Grok Bot 0.63.0");
        string completed = RoutingTraceUiText.FormatCompletedReport(TimeSpan.FromSeconds(40), "Grok Bot 0.63.0");
        Assert.DoesNotContain('?', active);
        Assert.DoesNotContain('?', completed);
        Assert.Contains("Grok Bot 0.63.0", active);
        Assert.Contains("Grok Bot 0.63.0", completed);
    }

    [Fact]
    public void V12_findings_distinguish_historical_vs_missing_proxy()
    {
        RoutingTraceEvent historical = Classify(BaseVpnTcp4(preExisting: true));
        RoutingTraceEvent missingProxy = Classify(BaseVpnTcp4(preExisting: false, flags: RoutingTraceEvidenceFlags.PassiveSocketTable));
        historical = historical with { FlowId = Guid.NewGuid() };
        missingProxy = missingProxy with { FlowId = Guid.NewGuid() };
        RoutingTraceSummary summary = RoutingTraceSummaryBuilder.Build(VpnTarget(), TimeSpan.FromMinutes(1), [historical, missingProxy]);
        Assert.Contains(summary.Findings, f => f.Kind == RoutingTraceFindingKind.PreExistingHistorical && f.Count >= 1);
        Assert.Contains(summary.Findings, f => f.Kind == RoutingTraceFindingKind.MissingProxyEvidence && f.Count >= 1);
    }

    [Fact]
    public void V12_tcp4_quality_line_separates_historical_from_verified_vpn()
    {
        RoutingTraceEvidenceFlags vpnFlags =
            RoutingTraceEvidenceFlags.WfpRedirectApplied | RoutingTraceEvidenceFlags.ProxyAccepted | RoutingTraceEvidenceFlags.VpnBound;
        RoutingTraceEvent verified = Classify(BaseVpnTcp4(flags: vpnFlags, localPort: 50001));
        RoutingTraceEvent historical = Classify(BaseVpnTcp4(preExisting: true, localPort: 50002));
        verified = verified with { FlowId = Guid.NewGuid(), Outcome = RoutingTraceOutcome.Connected };
        historical = historical with { FlowId = Guid.NewGuid() };
        RoutingTraceSummary summary = RoutingTraceSummaryBuilder.Build(VpnTarget(), TimeSpan.FromMinutes(1), [verified, historical]);
        RoutingTraceProtocolQualityLine tcp4 = summary.ProtocolLines.Single(l => l.Label == "TCP/IPv4");
        Assert.True(tcp4.Vpn >= 1);
        Assert.True(tcp4.Historical >= 1);
    }



    [Fact]
    public void V13_connection_telemetry_does_not_overwrite_os_process_start()
    {
        var scope = new RoutingTraceProcessScope(VpnTarget(), followChildren: true, includePackagedHelpers: true);
        DateTimeOffset osStart = new DateTimeOffset(2026, 10, 4, 19, 27, 25, TimeSpan.Zero);
        scope.ObserveProcess(9952, null, @"C:\Apps\Grok.exe", osStart, RoutingTraceProcessStartSource.Os);
        DateTimeOffset fakeConnectionStart = new DateTimeOffset(2026, 10, 4, 21, 16, 21, TimeSpan.Zero);
        scope.ObserveProcess(9952, null, @"C:\Apps\Grok.exe", fakeConnectionStart, RoutingTraceProcessStartSource.ConnectionTelemetry);
        Assert.True(scope.TryGetProcessStartUtcTicks(9952, out long ticks));
        Assert.Equal(osStart.UtcTicks, ticks);
    }

    [Fact]
    public void V13_proxy_lifecycle_unknown_then_connected_one_flow_id()
    {
        var store = new RoutingTraceLogicalFlowStore();
        long ticks = DateTimeOffset.UtcNow.UtcTicks;
        var attr = new RoutingTraceAttribution { ProcessPath = @"C:\Apps\Probe.exe", InheritsTargetExpectedRoute = true };
        Guid proxyFlowId = Guid.NewGuid();
        var early = new FlowEvent
        {
            FlowId = proxyFlowId,
            Pid = 10,
            ProcessPath = @"C:\Apps\Probe.exe",
            Destination = "104.18.19.125",
            Port = 443,
            Route = FlowRoute.Vpn,
            WfpRedirect = true,
            ProxyAccepted = true,
            VpnOutboundBound = true,
            Status = FlowLifecycle.Accepted,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var late = early with
        {
            VpnOutboundConnected = true,
            Status = FlowLifecycle.Connected,
            UpdatedAt = DateTimeOffset.UtcNow.AddSeconds(2),
        };
        string alias = RoutingTraceSourceAliases.ProxyFlow(proxyFlowId);
        UpsertProxy(store, early, ticks, attr, alias);
        UpsertProxy(store, late, ticks, attr, alias);
        Assert.Single(store.SnapshotOrdered());
        RoutingTraceEvent flow = store.SnapshotOrdered()[0];
        Assert.Equal(RoutingTraceOutcome.Connected, flow.Outcome);
        Assert.Equal(proxyFlowId, flow.SourceProxyFlowId);
        Assert.True(flow.UpdateCount >= 1);
    }

    [Fact]
    public void V13_same_proxy_flow_id_stable_sequence_increments()
    {
        var store = new RoutingTraceLogicalFlowStore();
        long ticks = DateTimeOffset.UtcNow.UtcTicks;
        var attr = new RoutingTraceAttribution { ProcessPath = @"C:\Apps\Probe.exe", InheritsTargetExpectedRoute = true };
        Guid proxyFlowId = Guid.NewGuid();
        var flow = new FlowEvent
        {
            FlowId = proxyFlowId,
            Pid = 10,
            ProcessPath = @"C:\Apps\Probe.exe",
            Destination = "1.1.1.1",
            Port = 443,
            Route = FlowRoute.Vpn,
            WfpRedirect = true,
            ProxyAccepted = true,
            Status = FlowLifecycle.Accepted,
        };
        string alias = RoutingTraceSourceAliases.ProxyFlow(proxyFlowId);
        UpsertProxy(store, flow, ticks, attr, alias);
        Guid flowId = store.SnapshotOrdered()[0].FlowId;
        long seq1 = store.SnapshotOrdered()[0].SequenceId;
        UpsertProxy(store, flow with { Status = FlowLifecycle.Connected, VpnOutboundConnected = true }, ticks, attr, alias);
        RoutingTraceEvent updated = store.SnapshotOrdered()[0];
        Assert.Equal(flowId, updated.FlowId);
        Assert.True(updated.SequenceId > seq1);
    }

    [Fact]
    public void V13_pid_reuse_with_different_os_start_stays_separate_flows()
    {
        var scope = new RoutingTraceProcessScope(VpnTarget(), true, true);
        DateTimeOffset startA = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset startB = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
        scope.ObserveProcess(1234, null, @"C:\Apps\Probe.exe", startA, RoutingTraceProcessStartSource.Os);
        scope.TryGetProcessStartUtcTicks(1234, out long ticksA);
        scope.ObserveProcess(1234, null, @"C:\Apps\Probe.exe", startB, RoutingTraceProcessStartSource.Os);
        scope.TryGetProcessStartUtcTicks(1234, out long ticksB);
        Assert.NotEqual(ticksA, ticksB);
    }

    [Fact]
    public void V13_passive_then_proxy_same_tuple_one_flow()
    {
        var store = new RoutingTraceLogicalFlowStore();
        long ticks = DateTimeOffset.UtcNow.UtcTicks;
        var attr = new RoutingTraceAttribution { ProcessPath = @"C:\Apps\Probe.exe", InheritsTargetExpectedRoute = true };
        var obs = new PassiveSocketObservation(10, RoutingTraceProtocol.Tcp, RoutingTraceAddressFamily.IPv4, "10.0.0.1", 50100, "104.18.19.125", 443, "ESTABLISHED", DateTimeOffset.UtcNow);
        UpsertPassive(store, obs, ticks, attr);
        Guid proxyFlowId = Guid.NewGuid();
        var flow = new FlowEvent
        {
            FlowId = proxyFlowId,
            Pid = 10,
            ProcessPath = @"C:\Apps\Probe.exe",
            Destination = "104.18.19.125",
            Port = 443,
            OutboundLocalEndpoint = "10.0.0.1:50100",
            Route = FlowRoute.Vpn,
            WfpRedirect = true,
            ProxyAccepted = true,
            VpnOutboundBound = true,
            VpnOutboundConnected = true,
            Status = FlowLifecycle.Connected,
        };
        UpsertProxy(store, flow, ticks, attr, RoutingTraceSourceAliases.ProxyFlow(proxyFlowId));
        Assert.Single(store.SnapshotOrdered());
    }

    [Fact]
    public void V13_different_local_ports_same_remote_are_two_flows()
    {
        var store = new RoutingTraceLogicalFlowStore();
        long ticks = DateTimeOffset.UtcNow.UtcTicks;
        var attr = new RoutingTraceAttribution { ProcessPath = @"C:\Apps\Probe.exe", InheritsTargetExpectedRoute = true };
        UpsertProxy(store, MakeProxyFlow(50101, Guid.NewGuid()), ticks, attr, null);
        UpsertProxy(store, MakeProxyFlow(50102, Guid.NewGuid()), ticks, attr, null);
        Assert.Equal(2, store.SnapshotOrdered().Count);
    }

    [Fact]
    public void V13_summary_not_inflated_by_proxy_lifecycle_updates()
    {
        var store = new RoutingTraceLogicalFlowStore();
        long ticks = DateTimeOffset.UtcNow.UtcTicks;
        var attr = new RoutingTraceAttribution { ProcessPath = @"C:\Apps\Probe.exe", InheritsTargetExpectedRoute = true };
        Guid id = Guid.NewGuid();
        string alias = RoutingTraceSourceAliases.ProxyFlow(id);
        UpsertProxy(store, MakeProxyFlow(50111, id, connected: false), ticks, attr, alias);
        UpsertProxy(store, MakeProxyFlow(50111, id, connected: true), ticks, attr, alias);
        var summary = RoutingTraceSummaryBuilder.Build(VpnTarget(), TimeSpan.FromMinutes(1), store.SnapshotOrdered());
        Assert.Equal(1, summary.TotalLogicalFlows);
    }

    private static FlowEvent MakeProxyFlow(int localPort, Guid flowId, bool connected = true)
    {
        return new FlowEvent
        {
            FlowId = flowId,
            Pid = 10,
            ProcessPath = @"C:\Apps\Probe.exe",
            Destination = "104.18.19.125",
            Port = 443,
            OutboundLocalEndpoint = $"10.0.0.1:{localPort}",
            Route = FlowRoute.Vpn,
            WfpRedirect = true,
            ProxyAccepted = true,
            VpnOutboundBound = true,
            VpnOutboundConnected = connected,
            Status = connected ? FlowLifecycle.Connected : FlowLifecycle.Accepted,
        };
    }

    private static void UpsertProxy(
        RoutingTraceLogicalFlowStore store,
        FlowEvent flow,
        long ticks,
        RoutingTraceAttribution attr,
        string? alias)
    {
        (string pp, string pw) = RoutingTraceFlowCorrelation.FromProxyFlow(flow, ticks);
        RoutingTraceEvent draft = RoutingTraceFlowEventMapper.FromProxyFlow(flow, VpnTarget(), attr, 0, ticks);
        IReadOnlyList<string>? aliases = alias is null ? null : [alias];
        store.Upsert(draft, pp, pw, aliases);
    }



    [Fact]
    public void V14_identical_passive_observation_does_not_grow_update_count()
    {
        var store = new RoutingTraceLogicalFlowStore();
        long ticks = DateTimeOffset.UtcNow.UtcTicks;
        var attr = new RoutingTraceAttribution { ProcessPath = @"C:\Apps\Probe.exe", InheritsTargetExpectedRoute = true };
        var obs = new PassiveSocketObservation(10, RoutingTraceProtocol.Tcp, RoutingTraceAddressFamily.IPv4, "10.0.0.1", 50000, "104.18.19.125", 443, "ESTABLISHED", DateTimeOffset.UtcNow);
        UpsertPassive(store, obs, ticks, attr);
        long seq = store.SnapshotOrdered()[0].SequenceId;
        for (int i = 0; i < 100; i++)
        {
            UpsertPassive(store, obs, ticks, attr);
        }
        RoutingTraceEvent flow = store.SnapshotOrdered()[0];
        Assert.Equal(0, flow.UpdateCount);
        Assert.Equal(seq, flow.SequenceId);
    }

    [Fact]
    public void V14_identical_proxy_observation_does_not_grow_update_count()
    {
        var store = new RoutingTraceLogicalFlowStore();
        long ticks = DateTimeOffset.UtcNow.UtcTicks;
        var attr = new RoutingTraceAttribution { ProcessPath = @"C:\Apps\Probe.exe", InheritsTargetExpectedRoute = true };
        Guid id = Guid.NewGuid();
        var flow = MakeProxyFlow(50123, id, connected: true);
        string alias = RoutingTraceSourceAliases.ProxyFlow(id);
        UpsertProxy(store, flow, ticks, attr, alias);
        long seq = store.SnapshotOrdered()[0].SequenceId;
        for (int i = 0; i < 100; i++)
        {
            UpsertProxy(store, flow, ticks, attr, alias);
        }
        RoutingTraceEvent stored = store.SnapshotOrdered()[0];
        Assert.Equal(0, stored.UpdateCount);
        Assert.Equal(seq, stored.SequenceId);
    }

    [Fact]
    public void V14_evidence_addition_is_meaningful_update()
    {
        var store = new RoutingTraceLogicalFlowStore();
        long ticks = DateTimeOffset.UtcNow.UtcTicks;
        var attr = new RoutingTraceAttribution { ProcessPath = @"C:\Apps\Probe.exe", InheritsTargetExpectedRoute = true };
        Guid id = Guid.NewGuid();
        var early = MakeProxyFlow(50124, id, connected: false);
        string alias = RoutingTraceSourceAliases.ProxyFlow(id);
        UpsertProxy(store, early, ticks, attr, alias);
        long seq1 = store.SnapshotOrdered()[0].SequenceId;
        UpsertProxy(store, MakeProxyFlow(50124, id, connected: true), ticks, attr, alias);
        RoutingTraceEvent flow = store.SnapshotOrdered()[0];
        Assert.Equal(1, flow.UpdateCount);
        Assert.True(flow.SequenceId > seq1);
        Assert.True(flow.EvidenceFlags.HasFlag(RoutingTraceEvidenceFlags.ProxyConnected));
    }

    [Fact]
    public void V14_poorer_passive_snapshot_cannot_downgrade_connected_vpn()
    {
        var store = new RoutingTraceLogicalFlowStore();
        long ticks = DateTimeOffset.UtcNow.UtcTicks;
        var attr = new RoutingTraceAttribution { ProcessPath = @"C:\Apps\Probe.exe", InheritsTargetExpectedRoute = true };
        UpsertProxy(store, MakeProxyFlow(50125, Guid.NewGuid(), connected: true), ticks, attr, RoutingTraceSourceAliases.ProxyFlow(Guid.NewGuid()));
        var obs = new PassiveSocketObservation(10, RoutingTraceProtocol.Tcp, RoutingTraceAddressFamily.IPv4, "10.0.0.1", 50125, "104.18.19.125", 443, "ESTABLISHED", DateTimeOffset.UtcNow);
        UpsertPassive(store, obs, ticks, attr);
        RoutingTraceEvent flow = store.SnapshotOrdered()[0];
        Assert.Equal(RoutingTraceObservedRoute.Vpn, flow.ObservedRoute);
        Assert.Equal(RoutingTraceOutcome.Connected, flow.Outcome);
    }

    [Fact]
    public void V14_last_seen_refreshes_without_public_update()
    {
        var store = new RoutingTraceLogicalFlowStore();
        long ticks = DateTimeOffset.UtcNow.UtcTicks;
        var attr = new RoutingTraceAttribution { ProcessPath = @"C:\Apps\Probe.exe", InheritsTargetExpectedRoute = true };
        var t0 = DateTimeOffset.UtcNow;
        var obs = new PassiveSocketObservation(10, RoutingTraceProtocol.Tcp, RoutingTraceAddressFamily.IPv4, "10.0.0.1", 50055, "104.18.19.125", 443, "ESTABLISHED", t0);
        UpsertPassive(store, obs, ticks, attr);
        var t1 = t0.AddSeconds(30);
        var obs2 = obs with { ObservedAt = t1 };
        UpsertPassive(store, obs2, ticks, attr);
        RoutingTraceEvent flow = store.SnapshotOrdered()[0];
        Assert.True(flow.LastSeenUtc >= t1);
        Assert.Equal(0, flow.UpdateCount);
    }

    [Fact]
    public void V14_identical_observations_do_not_fill_event_ring()
    {
        var store = new RoutingTraceLogicalFlowStore();
        var ring = new RoutingTraceRingBuffer(1000);
        long ticks = DateTimeOffset.UtcNow.UtcTicks;
        var attr = new RoutingTraceAttribution { ProcessPath = @"C:\Apps\Probe.exe", InheritsTargetExpectedRoute = true };
        var obs = new PassiveSocketObservation(10, RoutingTraceProtocol.Tcp, RoutingTraceAddressFamily.IPv4, "10.0.0.1", 50066, "104.18.19.125", 443, "ESTABLISHED", DateTimeOffset.UtcNow);
        void Store(RoutingTraceFlowUpsertResult r)
        {
            if (r.Created || r.Updated)
            {
                ring.Add(r.Event);
            }
        }
        (string pk, string wk) = RoutingTraceFlowCorrelation.FromPassiveObservation(obs, ticks);
        Store(store.Upsert(RoutingTraceSocketObservationMapper.FromPassiveObservation(obs, VpnTarget(), attr, 0, ticks), pk, wk));
        for (int i = 0; i < 200; i++)
        {
            Store(store.Upsert(RoutingTraceSocketObservationMapper.FromPassiveObservation(obs, VpnTarget(), attr, 0, ticks), pk, wk));
        }
        Assert.Single(ring.SnapshotOrdered());
        Assert.Equal(0, ring.DroppedEventCount);
    }

    [Fact]
    public void V14_synthetic_5000_identical_observations_stays_bounded()
    {
        var store = new RoutingTraceLogicalFlowStore();
        long ticks = DateTimeOffset.UtcNow.UtcTicks;
        var attr = new RoutingTraceAttribution { ProcessPath = @"C:\Apps\Probe.exe", InheritsTargetExpectedRoute = true };
        var obs = new PassiveSocketObservation(10, RoutingTraceProtocol.Tcp, RoutingTraceAddressFamily.IPv4, "10.0.0.1", 50077, "104.18.19.125", 443, "ESTABLISHED", DateTimeOffset.UtcNow);
        UpsertPassive(store, obs, ticks, attr);
        for (int i = 0; i < 5000; i++)
        {
            UpsertPassive(store, obs, ticks, attr);
        }
        Assert.Single(store.SnapshotOrdered());
        Assert.Equal(0, store.SnapshotOrdered()[0].UpdateCount);
        Assert.Equal(0, store.DroppedEventCount);
    }


    private static void UpsertPassive(RoutingTraceLogicalFlowStore store, PassiveSocketObservation obs, long ticks, RoutingTraceAttribution attr)
    {
        (string primary, string weak) = RoutingTraceFlowCorrelation.FromPassiveObservation(obs, ticks);
        RoutingTraceEvent draft = RoutingTraceSocketObservationMapper.FromPassiveObservation(obs, VpnTarget(), attr, 0, ticks);
        store.Upsert(draft, primary, weak);
    }

}
