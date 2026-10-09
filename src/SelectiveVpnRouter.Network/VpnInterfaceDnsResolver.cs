using System.Net;
using System.Net.Sockets;
using SelectiveVpnRouter.Core.BrowserRouting;

namespace SelectiveVpnRouter.Network;

/// <summary>
/// VPN-only DNS: UDP queries are sent from a socket bound to the tunnel interface (IP_UNICAST_IF)
/// toward DNS servers configured on that adapter. This is the only production layer allowed to perform
/// hostname resolution for browser explicit proxy.
/// </summary>
public sealed class VpnInterfaceDnsResolver
{
    public const int MaxHostLength = 253;
    public const int DefaultTimeoutMs = 5000;

    private readonly IVpnTunnelEgressReadiness _tunnel;
    private readonly IVpnSessionDnsServers _sessionDns;
    private readonly IVpnAdapterDnsServers _adapterDns;
    private readonly IVpnDnsUdpTransport _transport;
    private readonly int _timeoutMs;

    public VpnInterfaceDnsResolver(IVpnTunnelEgressReadiness tunnel, IVpnSessionDnsServers sessionDns)
        : this(tunnel, sessionDns, new NetworkInterfaceVpnAdapterDnsServers(), new VpnBoundUdpDnsTransport(), DefaultTimeoutMs)
    {
    }

    internal VpnInterfaceDnsResolver(
        IVpnTunnelEgressReadiness tunnel,
        IVpnSessionDnsServers sessionDns,
        IVpnAdapterDnsServers adapterDns,
        IVpnDnsUdpTransport transport,
        int timeoutMs)
    {
        _tunnel = tunnel;
        _sessionDns = sessionDns;
        _adapterDns = adapterDns;
        _transport = transport;
        _timeoutMs = timeoutMs;
    }

    public async Task<VpnDnsResolutionResult> ResolveIpv4Async(string hostname, CancellationToken cancellationToken = default)
    {
        if (!VpnDnsWireFormat.TryValidateHostname(hostname))
            return VpnDnsResolutionResult.Failure(VpnDnsResolutionStatus.InvalidHostname);

        if (!_tunnel.TryGetTunnelInterfaceIndex(out int interfaceIndex))
            return VpnDnsResolutionResult.Failure(VpnDnsResolutionStatus.TunnelNotReady);

        IReadOnlyList<IPAddress> servers = ResolveDnsServers(interfaceIndex);
        if (servers.Count == 0)
            return VpnDnsResolutionResult.Failure(VpnDnsResolutionStatus.NoDnsServers);

        VpnDnsResolutionStatus lastFailure = VpnDnsResolutionStatus.TransportError;
        var buffer = new byte[VpnDnsWireFormat.DefaultUdpBufferSize];

        foreach (IPAddress server in servers)
        {
            if (server.AddressFamily != AddressFamily.InterNetwork)
                continue;

            ushort transactionId = (ushort)Random.Shared.Next(1, ushort.MaxValue);
            byte[] query = VpnDnsWireFormat.BuildAQuery(hostname, transactionId);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_timeoutMs);

            VpnDnsTransportResult transportResult;
            try
            {
                transportResult = await _transport
                    .QueryAsync(interfaceIndex, server, query, buffer, timeout.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return VpnDnsResolutionResult.Failure(VpnDnsResolutionStatus.Timeout);
            }
            catch (OperationCanceledException)
            {
                lastFailure = VpnDnsResolutionStatus.Timeout;
                continue;
            }

            switch (transportResult.Outcome)
            {
                case VpnDnsTransportOutcome.BindFailed:
                    return VpnDnsResolutionResult.Failure(VpnDnsResolutionStatus.BindFailed);
                case VpnDnsTransportOutcome.Cancelled:
                    if (cancellationToken.IsCancellationRequested)
                        return VpnDnsResolutionResult.Failure(VpnDnsResolutionStatus.Timeout);
                    lastFailure = VpnDnsResolutionStatus.Timeout;
                    continue;
                case VpnDnsTransportOutcome.SendFailed:
                case VpnDnsTransportOutcome.ReceiveFailed:
                    lastFailure = VpnDnsResolutionStatus.TransportError;
                    continue;
            }

            VpnDnsWireParseResult parsed = VpnDnsWireFormat.TryParseARecords(
                buffer.AsSpan(0, transportResult.ByteCount),
                transactionId);

            switch (parsed.Status)
            {
                case VpnDnsWireParseStatus.Success:
                    return VpnDnsResolutionResult.Success(parsed.Addresses);
                case VpnDnsWireParseStatus.NxDomain:
                    return VpnDnsResolutionResult.Failure(VpnDnsResolutionStatus.NxDomain);
                case VpnDnsWireParseStatus.ServFail:
                    return VpnDnsResolutionResult.Failure(VpnDnsResolutionStatus.ServFail);
                case VpnDnsWireParseStatus.Truncated:
                    return VpnDnsResolutionResult.Failure(VpnDnsResolutionStatus.TruncatedResponse);
                case VpnDnsWireParseStatus.TransactionIdMismatch:
                    lastFailure = VpnDnsResolutionStatus.TransactionIdMismatch;
                    continue;
                case VpnDnsWireParseStatus.Malformed:
                    lastFailure = VpnDnsResolutionStatus.MalformedResponse;
                    continue;
                case VpnDnsWireParseStatus.NoMatchingAnswer:
                    lastFailure = VpnDnsResolutionStatus.NoMatchingAnswer;
                    continue;
            }
        }

        return VpnDnsResolutionResult.Failure(lastFailure);
    }

    internal IReadOnlyList<IPAddress> ResolveDnsServers(int interfaceIndex)
    {
        IReadOnlyList<IPAddress> session = _sessionDns.GetIpv4DnsServers();
        if (session.Count > 0)
            return session;

        return _adapterDns.GetIpv4DnsServers(interfaceIndex);
    }
}
