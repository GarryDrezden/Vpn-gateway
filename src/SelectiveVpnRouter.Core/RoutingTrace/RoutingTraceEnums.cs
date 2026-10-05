namespace SelectiveVpnRouter.Core.RoutingTrace;

public enum RoutingTraceProtocol { Tcp = 0, Udp = 1 }
public enum RoutingTraceAddressFamily { IPv4 = 4, IPv6 = 6 }
public enum RoutingTraceDestinationKind { External = 0, Loopback = 1, PrivateLan = 2, LinkLocal = 3, Unknown = 4 }
public enum RoutingTraceExpectedRoute { Vpn = 0, Direct = 1, Local = 2, DefaultDirect = 3 }
public enum RoutingTraceObservedRoute { Vpn = 0, Direct = 1, Local = 2, Uncovered = 3, Unknown = 4 }
public enum RoutingTraceOutcome { Connected = 0, Failed = 1, Timeout = 2, Reset = 3, AccessDenied = 4, Listening = 5, Unknown = 6 }
public enum RoutingTraceCoverageReason
{
    None = 0,
    PreExistingAtTraceStart = 1,
    UnsupportedProtocol = 2,
    UnsupportedAddressFamily = 3,
    MissingProxyEvidence = 4,
    CorrelationIncomplete = 5,
    InsufficientEvidence = 6,
    ProvenDirect = 7,
    LocalBypass = 8,
    Unknown = 9,
}

public enum RoutingTraceFindingKind { RoutingLeak = 0, UncoveredProtocol = 1, UncoveredAddressFamily = 2, UnassociatedProcess = 3, LoopbackCorrect = 4, ConnectionFailure = 5, UnknownEvidence = 6, PreExistingHistorical = 7, MissingProxyEvidence = 8 }
[Flags]
public enum RoutingTraceEvidenceFlags
{
    None = 0,
    WfpAppFilterMatched = 1 << 0,
    WfpRedirectApplied = 1 << 1,
    LoopbackPermitMatched = 1 << 2,
    ProxyAccepted = 1 << 3,
    ProxyConnected = 1 << 4,
    VpnBound = 1 << 5,
    DirectObserved = 1 << 6,
    ChildProcess = 1 << 7,
    PackagedHelper = 1 << 8,
    QuicCandidate = 1 << 9,
    Ipv6 = 1 << 10,
    PassiveSocketTable = 1 << 11,
    InsufficientEvidence = 1 << 12,
}
public enum RoutingTraceSessionState { Idle = 0, Running = 1, Completed = 2 }
public enum RoutingTraceHostnameSource { None = 0, DnsCorrelation = 1 }
