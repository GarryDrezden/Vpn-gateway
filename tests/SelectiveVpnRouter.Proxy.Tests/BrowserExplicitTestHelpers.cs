using System.Net;
using System.Net.Sockets;
using SelectiveVpnRouter.Proxy.BrowserExplicit;

namespace SelectiveVpnRouter.Proxy.Tests;

public sealed class RecordingVpnEgress : IVpnTcpEgress
{
    private readonly object _targetsGate = new();
    public readonly List<VpnConnectTarget> Targets = [];
    public Func<VpnConnectTarget, VpnConnectOutcome>? Handler;

    public ValueTask<VpnConnectOutcome> ConnectAsync(VpnConnectTarget target, CancellationToken cancellationToken)
    {
        lock (_targetsGate)
        {
            Targets.Add(target);
        }

        if (Handler is not null)
            return ValueTask.FromResult(Handler(target));
        return ValueTask.FromResult(new VpnConnectOutcome(false, null, Socks5ReplyCode.NetworkUnreachable));
    }
}

internal static class BrowserExplicitTestHelpers
{
    public static async Task GreetAsync(NetworkStream ns)
    {
        await ns.WriteAsync(new byte[] { 5, 1, 0 });
        var hello = new byte[2];
        await ReadExactAsync(ns, hello);
    }

    public static async Task<bool> ReadExactAsync(NetworkStream ns, byte[] buffer)
    {
        int got = 0;
        while (got < buffer.Length)
        {
            int n = await ns.ReadAsync(buffer.AsMemory(got, buffer.Length - got));
            if (n == 0)
                return false;
            got += n;
        }

        return true;
    }

    public static VpnConnectOutcome ConnectToLoopbackEcho()
    {
        var echo = new TcpListener(IPAddress.Loopback, 0);
        echo.Start();
        var endpoint = (IPEndPoint)echo.LocalEndpoint;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                using TcpClient peer = echo.AcceptTcpClient();
                peer.GetStream().Write([1]);
            }
            catch
            {
            }
        });
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Connect(endpoint);
        return new VpnConnectOutcome(true, socket, Socks5ReplyCode.Success);
    }

    public static async Task<byte> ConnectIpv4Async(int port, IPAddress target, int targetPort)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        using NetworkStream ns = client.GetStream();
        await GreetAsync(ns);
        byte[] addr = target.GetAddressBytes();
        byte[] req =
        [
            5, 1, 0, 1,
            addr[0], addr[1], addr[2], addr[3],
            (byte)(targetPort >> 8), (byte)(targetPort & 0xFF),
        ];
        await ns.WriteAsync(req);
        var resp = new byte[10];
        await ReadExactAsync(ns, resp);
        return resp[1];
    }

    public static async Task<byte> ConnectDomainAsync(int port, string host, int targetPort = 80)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        using NetworkStream ns = client.GetStream();
        await GreetAsync(ns);
        byte[] name = System.Text.Encoding.ASCII.GetBytes(host);
        byte[] req = new byte[7 + name.Length];
        req[0] = 5;
        req[1] = 1;
        req[3] = 3;
        req[4] = (byte)name.Length;
        name.CopyTo(req, 5);
        req[5 + name.Length] = (byte)(targetPort >> 8);
        req[6 + name.Length] = (byte)(targetPort & 0xFF);
        await ns.WriteAsync(req);
        var resp = new byte[10];
        await ReadExactAsync(ns, resp);
        return resp[1];
    }
}
