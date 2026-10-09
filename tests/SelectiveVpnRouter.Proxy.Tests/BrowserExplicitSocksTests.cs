using System.Net;
using System.Net.Sockets;
using System.Text;
using SelectiveVpnRouter.Proxy.BrowserExplicit;
using Xunit;

namespace SelectiveVpnRouter.Proxy.Tests;

public class BrowserExplicitSocksTests
{
    [Fact]
    public async Task A_listener_binds_loopback_only()
    {
        var egress = new RecordingVpnEgress();
        await using var proxy = new BrowserExplicitSocksProxy(egress);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await proxy.StartAsync(cts.Token);
        Assert.True(proxy.Port > 0);
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, proxy.Port);
    }

    [Fact]
    public async Task B_dynamic_port_non_zero()
    {
        var egress = new RecordingVpnEgress();
        await using var proxy = new BrowserExplicitSocksProxy(egress);
        await proxy.StartAsync(CancellationToken.None);
        Assert.True(proxy.Port > 0);
    }

    [Fact]
    public async Task C_socks5_no_auth_negotiation_succeeds()
    {
        var egress = new RecordingVpnEgress { Handler = _ => BrowserExplicitTestHelpers.ConnectToLoopbackEcho() };
        await using var proxy = new BrowserExplicitSocksProxy(egress);
        await proxy.StartAsync(CancellationToken.None);
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, proxy.Port);
        using NetworkStream ns = client.GetStream();
        await ns.WriteAsync(new byte[] { 5, 1, 0 });
        var hello = new byte[2];
        Assert.True(await BrowserExplicitTestHelpers.ReadExactAsync(ns, hello));
        Assert.Equal(5, hello[0]);
        Assert.Equal(0, hello[1]);
    }

    [Fact]
    public async Task E_connect_ipv4_success_rep_zero()
    {
        using var echo = new TcpListener(IPAddress.Loopback, 0);
        echo.Start();
        var endpoint = (IPEndPoint)echo.LocalEndpoint;
        _ = Task.Run(async () =>
        {
            using TcpClient c = await echo.AcceptTcpClientAsync();
            using NetworkStream s = c.GetStream();
            var buf = new byte[16];
            int n = await s.ReadAsync(buf);
            await s.WriteAsync(buf.AsMemory(0, n));
        });
        var egress = new RecordingVpnEgress
        {
            Handler = _ =>
            {
                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                socket.Connect(endpoint);
                return new VpnConnectOutcome(true, socket, Socks5ReplyCode.Success);
            },
        };
        await using var proxy = new BrowserExplicitSocksProxy(egress);
        await proxy.StartAsync(CancellationToken.None);
        byte rep = await BrowserExplicitTestHelpers.ConnectIpv4Async(proxy.Port, endpoint.Address, endpoint.Port);
        Assert.Equal(0, rep);
    }

    [Fact]
    public async Task F_connect_domain_success_uses_egress()
    {
        var egress = new RecordingVpnEgress { Handler = _ => BrowserExplicitTestHelpers.ConnectToLoopbackEcho() };
        await using var proxy = new BrowserExplicitSocksProxy(egress);
        await proxy.StartAsync(CancellationToken.None);
        byte rep = await BrowserExplicitTestHelpers.ConnectDomainAsync(proxy.Port, "www.example.com", 443);
        Assert.Equal(0, rep);
        Assert.Single(egress.Targets);
        Assert.Equal(VpnConnectTargetKind.Domain, egress.Targets[0].Kind);
        Assert.Equal("www.example.com", egress.Targets[0].Host);
    }

    [Fact]
    public async Task StartAsync_can_restart_listener_on_same_instance_after_stop()
    {
        var egress = new RecordingVpnEgress();
        var proxy = new BrowserExplicitSocksProxy(egress);
        using var cts = new CancellationTokenSource();
        await proxy.StartAsync(cts.Token);
        int firstPort = proxy.Port;
        await proxy.StopListeningAsync();
        await proxy.StartAsync(cts.Token);
        Assert.NotEqual(firstPort, proxy.Port);
        await proxy.DisposeAsync();
    }
}
