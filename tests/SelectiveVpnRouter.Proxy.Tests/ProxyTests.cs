using System.Net;
using System.Net.Sockets;
using SelectiveVpnRouter.Proxy;
using Xunit;

namespace SelectiveVpnRouter.Proxy.Tests;

public class ProxyForwardingTests
{
    [Fact]
    public async Task Socks5_relays_to_local_echo_server()
    {
        using var echo = new TcpListener(IPAddress.Loopback, 0);
        echo.Start();
        int echoPort = ((IPEndPoint)echo.LocalEndpoint).Port;
        var echoTask = Task.Run(async () =>
        {
            using TcpClient c = await echo.AcceptTcpClientAsync();
            using NetworkStream s = c.GetStream();
            var buf = new byte[64];
            int n = await s.ReadAsync(buf);
            await s.WriteAsync(buf.AsMemory(0, n));
        });

        await using var proxy = new TransparentTcpProxy { BindOutboundToVpn = false };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await proxy.StartAsync(IPAddress.Loopback, 0, cts.Token);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, proxy.Port);
        using NetworkStream ns = client.GetStream();
        await ns.WriteAsync(new byte[] { 5, 1, 0 });
        var hello = new byte[2];
        Assert.Equal(2, await ns.ReadAsync(hello));
        Assert.Equal(5, hello[0]);

        byte[] req =
        [
            5, 1, 0, 1,
            127, 0, 0, 1,
            (byte)(echoPort >> 8), (byte)(echoPort & 0xFF)
        ];
        await ns.WriteAsync(req);
        var resp = new byte[10];
        int got = await ns.ReadAsync(resp);
        Assert.True(got >= 2);
        Assert.Equal(0, resp[1]);

        byte[] payload = "ping"u8.ToArray();
        await ns.WriteAsync(payload);
        var back = new byte[16];
        int n = await ns.ReadAsync(back);
        Assert.Equal("ping", System.Text.Encoding.ASCII.GetString(back, 0, n));
        await echoTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Multiple_concurrent_echo_connections()
    {
        using var echo = new TcpListener(IPAddress.Loopback, 0);
        echo.Start();
        int echoPort = ((IPEndPoint)echo.LocalEndpoint).Port;
        _ = Task.Run(async () =>
        {
            while (true)
            {
                TcpClient c = await echo.AcceptTcpClientAsync();
                _ = Task.Run(async () =>
                {
                    using TcpClient inner = c;
                    using NetworkStream s = inner.GetStream();
                    var buf = new byte[32];
                    int n = await s.ReadAsync(buf);
                    await s.WriteAsync(buf.AsMemory(0, n));
                });
            }
        });

        await using var proxy = new TransparentTcpProxy { BindOutboundToVpn = false };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await proxy.StartAsync(IPAddress.Loopback, 0, cts.Token);

        Task[] tasks = Enumerable.Range(0, 8).Select(i => OneSocks(proxy.Port, echoPort, (byte)i)).ToArray();
        await Task.WhenAll(tasks);
    }

    private static async Task OneSocks(int proxyPort, int echoPort, byte token)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, proxyPort);
        using NetworkStream ns = client.GetStream();
        await ns.WriteAsync(new byte[] { 5, 1, 0 });
        var hello = new byte[2];
        await ns.ReadAsync(hello);
        await ns.WriteAsync(new byte[] { 5, 1, 0, 1, 127, 0, 0, 1, (byte)(echoPort >> 8), (byte)(echoPort & 0xFF) });
        var resp = new byte[10];
        await ns.ReadAsync(resp);
        await ns.WriteAsync(new byte[] { token });
        var back = new byte[8];
        int n = await ns.ReadAsync(back);
        Assert.True(n >= 1);
        Assert.Equal(token, back[0]);
    }

    [Fact]
    public async Task Socks5_to_proxy_listen_port_is_rejected_as_loop()
    {
        await using var proxy = new TransparentTcpProxy { BindOutboundToVpn = false };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await proxy.StartAsync(IPAddress.Loopback, 0, cts.Token);
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, proxy.Port);
        using NetworkStream ns = client.GetStream();
        await ns.WriteAsync(new byte[] { 5, 1, 0 });
        var hello = new byte[2];
        await ns.ReadAsync(hello);
        await ns.WriteAsync(new byte[]
        {
            5, 1, 0, 1, 127, 0, 0, 1,
            (byte)(proxy.Port >> 8), (byte)(proxy.Port & 0xFF),
        });
        await Task.Delay(400);
        Assert.Contains(proxy.Flows, f => f.Status == "loop-rejected");
    }
}
