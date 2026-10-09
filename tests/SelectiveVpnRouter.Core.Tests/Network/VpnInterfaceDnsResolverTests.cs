using System.Net;
using System.Net.Sockets;
using SelectiveVpnRouter.Core.BrowserRouting;
using SelectiveVpnRouter.Network;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests.Network;

public class VpnInterfaceDnsResolverTests
{
    private static readonly IPAddress ExampleA = IPAddress.Parse("93.184.216.34");

    [Fact]
    public async Task A_tunnel_not_ready_does_not_query()
    {
        var tunnel = new FakeTunnel(false, 0);
        var transport = new RecordingTransport();
        var resolver = new VpnInterfaceDnsResolver(
            tunnel,
            new FakeSessionDns([]),
            new FakeAdapterDns([IPAddress.Parse("10.0.0.53")]),
            transport,
            1000);

        VpnDnsResolutionResult result = await resolver.ResolveIpv4Async("example.com");

        Assert.Equal(VpnDnsResolutionStatus.TunnelNotReady, result.Status);
        Assert.Equal(0, transport.QueryCount);
    }

    [Fact]
    public async Task B_valid_tunnel_and_dns_returns_ipv4()
    {
        var tunnel = new FakeTunnel(true, 42);
        var transport = new RecordingTransport
        {
            Handler = (_, _, query, buffer) =>
            {
                ushort tx = (ushort)((query.Span[0] << 8) | query.Span[1]);
                byte[] response = VpnDnsWireFormat.BuildResponse(
                    tx,
                    0x8180,
                    1,
                    ExampleA.GetAddressBytes());
                response.AsSpan().CopyTo(buffer.Span);
                return VpnDnsTransportResult.Received(response.Length);
            },
        };
        var resolver = new VpnInterfaceDnsResolver(
            tunnel,
            new FakeSessionDns([]),
            new FakeAdapterDns([IPAddress.Parse("10.8.0.1")]),
            transport,
            5000);

        VpnDnsResolutionResult result = await resolver.ResolveIpv4Async("www.example.com");

        Assert.True(result.IsSuccess);
        Assert.Single(result.Addresses);
        Assert.Equal(ExampleA, result.Addresses[0]);
        Assert.Equal(42, transport.LastInterfaceIndex);
    }

    [Fact]
    public async Task C_no_dns_servers_fail_closed()
    {
        var tunnel = new FakeTunnel(true, 5);
        var transport = new RecordingTransport();
        var resolver = new VpnInterfaceDnsResolver(
            tunnel,
            new FakeSessionDns([]),
            new FakeAdapterDns([]),
            transport,
            1000);

        VpnDnsResolutionResult result = await resolver.ResolveIpv4Async("example.com");

        Assert.Equal(VpnDnsResolutionStatus.NoDnsServers, result.Status);
        Assert.Equal(0, transport.QueryCount);
    }

    /// <summary>
    /// When UDP receive times out, transport returns Cancelled; resolver must try remaining session DNS servers.
    /// </summary>
    [Fact]
    public async Task U_timeout_on_first_server_tries_remaining_session_dns_servers()
    {
        var tunnel = new FakeTunnel(true, 8);
        var transport = new RecordingTransport
        {
            Handler = (_, server, query, buffer) =>
            {
                if (server.ToString() == "10.0.0.1")
                    return VpnDnsTransportResult.Failure(VpnDnsTransportOutcome.Cancelled);

                ushort tx = (ushort)((query.Span[0] << 8) | query.Span[1]);
                byte[] response = VpnDnsWireFormat.BuildResponse(tx, 0x8180, 1, ExampleA.GetAddressBytes());
                response.AsSpan().CopyTo(buffer.Span);
                return VpnDnsTransportResult.Received(response.Length);
            },
        };
        var resolver = new VpnInterfaceDnsResolver(
            tunnel,
            new FakeSessionDns([IPAddress.Parse("10.0.0.1"), IPAddress.Parse("10.0.0.2")]),
            new FakeAdapterDns([]),
            transport,
            5000);

        VpnDnsResolutionResult result = await resolver.ResolveIpv4Async("example.com");

        Assert.True(result.IsSuccess);
        Assert.Equal(2, transport.QueryCount);
    }

    [Fact]
    public async Task V_first_two_servers_timeout_third_succeeds()
    {
        var tunnel = new FakeTunnel(true, 3);
        var transport = new RecordingTransport
        {
            Handler = (_, server, query, buffer) =>
            {
                if (server.ToString() is "10.0.0.1" or "10.0.0.2")
                    return VpnDnsTransportResult.Failure(VpnDnsTransportOutcome.Cancelled);

                ushort tx = (ushort)((query.Span[0] << 8) | query.Span[1]);
                byte[] response = VpnDnsWireFormat.BuildResponse(tx, 0x8180, 1, ExampleA.GetAddressBytes());
                response.AsSpan().CopyTo(buffer.Span);
                return VpnDnsTransportResult.Received(response.Length);
            },
        };
        var resolver = new VpnInterfaceDnsResolver(
            tunnel,
            new FakeSessionDns([
                IPAddress.Parse("10.0.0.1"),
                IPAddress.Parse("10.0.0.2"),
                IPAddress.Parse("10.0.0.3"),
            ]),
            new FakeAdapterDns([]),
            transport,
            5000);

        VpnDnsResolutionResult result = await resolver.ResolveIpv4Async("example.com");

        Assert.True(result.IsSuccess);
        Assert.Equal(3, transport.QueryCount);
    }

    [Fact]
    public async Task W_all_session_dns_servers_timeout_fail_closed()
    {
        var tunnel = new FakeTunnel(true, 3);
        var transport = new RecordingTransport
        {
            Handler = (_, _, _, _) => VpnDnsTransportResult.Failure(VpnDnsTransportOutcome.Cancelled),
        };
        var resolver = new VpnInterfaceDnsResolver(
            tunnel,
            new FakeSessionDns([
                IPAddress.Parse("10.0.0.1"),
                IPAddress.Parse("10.0.0.2"),
            ]),
            new FakeAdapterDns([]),
            transport,
            5000);

        VpnDnsResolutionResult result = await resolver.ResolveIpv4Async("example.com");

        Assert.Equal(VpnDnsResolutionStatus.Timeout, result.Status);
        Assert.Equal(2, transport.QueryCount);
    }

    [Fact]
    public async Task X_outer_cancellation_stops_without_trying_remaining_servers()
    {
        var tunnel = new FakeTunnel(true, 3);
        using var cts = new CancellationTokenSource();
        var transport = new RecordingTransport
        {
            AsyncHandler = async (_, _, _, _, ct) =>
            {
                cts.Cancel();
                await Task.Delay(5000, ct);
                return VpnDnsTransportResult.Failure(VpnDnsTransportOutcome.Cancelled);
            },
        };
        var resolver = new VpnInterfaceDnsResolver(
            tunnel,
            new FakeSessionDns([
                IPAddress.Parse("10.0.0.1"),
                IPAddress.Parse("10.0.0.2"),
                IPAddress.Parse("10.0.0.3"),
            ]),
            new FakeAdapterDns([]),
            transport,
            5000);

        VpnDnsResolutionResult result = await resolver.ResolveIpv4Async("example.com", cts.Token);

        Assert.Equal(VpnDnsResolutionStatus.Timeout, result.Status);
        Assert.Equal(1, transport.QueryCount);
    }

    [Fact]
    public async Task D_timeout_fail_closed_no_fallback()
    {
        var tunnel = new FakeTunnel(true, 9);
        var transport = new RecordingTransport
        {
            AsyncHandler = async (_, _, _, _, ct) =>
            {
                await Task.Delay(500, ct);
                return VpnDnsTransportResult.Received(0);
            },
        };
        var resolver = new VpnInterfaceDnsResolver(
            tunnel,
            new FakeSessionDns([]),
            new FakeAdapterDns([IPAddress.Parse("10.0.0.1")]),
            transport,
            50);

        VpnDnsResolutionResult result = await resolver.ResolveIpv4Async("example.com");

        Assert.Equal(VpnDnsResolutionStatus.Timeout, result.Status);
        Assert.Equal(1, transport.QueryCount);
    }

    [Fact]
    public async Task E_nxdomain_controlled_failure()
    {
        var tunnel = new FakeTunnel(true, 3);
        var transport = new RecordingTransport
        {
            Handler = (_, _, query, buffer) =>
            {
                ushort tx = (ushort)((query.Span[0] << 8) | query.Span[1]);
                byte[] response = VpnDnsWireFormat.BuildResponse(tx, 0x8183, 0, ReadOnlySpan<byte>.Empty);
                response.AsSpan().CopyTo(buffer.Span);
                return VpnDnsTransportResult.Received(response.Length);
            },
        };
        var resolver = new VpnInterfaceDnsResolver(
            tunnel,
            new FakeSessionDns([]),
            new FakeAdapterDns([IPAddress.Parse("10.0.0.2")]),
            transport,
            1000);

        VpnDnsResolutionResult result = await resolver.ResolveIpv4Async("missing.example.com");

        Assert.Equal(VpnDnsResolutionStatus.NxDomain, result.Status);
        Assert.Empty(result.Addresses);
    }

    [Fact]
    public async Task F_servfail_fail_closed()
    {
        var tunnel = new FakeTunnel(true, 3);
        var transport = new RecordingTransport
        {
            Handler = (_, _, query, buffer) =>
            {
                ushort tx = (ushort)((query.Span[0] << 8) | query.Span[1]);
                byte[] response = VpnDnsWireFormat.BuildResponse(tx, 0x8182, 0, ReadOnlySpan<byte>.Empty);
                response.AsSpan().CopyTo(buffer.Span);
                return VpnDnsTransportResult.Received(response.Length);
            },
        };
        var resolver = new VpnInterfaceDnsResolver(
            tunnel,
            new FakeSessionDns([]),
            new FakeAdapterDns([IPAddress.Parse("10.0.0.2")]),
            transport,
            1000);

        VpnDnsResolutionResult result = await resolver.ResolveIpv4Async("broken.example.com");

        Assert.Equal(VpnDnsResolutionStatus.ServFail, result.Status);
    }

    [Fact]
    public async Task G_transaction_id_mismatch_rejected()
    {
        var tunnel = new FakeTunnel(true, 1);
        var transport = new RecordingTransport
        {
            Handler = (_, _, _, buffer) =>
            {
                byte[] response = VpnDnsWireFormat.BuildResponse(
                    0xBEEF,
                    0x8180,
                    1,
                    ExampleA.GetAddressBytes());
                response.AsSpan().CopyTo(buffer.Span);
                return VpnDnsTransportResult.Received(response.Length);
            },
        };
        var resolver = new VpnInterfaceDnsResolver(
            tunnel,
            new FakeSessionDns([]),
            new FakeAdapterDns([IPAddress.Parse("10.0.0.3")]),
            transport,
            1000);

        VpnDnsResolutionResult result = await resolver.ResolveIpv4Async("example.com");

        Assert.Equal(VpnDnsResolutionStatus.TransactionIdMismatch, result.Status);
    }

    [Fact]
    public async Task H_malformed_packet_fail_closed()
    {
        var tunnel = new FakeTunnel(true, 1);
        var transport = new RecordingTransport
        {
            Handler = (_, _, _, buffer) =>
            {
                buffer.Span[0] = 0x01;
                return VpnDnsTransportResult.Received(1);
            },
        };
        var resolver = new VpnInterfaceDnsResolver(
            tunnel,
            new FakeSessionDns([]),
            new FakeAdapterDns([IPAddress.Parse("10.0.0.3")]),
            transport,
            1000);

        VpnDnsResolutionResult result = await resolver.ResolveIpv4Async("example.com");

        Assert.Equal(VpnDnsResolutionStatus.MalformedResponse, result.Status);
    }

    [Fact]
    public async Task I_second_vpn_dns_server_used_after_first_fails()
    {
        var tunnel = new FakeTunnel(true, 8);
        int calls = 0;
        var transport = new RecordingTransport
        {
            Handler = (_, server, query, buffer) =>
            {
                calls++;
                if (server.ToString() == "10.0.0.1")
                    return VpnDnsTransportResult.Failure(VpnDnsTransportOutcome.ReceiveFailed);

                ushort tx = (ushort)((query.Span[0] << 8) | query.Span[1]);
                byte[] response = VpnDnsWireFormat.BuildResponse(tx, 0x8180, 1, ExampleA.GetAddressBytes());
                response.AsSpan().CopyTo(buffer.Span);
                return VpnDnsTransportResult.Received(response.Length);
            },
        };
        var resolver = new VpnInterfaceDnsResolver(
            tunnel,
            new FakeSessionDns([]),
            new FakeAdapterDns([IPAddress.Parse("10.0.0.1"), IPAddress.Parse("10.0.0.2")]),
            transport,
            1000);

        VpnDnsResolutionResult result = await resolver.ResolveIpv4Async("example.com");

        Assert.True(result.IsSuccess);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task J_all_vpn_dns_servers_fail_closed()
    {
        var tunnel = new FakeTunnel(true, 8);
        var transport = new RecordingTransport
        {
            Handler = (_, _, _, _) => VpnDnsTransportResult.Failure(VpnDnsTransportOutcome.ReceiveFailed),
        };
        var resolver = new VpnInterfaceDnsResolver(
            tunnel,
            new FakeSessionDns([]),
            new FakeAdapterDns([IPAddress.Parse("10.0.0.1"), IPAddress.Parse("10.0.0.2")]),
            transport,
            1000);

        VpnDnsResolutionResult result = await resolver.ResolveIpv4Async("example.com");

        Assert.Equal(VpnDnsResolutionStatus.TransportError, result.Status);
        Assert.Equal(2, transport.QueryCount);
    }

    [Fact]
    public async Task K_bind_failure_after_readiness_fail_closed()
    {
        var tunnel = new FakeTunnel(true, 99);
        var transport = new RecordingTransport
        {
            Handler = (_, _, _, _) => VpnDnsTransportResult.Failure(VpnDnsTransportOutcome.BindFailed),
        };
        var resolver = new VpnInterfaceDnsResolver(
            tunnel,
            new FakeSessionDns([]),
            new FakeAdapterDns([IPAddress.Parse("10.0.0.1"), IPAddress.Parse("10.0.0.2")]),
            transport,
            1000);

        VpnDnsResolutionResult result = await resolver.ResolveIpv4Async("example.com");

        Assert.Equal(VpnDnsResolutionStatus.BindFailed, result.Status);
        Assert.Equal(1, transport.QueryCount);
    }

    [Fact]
    public void L_wire_parser_collects_a_records_only()
    {
        byte[] response = VpnDnsWireFormat.BuildResponse(0x1234, 0x8180, 1, ExampleA.GetAddressBytes());
        VpnDnsWireParseResult parsed = VpnDnsWireFormat.TryParseARecords(response, 0x1234);

        Assert.Equal(VpnDnsWireParseStatus.Success, parsed.Status);
        Assert.Single(parsed.Addresses);
        Assert.Equal(AddressFamily.InterNetwork, parsed.Addresses[0].AddressFamily);
    }

    [Theory]
    [InlineData("")]
    [InlineData(".example.com")]
    [InlineData("labeltoolonglabeltoolonglabeltoolonglabeltoolonglabeltoolonglabeltoolongx.com")]
    public async Task M_invalid_hostname_rejected(string host)
    {
        var resolver = new VpnInterfaceDnsResolver(
            new FakeTunnel(true, 1),
            new FakeSessionDns([]),
            new FakeAdapterDns([IPAddress.Parse("10.0.0.1")]),
            new RecordingTransport(),
            1000);

        VpnDnsResolutionResult result = await resolver.ResolveIpv4Async(host);

        Assert.Equal(VpnDnsResolutionStatus.InvalidHostname, result.Status);
    }

    [Fact]
    public void Wire_parser_rejects_truncated_flag()
    {
        byte[] response = VpnDnsWireFormat.BuildResponse(0x1111, 0x8300, 0, ReadOnlySpan<byte>.Empty);
        VpnDnsWireParseResult parsed = VpnDnsWireFormat.TryParseARecords(response, 0x1111);
        Assert.Equal(VpnDnsWireParseStatus.Truncated, parsed.Status);
    }

    [Fact]
    public void P_session_push_dns_wins_adapter_dns_not_queried()
    {
        var tunnel = new FakeTunnel(true, 7);
        var adapter = new TrackingAdapterDns([IPAddress.Parse("10.9.0.1")]);
        var resolver = new VpnInterfaceDnsResolver(
            tunnel,
            new FakeSessionDns([IPAddress.Parse("1.1.1.1")]),
            adapter,
            new RecordingTransport(),
            1000);

        IReadOnlyList<IPAddress> servers = resolver.ResolveDnsServers(7);

        Assert.Equal("1.1.1.1", servers[0].ToString());
        Assert.Equal(0, adapter.LookupCount);
    }

    [Fact]
    public void Q_session_empty_uses_adapter_dns()
    {
        var tunnel = new FakeTunnel(true, 7);
        var adapter = new TrackingAdapterDns([IPAddress.Parse("10.9.0.2")]);
        var resolver = new VpnInterfaceDnsResolver(
            tunnel,
            new FakeSessionDns([]),
            adapter,
            new RecordingTransport(),
            1000);

        IReadOnlyList<IPAddress> servers = resolver.ResolveDnsServers(7);

        Assert.Equal("10.9.0.2", servers[0].ToString());
        Assert.Equal(1, adapter.LookupCount);
    }

    [Fact]
    public async Task R_both_empty_returns_no_dns_servers_on_resolve()
    {
        var tunnel = new FakeTunnel(true, 3);
        var transport = new RecordingTransport();
        var resolver = new VpnInterfaceDnsResolver(
            tunnel,
            new FakeSessionDns([]),
            new FakeAdapterDns([]),
            transport,
            1000);

        VpnDnsResolutionResult result = await resolver.ResolveIpv4Async("example.com");

        Assert.Equal(VpnDnsResolutionStatus.NoDnsServers, result.Status);
        Assert.Equal(0, transport.QueryCount);
    }

    [Fact]
    public async Task S_pushed_dns_uses_current_vpn_if_index()
    {
        var tunnel = new FakeTunnel(true, 42);
        var transport = new RecordingTransport
        {
            Handler = (_, _, query, buffer) =>
            {
                ushort tx = (ushort)((query.Span[0] << 8) | query.Span[1]);
                byte[] response = VpnDnsWireFormat.BuildResponse(tx, 0x8180, 1, ExampleA.GetAddressBytes());
                response.AsSpan().CopyTo(buffer.Span);
                return VpnDnsTransportResult.Received(response.Length);
            },
        };
        var resolver = new VpnInterfaceDnsResolver(
            tunnel,
            new FakeSessionDns([IPAddress.Parse("1.0.0.1")]),
            new FakeAdapterDns([]),
            transport,
            5000);

        await resolver.ResolveIpv4Async("example.com");

        Assert.Equal(42, transport.LastInterfaceIndex);
    }

    [Fact]
    public async Task T_dns_still_uses_bound_udp_transport()
    {
        var tunnel = new FakeTunnel(true, 2);
        var transport = new RecordingTransport
        {
            Handler = (_, server, _, _) =>
            {
                Assert.Equal("9.9.9.9", server.ToString());
                return VpnDnsTransportResult.Failure(VpnDnsTransportOutcome.ReceiveFailed);
            },
        };
        var resolver = new VpnInterfaceDnsResolver(
            tunnel,
            new FakeSessionDns([IPAddress.Parse("9.9.9.9")]),
            new FakeAdapterDns([]),
            transport,
            1000);

        await resolver.ResolveIpv4Async("example.com");

        Assert.Equal(1, transport.QueryCount);
    }

    [Fact]
    public void Wire_parser_compression_pointer_safe()
    {
        byte[] response =
        [
            0x12, 0x34, 0x81, 0x80, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00,
            0x03, (byte)'w', (byte)'w', (byte)'w', 0x00, 0x00, 0x01, 0x00, 0x01,
            0xC0, 0x0C, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x3C, 0x00, 0x04,
            93, 184, 216, 34,
        ];
        VpnDnsWireParseResult parsed = VpnDnsWireFormat.TryParseARecords(response, 0x1234);
        Assert.Equal(VpnDnsWireParseStatus.Success, parsed.Status);
    }

    private sealed class FakeTunnel(bool ready, int ifIndex) : IVpnTunnelEgressReadiness
    {
        public bool TryGetTunnelInterfaceIndex(out int interfaceIndex)
        {
            interfaceIndex = ifIndex;
            return ready;
        }
    }

    private sealed class FakeSessionDns(IReadOnlyList<IPAddress> servers) : IVpnSessionDnsServers
    {
        public IReadOnlyList<IPAddress> GetIpv4DnsServers() => servers;
    }

    private sealed class FakeAdapterDns(IReadOnlyList<IPAddress> servers) : IVpnAdapterDnsServers
    {
        public IReadOnlyList<IPAddress> GetIpv4DnsServers(int interfaceIndex) => servers;
    }

    private sealed class TrackingAdapterDns(IReadOnlyList<IPAddress> servers) : IVpnAdapterDnsServers
    {
        public int LookupCount { get; private set; }

        public IReadOnlyList<IPAddress> GetIpv4DnsServers(int interfaceIndex)
        {
            LookupCount++;
            return servers;
        }
    }

    private sealed class RecordingTransport : IVpnDnsUdpTransport
    {
        public int QueryCount { get; private set; }

        public int LastInterfaceIndex { get; private set; }

        public Func<int, IPAddress, ReadOnlyMemory<byte>, Memory<byte>, VpnDnsTransportResult>? Handler { get; init; }

        public Func<int, IPAddress, ReadOnlyMemory<byte>, Memory<byte>, CancellationToken, Task<VpnDnsTransportResult>>?
            AsyncHandler { get; init; }

        public Task<VpnDnsTransportResult> QueryAsync(
            int interfaceIndex,
            IPAddress dnsServer,
            ReadOnlyMemory<byte> query,
            Memory<byte> receiveBuffer,
            CancellationToken cancellationToken)
        {
            QueryCount++;
            LastInterfaceIndex = interfaceIndex;
            if (AsyncHandler is not null)
                return AsyncHandler(interfaceIndex, dnsServer, query, receiveBuffer, cancellationToken);
            if (Handler is null)
                return Task.FromResult(VpnDnsTransportResult.Failure(VpnDnsTransportOutcome.ReceiveFailed));

            return Task.FromResult(Handler(interfaceIndex, dnsServer, query, receiveBuffer));
        }
    }
}
