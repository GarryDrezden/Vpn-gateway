using System.Net;
using System.Net.Sockets;
using SelectiveVpnRouter.Proxy.BrowserExplicit;
using Xunit;

namespace SelectiveVpnRouter.Proxy.Tests;

public class BrowserExplicitRelayTests
{
    [Fact]
    public async Task V_relay_transfers_bytes_both_directions()
    {
        using var echo = new TcpListener(IPAddress.Loopback, 0);
        echo.Start();
        var endpoint = (IPEndPoint)echo.LocalEndpoint;
        _ = Task.Run(async () =>
        {
            using TcpClient c = await echo.AcceptTcpClientAsync();
            using NetworkStream s = c.GetStream();
            var buf = new byte[32];
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
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, proxy.Port);
        using NetworkStream ns = client.GetStream();
        await BrowserExplicitTestHelpers.GreetAsync(ns);
        byte[] addr = endpoint.Address.GetAddressBytes();
        byte[] req =
        [
            5, 1, 0, 1, addr[0], addr[1], addr[2], addr[3],
            (byte)(endpoint.Port >> 8), (byte)(endpoint.Port & 0xFF),
        ];
        await ns.WriteAsync(req);
        var resp = new byte[10];
        await BrowserExplicitTestHelpers.ReadExactAsync(ns, resp);
        Assert.Equal(0, resp[1]);
        await ns.WriteAsync("ping"u8.ToArray());
        var back = new byte[8];
        int got = await ns.ReadAsync(back);
        Assert.Equal("ping", System.Text.Encoding.ASCII.GetString(back, 0, got));
    }

    [Fact]
    public async Task W_client_eof_closes_session()
    {
        using var echo = new TcpListener(IPAddress.Loopback, 0);
        echo.Start();
        var endpoint = (IPEndPoint)echo.LocalEndpoint;
        var echoAccepted = new TaskCompletionSource();
        _ = Task.Run(async () =>
        {
            using TcpClient c = await echo.AcceptTcpClientAsync();
            echoAccepted.SetResult();
            using NetworkStream s = c.GetStream();
            var buf = new byte[8];
            int n = await s.ReadAsync(buf);
            Assert.Equal(0, n);
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
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, proxy.Port);
        using NetworkStream ns = client.GetStream();
        await BrowserExplicitTestHelpers.GreetAsync(ns);
        byte[] addr = endpoint.Address.GetAddressBytes();
        byte[] connectReq =
        [
            5, 1, 0, 1, addr[0], addr[1], addr[2], addr[3],
            (byte)(endpoint.Port >> 8), (byte)(endpoint.Port & 0xFF),
        ];
        await ns.WriteAsync(connectReq);
        var resp = new byte[10];
        await BrowserExplicitTestHelpers.ReadExactAsync(ns, resp);
        ns.Close();
        await echoAccepted.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task X_remote_eof_closes_session()
    {
        using var echo = new TcpListener(IPAddress.Loopback, 0);
        echo.Start();
        var endpoint = (IPEndPoint)echo.LocalEndpoint;
        _ = Task.Run(async () =>
        {
            using TcpClient c = await echo.AcceptTcpClientAsync();
            c.Client.Shutdown(SocketShutdown.Send);
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
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, proxy.Port);
        using NetworkStream ns = client.GetStream();
        await BrowserExplicitTestHelpers.GreetAsync(ns);
        byte[] addr = endpoint.Address.GetAddressBytes();
        byte[] connectReq =
        [
            5, 1, 0, 1, addr[0], addr[1], addr[2], addr[3],
            (byte)(endpoint.Port >> 8), (byte)(endpoint.Port & 0xFF),
        ];
        await ns.WriteAsync(connectReq);
        var resp = new byte[10];
        await BrowserExplicitTestHelpers.ReadExactAsync(ns, resp);
        var buf = new byte[8];
        int n = await ns.ReadAsync(buf);
        Assert.Equal(0, n);
    }

    [Fact]
    public async Task Y_stop_stops_accepting_new_clients()
    {
        var egress = new RecordingVpnEgress { Handler = _ => BrowserExplicitTestHelpers.ConnectToLoopbackEcho() };
        var proxy = new BrowserExplicitSocksProxy(egress);
        await proxy.StartAsync(CancellationToken.None);
        int port = proxy.Port;
        await proxy.StopListeningAsync();
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port).WaitAsync(TimeSpan.FromSeconds(2));
        });
        await proxy.DisposeAsync();
    }

    [Fact]
    public async Task Z_stop_cleans_active_sessions_without_unobserved_exceptions()
    {
        using var echo = new TcpListener(IPAddress.Loopback, 0);
        echo.Start();
        var endpoint = (IPEndPoint)echo.LocalEndpoint;
        _ = Task.Run(async () =>
        {
            using TcpClient c = await echo.AcceptTcpClientAsync();
            await Task.Delay(500);
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
        var proxy = new BrowserExplicitSocksProxy(egress);
        using var cts = new CancellationTokenSource();
        await proxy.StartAsync(cts.Token);
        _ = Task.Run(async () =>
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, proxy.Port);
            using NetworkStream ns = client.GetStream();
            await BrowserExplicitTestHelpers.GreetAsync(ns);
            byte[] addr = endpoint.Address.GetAddressBytes();
            byte[] connectReq =
            [
                5, 1, 0, 1, addr[0], addr[1], addr[2], addr[3],
                (byte)(endpoint.Port >> 8), (byte)(endpoint.Port & 0xFF),
            ];
            await ns.WriteAsync(connectReq);
            await Task.Delay(Timeout.Infinite, cts.Token);
        });
        await Task.Delay(100);
        cts.Cancel();
        await proxy.StopListeningAsync();
        await proxy.DisposeAsync();
    }

    [Fact]
    public async Task AA_concurrency_cap_enforced()
    {
        var gate = new TaskCompletionSource();
        var egress = new RecordingVpnEgress
        {
            Handler = _ =>
            {
                gate.Task.Wait(TimeSpan.FromSeconds(30));
                return new VpnConnectOutcome(false, null, Socks5ReplyCode.GeneralFailure);
            },
        };
        await using var proxy = new BrowserExplicitSocksProxy(egress);
        await proxy.StartAsync(CancellationToken.None);
        var clients = new List<TcpClient>();
        try
        {
            for (int i = 0; i < BrowserExplicitSocksProxy.MaxConcurrentConnections + 5; i++)
            {
                var client = new TcpClient();
                try
                {
                    await client.ConnectAsync(IPAddress.Loopback, proxy.Port).WaitAsync(TimeSpan.FromSeconds(2));
                    clients.Add(client);
                    using NetworkStream ns = client.GetStream();
                    await ns.WriteAsync(new byte[] { 5, 1, 0 });
                    var hello = new byte[2];
                    await BrowserExplicitTestHelpers.ReadExactAsync(ns, hello);
                    byte[] req = [5, 1, 0, 1, 127, 0, 0, 1, 0, 80];
                    await ns.WriteAsync(req);
                }
                catch
                {
                    client.Dispose();
                }
            }

            Assert.True(proxy.ActiveConnections <= BrowserExplicitSocksProxy.MaxConcurrentConnections);
        }
        finally
        {
            gate.SetResult();
            foreach (TcpClient c in clients)
                c.Dispose();
        }
    }
}
