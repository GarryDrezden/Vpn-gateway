using System.Net.Sockets;

namespace SelectiveVpnRouter.Proxy.BrowserExplicit;

public enum VpnConnectTargetKind
{
    Ipv4,
    Domain,
    Ipv6,
}

public sealed record VpnConnectTarget(VpnConnectTargetKind Kind, string? Host, byte[]? AddressBytes, int Port);

public sealed record VpnConnectOutcome(bool Success, Socket? Socket, Socks5ReplyCode FailureCode);

/// <summary>Fail-closed TCP connect through the VPN tunnel interface only.</summary>
public interface IVpnTcpEgress
{
    ValueTask<VpnConnectOutcome> ConnectAsync(VpnConnectTarget target, CancellationToken cancellationToken);
}
