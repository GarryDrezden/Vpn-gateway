namespace SelectiveVpnRouter.Network;

public enum VpnDnsResolutionStatus
{
    Success,
    TunnelNotReady,
    InvalidHostname,
    NoDnsServers,
    Timeout,
    NxDomain,
    ServFail,
    TruncatedResponse,
    MalformedResponse,
    NoMatchingAnswer,
    TransactionIdMismatch,
    BindFailed,
    TransportError,
}

public sealed class VpnDnsResolutionResult
{
    public static VpnDnsResolutionResult Success(IReadOnlyList<System.Net.IPAddress> addresses) =>
        new(VpnDnsResolutionStatus.Success, addresses);

    public static VpnDnsResolutionResult Failure(VpnDnsResolutionStatus status) =>
        new(status, Array.Empty<System.Net.IPAddress>());

    private VpnDnsResolutionResult(VpnDnsResolutionStatus status, IReadOnlyList<System.Net.IPAddress> addresses)
    {
        Status = status;
        Addresses = addresses;
    }

    public VpnDnsResolutionStatus Status { get; }

    public IReadOnlyList<System.Net.IPAddress> Addresses { get; }

    public bool IsSuccess => Status == VpnDnsResolutionStatus.Success;
}
