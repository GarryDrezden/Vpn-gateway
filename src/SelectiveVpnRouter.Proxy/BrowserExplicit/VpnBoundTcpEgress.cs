using System.Net;
using System.Net.Sockets;
using SelectiveVpnRouter.Core.BrowserRouting;
using SelectiveVpnRouter.Network;

namespace SelectiveVpnRouter.Proxy.BrowserExplicit;

public sealed class VpnBoundTcpEgress : IVpnTcpEgress
{
    public const int ConnectTimeoutMs = 15_000;

    private readonly IVpnTunnelEgressReadiness _tunnel;
    private readonly IVpnBrowserHostnameResolver _dns;
    private readonly IVpnInterfaceSocketBinder _binder;

    public VpnBoundTcpEgress(IVpnTunnelEgressReadiness tunnel, VpnInterfaceDnsResolver dns)
        : this(tunnel, new VpnInterfaceDnsResolverHostnameResolver(dns), new ProductionVpnInterfaceSocketBinder())
    {
    }

    internal VpnBoundTcpEgress(
        IVpnTunnelEgressReadiness tunnel,
        IVpnBrowserHostnameResolver dns,
        IVpnInterfaceSocketBinder binder)
    {
        _tunnel = tunnel;
        _dns = dns;
        _binder = binder;
    }

    public async ValueTask<VpnConnectOutcome> ConnectAsync(VpnConnectTarget target, CancellationToken cancellationToken)
    {
        return target.Kind switch
        {
            VpnConnectTargetKind.Ipv4 when target.AddressBytes is { Length: 4 } bytes =>
                await ConnectLiteralAsync(new IPAddress(bytes), target.Port, cancellationToken).ConfigureAwait(false),
            VpnConnectTargetKind.Ipv6 when target.AddressBytes is { Length: 16 } bytes6 =>
                await ConnectLiteralAsync(new IPAddress(bytes6), target.Port, cancellationToken).ConfigureAwait(false),
            VpnConnectTargetKind.Domain when !string.IsNullOrEmpty(target.Host) =>
                await ConnectDomainAsync(target.Host, target.Port, cancellationToken).ConfigureAwait(false),
            _ => new VpnConnectOutcome(false, null, Socks5ReplyCode.HostUnreachable),
        };
    }

    private async ValueTask<VpnConnectOutcome> ConnectDomainAsync(
        string host,
        int port,
        CancellationToken cancellationToken)
    {
        VpnDnsResolutionResult dns = await _dns.ResolveIpv4Async(host, cancellationToken).ConfigureAwait(false);
        if (!dns.IsSuccess)
            return new VpnConnectOutcome(false, null, MapDnsFailure(dns.Status));

        Socks5ReplyCode lastFailure = Socks5ReplyCode.HostUnreachable;
        foreach (IPAddress address in dns.Addresses)
        {
            VpnConnectOutcome attempt = await ConnectLiteralAsync(address, port, cancellationToken).ConfigureAwait(false);
            if (attempt.Success)
                return attempt;

            lastFailure = attempt.FailureCode;
            if (attempt.FailureCode is Socks5ReplyCode.NetworkUnreachable)
                return attempt;
        }

        return new VpnConnectOutcome(false, null, lastFailure);
    }

    private async ValueTask<VpnConnectOutcome> ConnectLiteralAsync(
        IPAddress address,
        int port,
        CancellationToken cancellationToken)
    {
        if (!_tunnel.TryGetTunnelInterfaceIndex(out int interfaceIndex))
            return new VpnConnectOutcome(false, null, Socks5ReplyCode.NetworkUnreachable);

        var remote = new IPEndPoint(address, port);
        var socket = new Socket(remote.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            if (remote.AddressFamily == AddressFamily.InterNetwork)
                _binder.BindIpv4(socket, interfaceIndex);
            else if (remote.AddressFamily == AddressFamily.InterNetworkV6)
                _binder.BindIpv6(socket, interfaceIndex);
            else
            {
                socket.Dispose();
                return new VpnConnectOutcome(false, null, Socks5ReplyCode.AddressTypeNotSupported);
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ConnectTimeoutMs);
            await socket.ConnectAsync(remote, timeout.Token).ConfigureAwait(false);
            return new VpnConnectOutcome(true, socket, Socks5ReplyCode.Success);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            socket.Dispose();
            return new VpnConnectOutcome(false, null, Socks5ReplyCode.HostUnreachable);
        }
        catch (SocketException ex) when (ex.SocketErrorCode is SocketError.ConnectionRefused)
        {
            socket.Dispose();
            return new VpnConnectOutcome(false, null, Socks5ReplyCode.ConnectionRefused);
        }
        catch (SocketException)
        {
            socket.Dispose();
            return new VpnConnectOutcome(false, null, Socks5ReplyCode.GeneralFailure);
        }
        catch (Exception)
        {
            socket.Dispose();
            return new VpnConnectOutcome(false, null, Socks5ReplyCode.GeneralFailure);
        }
    }

    internal static Socks5ReplyCode MapDnsFailure(VpnDnsResolutionStatus status) =>
        status switch
        {
            VpnDnsResolutionStatus.TunnelNotReady => Socks5ReplyCode.NetworkUnreachable,
            VpnDnsResolutionStatus.NxDomain => Socks5ReplyCode.HostUnreachable,
            VpnDnsResolutionStatus.NoDnsServers => Socks5ReplyCode.HostUnreachable,
            VpnDnsResolutionStatus.NoMatchingAnswer => Socks5ReplyCode.HostUnreachable,
            VpnDnsResolutionStatus.InvalidHostname => Socks5ReplyCode.HostUnreachable,
            VpnDnsResolutionStatus.Timeout => Socks5ReplyCode.HostUnreachable,
            VpnDnsResolutionStatus.ServFail => Socks5ReplyCode.GeneralFailure,
            VpnDnsResolutionStatus.TruncatedResponse => Socks5ReplyCode.GeneralFailure,
            VpnDnsResolutionStatus.MalformedResponse => Socks5ReplyCode.GeneralFailure,
            VpnDnsResolutionStatus.TransactionIdMismatch => Socks5ReplyCode.GeneralFailure,
            VpnDnsResolutionStatus.BindFailed => Socks5ReplyCode.NetworkUnreachable,
            VpnDnsResolutionStatus.TransportError => Socks5ReplyCode.HostUnreachable,
            _ => Socks5ReplyCode.GeneralFailure,
        };
}
