using System.Net;
using System.Net.Sockets;

namespace SelectiveVpnRouter.Network;

public enum VpnDnsTransportOutcome
{
    Received,
    BindFailed,
    SendFailed,
    ReceiveFailed,
    Cancelled,
}

public sealed class VpnDnsTransportResult
{
    public static VpnDnsTransportResult Received(int byteCount) =>
        new(VpnDnsTransportOutcome.Received, byteCount);

    public static VpnDnsTransportResult Failure(VpnDnsTransportOutcome outcome) =>
        new(outcome, 0);

    private VpnDnsTransportResult(VpnDnsTransportOutcome outcome, int byteCount)
    {
        Outcome = outcome;
        ByteCount = byteCount;
    }

    public VpnDnsTransportOutcome Outcome { get; }

    public int ByteCount { get; }
}

public interface IVpnDnsUdpTransport
{
    Task<VpnDnsTransportResult> QueryAsync(
        int interfaceIndex,
        IPAddress dnsServer,
        ReadOnlyMemory<byte> query,
        Memory<byte> receiveBuffer,
        CancellationToken cancellationToken);
}

public sealed class VpnBoundUdpDnsTransport : IVpnDnsUdpTransport
{
    public async Task<VpnDnsTransportResult> QueryAsync(
        int interfaceIndex,
        IPAddress dnsServer,
        ReadOnlyMemory<byte> query,
        Memory<byte> receiveBuffer,
        CancellationToken cancellationToken)
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            SocketInterfaceBinder.BindIpv4UnicastIf(socket, interfaceIndex);
        }
        catch (SocketException)
        {
            return VpnDnsTransportResult.Failure(VpnDnsTransportOutcome.BindFailed);
        }

        try
        {
            await socket
                .SendToAsync(query, SocketFlags.None, new IPEndPoint(dnsServer, 53), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return VpnDnsTransportResult.Failure(VpnDnsTransportOutcome.Cancelled);
        }
        catch (SocketException)
        {
            return VpnDnsTransportResult.Failure(VpnDnsTransportOutcome.SendFailed);
        }

        try
        {
            int n = await socket
                .ReceiveAsync(receiveBuffer, SocketFlags.None, cancellationToken)
                .ConfigureAwait(false);
            return VpnDnsTransportResult.Received(n);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return VpnDnsTransportResult.Failure(VpnDnsTransportOutcome.Cancelled);
        }
        catch (SocketException)
        {
            return VpnDnsTransportResult.Failure(VpnDnsTransportOutcome.ReceiveFailed);
        }
    }
}
