using System.Net;
using System.Net.Sockets;
using SelectiveVpnRouter.Core.BrowserRouting;
using SelectiveVpnRouter.Network;
using SelectiveVpnRouter.Proxy.BrowserExplicit;
using Xunit;

namespace SelectiveVpnRouter.Proxy.Tests;

public class VpnBoundTcpEgressTests
{
    [Fact]
    public async Task M_vpn_not_ready_no_bind_attempt()
    {
        var tunnel = new FakeTunnel(false, 0);
        var binder = new RecordingVpnInterfaceSocketBinder();
        var egress = new VpnBoundTcpEgress(tunnel, new FixedHostnameResolver([]), binder);
        VpnConnectOutcome outcome = await egress.ConnectAsync(
            new VpnConnectTarget(VpnConnectTargetKind.Ipv4, null, [127, 0, 0, 1], 80),
            CancellationToken.None);
        Assert.Equal(Socks5ReplyCode.NetworkUnreachable, outcome.FailureCode);
        Assert.Empty(binder.Ipv4Indexes);
    }

    [Fact]
    public async Task N_ipv4_bind_called_before_connect()
    {
        var echo = StartEcho();
        var tunnel = new FakeTunnel(true, 77);
        var binder = new RecordingVpnInterfaceSocketBinder();
        var egress = new VpnBoundTcpEgress(tunnel, new FixedHostnameResolver([]), binder);
        VpnConnectOutcome outcome = await egress.ConnectAsync(
            new VpnConnectTarget(VpnConnectTargetKind.Ipv4, null, echo.Address.GetAddressBytes(), echo.Port),
            CancellationToken.None);
        Assert.True(outcome.Success);
        Assert.Equal([77], binder.Ipv4Indexes);
        outcome.Socket?.Dispose();
    }

    [Fact]
    public async Task O_bind_failure_fail_closed()
    {
        var tunnel = new FakeTunnel(true, 5);
        var binder = new RecordingVpnInterfaceSocketBinder { FailIpv4Bind = true };
        var egress = new VpnBoundTcpEgress(tunnel, new FixedHostnameResolver([]), binder);
        VpnConnectOutcome outcome = await egress.ConnectAsync(
            new VpnConnectTarget(VpnConnectTargetKind.Ipv4, null, [127, 0, 0, 1], 80),
            CancellationToken.None);
        Assert.False(outcome.Success);
        Assert.Equal(Socks5ReplyCode.GeneralFailure, outcome.FailureCode);
        Assert.Single(binder.Ipv4Indexes);
    }

    [Fact]
    public async Task P_dns_success_tcp_success()
    {
        var echo = StartEcho();
        var tunnel = new FakeTunnel(true, 12);
        var binder = new RecordingVpnInterfaceSocketBinder();
        var resolver = new FixedHostnameResolver([echo.Address]);
        var egress = new VpnBoundTcpEgress(tunnel, resolver, binder);
        VpnConnectOutcome outcome = await egress.ConnectAsync(
            new VpnConnectTarget(VpnConnectTargetKind.Domain, "host.example", null, echo.Port),
            CancellationToken.None);
        Assert.True(outcome.Success);
        outcome.Socket?.Dispose();
    }

    [Fact]
    public async Task Q_dns_nxdomain_host_unreachable()
    {
        var tunnel = new FakeTunnel(true, 1);
        var resolver = new FixedHostnameResolver(
            VpnDnsResolutionResult.Failure(VpnDnsResolutionStatus.NxDomain));
        var egress = new VpnBoundTcpEgress(tunnel, resolver, new RecordingVpnInterfaceSocketBinder());
        VpnConnectOutcome outcome = await egress.ConnectAsync(
            new VpnConnectTarget(VpnConnectTargetKind.Domain, "missing.example", null, 80),
            CancellationToken.None);
        Assert.Equal(Socks5ReplyCode.HostUnreachable, outcome.FailureCode);
    }

    [Fact]
    public async Task R_second_a_succeeds_after_first_refused()
    {
        var echo = StartEcho();
        var tunnel = new FakeTunnel(true, 9);
        var binder = new RecordingVpnInterfaceSocketBinder();
        var resolver = new FixedHostnameResolver([
            IPAddress.Parse("127.0.0.2"),
            echo.Address,
        ]);
        var egress = new VpnBoundTcpEgress(tunnel, resolver, binder);
        VpnConnectOutcome outcome = await egress.ConnectAsync(
            new VpnConnectTarget(VpnConnectTargetKind.Domain, "multi.example", null, echo.Port),
            CancellationToken.None);
        Assert.True(outcome.Success);
        Assert.Equal(2, binder.Ipv4Indexes.Count);
        outcome.Socket?.Dispose();
    }

    [Fact]
    public async Task S_all_a_fail_controlled_failure()
    {
        var tunnel = new FakeTunnel(true, 9);
        var resolver = new FixedHostnameResolver([
            IPAddress.Parse("127.0.0.2"),
            IPAddress.Parse("127.0.0.3"),
        ]);
        var egress = new VpnBoundTcpEgress(tunnel, resolver, new RecordingVpnInterfaceSocketBinder());
        VpnConnectOutcome outcome = await egress.ConnectAsync(
            new VpnConnectTarget(VpnConnectTargetKind.Domain, "none.example", null, 65500),
            CancellationToken.None);
        Assert.False(outcome.Success);
        Assert.Equal(Socks5ReplyCode.ConnectionRefused, outcome.FailureCode);
    }

    [Fact]
    public async Task T_tunnel_lost_after_dns_still_fail_closed()
    {
        var tunnel = new FakeTunnel(false, 0);
        var resolver = new FixedHostnameResolver([IPAddress.Parse("127.0.0.1")]);
        var egress = new VpnBoundTcpEgress(tunnel, resolver, new RecordingVpnInterfaceSocketBinder());
        VpnConnectOutcome outcome = await egress.ConnectAsync(
            new VpnConnectTarget(VpnConnectTargetKind.Domain, "race.example", null, 80),
            CancellationToken.None);
        Assert.Equal(Socks5ReplyCode.NetworkUnreachable, outcome.FailureCode);
    }

    [Fact]
    public async Task U_each_tcp_attempt_uses_fresh_tunnel_index()
    {
        var echo = StartEcho();
        var tunnel = new SteppingTunnel([10, 20]);
        var binder = new RecordingVpnInterfaceSocketBinder();
        var resolver = new FixedHostnameResolver([
            IPAddress.Parse("127.0.0.2"),
            echo.Address,
        ]);
        var egress = new VpnBoundTcpEgress(tunnel, resolver, binder);
        VpnConnectOutcome outcome = await egress.ConnectAsync(
            new VpnConnectTarget(VpnConnectTargetKind.Domain, "step.example", null, echo.Port),
            CancellationToken.None);
        Assert.True(outcome.Success);
        Assert.Equal([10, 20], binder.Ipv4Indexes);
        outcome.Socket?.Dispose();
    }

    private static (IPAddress Address, int Port) StartEcho()
    {
        var echo = new TcpListener(IPAddress.Loopback, 0);
        echo.Start();
        var endpoint = (IPEndPoint)echo.LocalEndpoint;
        _ = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    using TcpClient c = await echo.AcceptTcpClientAsync();
                    using NetworkStream s = c.GetStream();
                    var buf = new byte[64];
                    int n = await s.ReadAsync(buf);
                    if (n > 0)
                        await s.WriteAsync(buf.AsMemory(0, n));
                }
            }
            catch
            {
            }
        });
        return (endpoint.Address, endpoint.Port);
    }

    private sealed class FakeTunnel(bool ready, int ifIndex) : IVpnTunnelEgressReadiness
    {
        public bool FailAfterFirstTryGet { get; set; }
        private int _calls;

        public bool TryGetTunnelInterfaceIndex(out int interfaceIndex)
        {
            _calls++;
            if (FailAfterFirstTryGet && _calls > 1)
            {
                interfaceIndex = 0;
                return false;
            }

            interfaceIndex = ifIndex;
            return ready;
        }
    }

    private sealed class SteppingTunnel(int[] indexes) : IVpnTunnelEgressReadiness
    {
        private int _i;

        public bool TryGetTunnelInterfaceIndex(out int interfaceIndex)
        {
            if (_i >= indexes.Length)
            {
                interfaceIndex = 0;
                return false;
            }

            interfaceIndex = indexes[_i++];
            return true;
        }
    }

    private sealed class FixedHostnameResolver : IVpnBrowserHostnameResolver
    {
        private readonly VpnDnsResolutionResult _result;

        public FixedHostnameResolver(IReadOnlyList<IPAddress> addresses) =>
            _result = VpnDnsResolutionResult.Success(addresses);

        public FixedHostnameResolver(VpnDnsResolutionResult result) => _result = result;

        public Task<VpnDnsResolutionResult> ResolveIpv4Async(string hostname, CancellationToken cancellationToken = default) =>
            Task.FromResult(_result);
    }

    private sealed class RecordingVpnInterfaceSocketBinder : IVpnInterfaceSocketBinder
    {
        public List<int> Ipv4Indexes { get; } = [];
        public bool FailIpv4Bind { get; set; }

        public void BindIpv4(Socket socket, int interfaceIndex)
        {
            Ipv4Indexes.Add(interfaceIndex);
            if (FailIpv4Bind)
                throw new SocketException((int)SocketError.InvalidArgument);
        }

        public void BindIpv6(Socket socket, int interfaceIndex) =>
            BindIpv4(socket, interfaceIndex);
    }
}
