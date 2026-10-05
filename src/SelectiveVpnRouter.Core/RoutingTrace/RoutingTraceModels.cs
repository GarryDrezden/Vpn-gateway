using System.Text.Json.Serialization;

namespace SelectiveVpnRouter.Core.RoutingTrace;

public sealed record PassiveSocketObservation(
    int Pid,
    RoutingTraceProtocol Protocol,
    RoutingTraceAddressFamily AddressFamily,
    string LocalAddress,
    int LocalPort,
    string RemoteAddress,
    int RemotePort,
    string State,
    DateTimeOffset ObservedAt);

public sealed record RoutingTraceTarget
{
    public string DisplayName { get; init; } = "";
    public string PrimaryExecutablePath { get; init; } = "";
    public Guid? RuleId { get; init; }
    public RoutingTraceExpectedRoute ExpectedRoute { get; init; } = RoutingTraceExpectedRoute.Vpn;
    public IReadOnlyList<string> AssociatedExecutablePaths { get; init; } = [];
}

public sealed record RoutingTraceAttribution
{
    public string? ProcessPath { get; init; }
    public bool InheritsTargetExpectedRoute { get; init; }
    public bool IsPackagedHelper { get; init; }
    public bool IsChildProcess { get; init; }
    public Guid? LogicalRuleId { get; init; }
    public string? LogicalApplicationName { get; init; }
}

public sealed record RoutingTraceOptions
{
    public bool FollowChildProcesses { get; init; } = true;
    public bool IncludePackagedHelpers { get; init; } = true;
    public bool IncludeUdp { get; init; } = true;
    public bool IncludeIpv6 { get; init; } = true;
    public bool IncludeLoopback { get; init; } = true;
    public int MaxEvents { get; init; } = 10_000;
}

public sealed record StartRoutingTraceRequest
{
    public Guid? RuleId { get; init; }
    public string? ExecutablePath { get; init; }
    public RoutingTraceOptions? Options { get; init; }
}

public sealed record GetRoutingTraceEventsRequest
{
    public Guid SessionId { get; init; }
    public long AfterSequence { get; init; }
    public int Limit { get; init; } = 200;
}

public sealed record RoutingTraceLiveCounters
{
    public int TcpIpv4Vpn { get; init; }
    public int UdpIpv4Uncovered { get; init; }
    public int TcpIpv6Uncovered { get; init; }
    public int UdpIpv6Uncovered { get; init; }
    public int ConfirmedLeaks { get; init; }
    public int TcpIpv4Historical { get; init; }
    public int TcpIpv4MissingProxy { get; init; }
}

public sealed record RoutingTraceFinding
{
    public RoutingTraceFindingKind Kind { get; init; }
    public string Summary { get; init; } = "";
    public int Count { get; init; }
}

public sealed record RoutingTraceProtocolQualityLine
{
    public string Label { get; init; } = "";
    public int Vpn { get; init; }
    public int Direct { get; init; }
    public int Uncovered { get; init; }
    public int Unknown { get; init; }
    public int Local { get; init; }
    public int Historical { get; init; }
}

public sealed record RoutingTraceQualitySummary
{
    public int UnknownFlows { get; init; }
    public int ConfirmedLeaks { get; init; }
    public int UncoveredFlows { get; init; }
}

public sealed record RoutingTraceSummary
{
    public TimeSpan Duration { get; init; }
    public int TotalEvents { get; init; }
    public int TotalLogicalFlows { get; init; }
    public RoutingTraceQualitySummary Quality { get; init; } = new();
    public IReadOnlyList<RoutingTraceProtocolQualityLine> ProtocolLines { get; init; } = [];
    public IReadOnlyList<RoutingTraceFinding> Findings { get; init; } = [];
}

public sealed record RoutingTraceSession
{
    public Guid SessionId { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? StoppedAt { get; init; }
    public RoutingTraceSessionState State { get; init; } = RoutingTraceSessionState.Idle;
    public RoutingTraceTarget Target { get; init; } = new();
    public RoutingTraceOptions Options { get; init; } = new();
    public RoutingTraceSummary? Summary { get; init; }
    public RoutingTraceLiveCounters LiveCounters { get; init; } = new();
    public long LastEventSequenceId { get; init; }
    public int DroppedEventCount { get; init; }
}

public sealed record RoutingTraceEvent
{
    public long SequenceId { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public Guid FlowId { get; init; }
    public Guid? SourceProxyFlowId { get; init; }
    public int UpdateCount { get; init; }
    public string FlowCorrelationKey { get; init; } = "";
    public int ProcessId { get; init; }
    public int? ParentProcessId { get; init; }
    public string ProcessPath { get; init; } = "";
    public long ProcessStartUtcTicks { get; init; }
    public RoutingTraceProtocol Protocol { get; init; }
    public RoutingTraceAddressFamily AddressFamily { get; init; }
    public string LocalAddress { get; init; } = "";
    public int LocalPort { get; init; }
    public string RemoteAddress { get; init; } = "";
    public int RemotePort { get; init; }
    public RoutingTraceDestinationKind DestinationKind { get; init; }
    public RoutingTraceExpectedRoute ExpectedRoute { get; init; }
    public RoutingTraceObservedRoute ObservedRoute { get; init; }
    public DateTimeOffset FirstSeenUtc { get; init; }
    public DateTimeOffset LastSeenUtc { get; init; }
    public bool PreExistingAtTraceStart { get; init; }
    public RoutingTraceCoverageReason CoverageReason { get; init; } = RoutingTraceCoverageReason.None;
    public RoutingTraceOutcome Outcome { get; init; } = RoutingTraceOutcome.Unknown;
    public RoutingTraceEvidenceFlags EvidenceFlags { get; init; }
    public string? Hostname { get; init; }
    public RoutingTraceHostnameSource HostnameSource { get; init; }
    public Guid? LogicalRuleId { get; init; }
    public string? LogicalApplicationName { get; init; }
}

public sealed record RoutingTraceSnapshot
{
    public RoutingTraceSession? Session { get; init; }
    public IReadOnlyList<RoutingTraceEvent> RecentEvents { get; init; } = [];
}

public sealed record RoutingTraceEventsPage
{
    public Guid SessionId { get; init; }
    public IReadOnlyList<RoutingTraceEvent> Events { get; init; } = [];
    public long LastSequenceId { get; init; }
    public bool HasMore { get; init; }
    public bool SessionFound { get; init; } = true;
}

public sealed record RoutingTraceStatus
{
    public bool Active { get; init; }
    public Guid? ActiveSessionId { get; init; }
    public DateTimeOffset? ActiveStartedAt { get; init; }
    public string? ActiveTargetName { get; init; }
    public bool HasCompletedSession { get; init; }
    public Guid? CompletedSessionId { get; init; }
    public DateTimeOffset? CompletedStartedAt { get; init; }
    public DateTimeOffset? CompletedStoppedAt { get; init; }
    public string? CompletedTargetName { get; init; }
}
