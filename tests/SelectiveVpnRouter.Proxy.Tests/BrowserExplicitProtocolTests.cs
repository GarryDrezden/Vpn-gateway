using System.Net;
using System.Net.Sockets;
using System.Text;
using SelectiveVpnRouter.Proxy.BrowserExplicit;
using Xunit;

namespace SelectiveVpnRouter.Proxy.Tests;

public class BrowserExplicitProtocolTests
{
    [Fact]
    public async Task D_handshake_rejects_unsupported_auth_method()
    {
        var egress = new RecordingVpnEgress();
        await using var proxy = new BrowserExplicitSocksProxy(egress);
        await proxy.StartAsync(CancellationToken.None);
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, proxy.Port);
        using NetworkStream ns = client.GetStream();
        await ns.WriteAsync(new byte[] { 5, 1, 2 });
        var resp = new byte[2];
        Assert.True(await BrowserExplicitTestHelpers.ReadExactAsync(ns, resp));
        Assert.Equal(0xFF, resp[1]);
    }

    [Fact]
    public async Task H_bind_command_returns_command_not_supported()
    {
        var egress = new RecordingVpnEgress();
        await using var proxy = new BrowserExplicitSocksProxy(egress);
        await proxy.StartAsync(CancellationToken.None);
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, proxy.Port);
        using NetworkStream ns = client.GetStream();
        await BrowserExplicitTestHelpers.GreetAsync(ns);
        byte[] req = [5, 2, 0, 1, 127, 0, 0, 1, 0, 80];
        await ns.WriteAsync(req);
        var resp = new byte[10];
        await BrowserExplicitTestHelpers.ReadExactAsync(ns, resp);
        Assert.Equal(7, resp[1]);
        Assert.Empty(egress.Targets);
    }

    [Fact]
    public async Task I_udp_associate_returns_command_not_supported()
    {
        var egress = new RecordingVpnEgress();
        await using var proxy = new BrowserExplicitSocksProxy(egress);
        await proxy.StartAsync(CancellationToken.None);
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, proxy.Port);
        using NetworkStream ns = client.GetStream();
        await BrowserExplicitTestHelpers.GreetAsync(ns);
        byte[] req = [5, 3, 0, 1, 127, 0, 0, 1, 0, 80];
        await ns.WriteAsync(req);
        var resp = new byte[10];
        await BrowserExplicitTestHelpers.ReadExactAsync(ns, resp);
        Assert.Equal(7, resp[1]);
    }

    [Fact]
    public async Task J_unsupported_atyp_returns_address_type_not_supported()
    {
        var egress = new RecordingVpnEgress();
        await using var proxy = new BrowserExplicitSocksProxy(egress);
        await proxy.StartAsync(CancellationToken.None);
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, proxy.Port);
        using NetworkStream ns = client.GetStream();
        await BrowserExplicitTestHelpers.GreetAsync(ns);
        byte[] req = [5, 1, 0, 99, 127, 0, 0, 1, 0, 80];
        await ns.WriteAsync(req);
        var resp = new byte[10];
        await BrowserExplicitTestHelpers.ReadExactAsync(ns, resp);
        Assert.Equal(8, resp[1]);
        Assert.Empty(egress.Targets);
    }

    [Fact]
    public async Task K_malformed_greeting_closes_without_crashing_proxy()
    {
        var egress = new RecordingVpnEgress { Handler = _ => BrowserExplicitTestHelpers.ConnectToLoopbackEcho() };
        await using var proxy = new BrowserExplicitSocksProxy(egress);
        await proxy.StartAsync(CancellationToken.None);
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, proxy.Port);
        using NetworkStream ns = client.GetStream();
        await ns.WriteAsync(new byte[] { 4, 1, 0 });
        await Task.Delay(50);
        byte rep = await BrowserExplicitTestHelpers.ConnectDomainAsync(proxy.Port, "still.alive.test");
        Assert.Equal(0, rep);
    }

    [Fact]
    public async Task L_oversized_domain_rejected_without_egress()
    {
        var egress = new RecordingVpnEgress();
        await using var proxy = new BrowserExplicitSocksProxy(egress);
        await proxy.StartAsync(CancellationToken.None);
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, proxy.Port);
        using NetworkStream ns = client.GetStream();
        await BrowserExplicitTestHelpers.GreetAsync(ns);
        await ns.WriteAsync(new byte[] { 5, 1, 0, 3, 254 });
        Assert.Empty(egress.Targets);
    }

    [Fact]
    public async Task Vpn_unavailable_returns_network_unreachable_not_direct()
    {
        var egress = new RecordingVpnEgress();
        await using var proxy = new BrowserExplicitSocksProxy(egress);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await proxy.StartAsync(cts.Token);
        byte rep = await BrowserExplicitTestHelpers.ConnectIpv4Async(
            proxy.Port,
            IPAddress.Parse("127.0.0.1"),
            80);
        Assert.Equal(3, rep);
        Assert.Single(egress.Targets);
    }
}
