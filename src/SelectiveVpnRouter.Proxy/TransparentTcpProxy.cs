using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using SelectiveVpnRouter.Core;
using SelectiveVpnRouter.Network;

namespace SelectiveVpnRouter.Proxy;

/// <summary>
/// Local TCP relay. Accepts:
/// 1. WFP-redirected sockets (original destination in redirect context)
/// 2. Explicit SOCKS5 clients (Probe --via-proxy)
/// Outbound sockets are bound to the VPN interface with IP_UNICAST_IF when configured.
/// </summary>
public sealed class TransparentTcpProxy : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, FlowEvent> _flows = new();
    private int _acceptedConnections;
    private int _redirectContextQueries;
    private int _redirectContextSuccess;
    private int _redirectContextFailures;
    private int _lastRedirectContextError;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _accept;

    public int Port { get; private set; }
    public TransparentProxyDiagnostics Diagnostics => new()
    {
        AcceptedConnections = (uint)Volatile.Read(ref _acceptedConnections),
        RedirectContextQueries = (uint)Volatile.Read(ref _redirectContextQueries),
        RedirectContextSuccess = (uint)Volatile.Read(ref _redirectContextSuccess),
        RedirectContextFailures = (uint)Volatile.Read(ref _redirectContextFailures),
        LastRedirectContextError = Volatile.Read(ref _lastRedirectContextError),
    };
    public int? VpnInterfaceIndex { get; set; }
    public string? VpnInterfaceName { get; set; }
    public bool BindOutboundToVpn { get; set; } = true;

    public event Action<FlowEvent>? FlowChanged;

    public IReadOnlyList<FlowEvent> Flows => _flows.Values.OrderByDescending(f => f.Time).Take(200).ToList();

    public async Task StartAsync(IPAddress listenAddress, int port, CancellationToken ct)
    {
        _listener = new TcpListener(listenAddress, port);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _accept = AcceptLoop(_cts.Token);
        await Task.CompletedTask.ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            _cts?.Cancel();
            _listener?.Stop();
            if (_accept is not null)
            {
                await _accept.ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
        }

        _cts?.Dispose();
    }

    private async Task AcceptLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener is not null)
        {
            TcpClient incoming;
            try
            {
                incoming = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            _ = Task.Run(() => HandleAsync(incoming, ct), ct);
        }
    }

    private async Task HandleAsync(TcpClient incoming, CancellationToken ct)
    {
        Interlocked.Increment(ref _acceptedConnections);
        using TcpClient client = incoming;
        client.NoDelay = true;
        Socket accepted = client.Client;
        IPEndPoint? original = null;
        byte[] records = [];
        NetworkStream inbound = client.GetStream();
        byte[] peek = new byte[1];
        bool socks = false;
        try
        {
            int peeked = accepted.Receive(peek, 0, 1, SocketFlags.Peek);
            socks = peeked == 1 && peek[0] == 5;
        }
        catch (SocketException)
        {
        }

        if (socks)
        {
            original = await Socks5.TryHandshakeAsync(inbound, ct).ConfigureAwait(false);
        }
        else
        {
            try
            {
                Interlocked.Increment(ref _redirectContextQueries);
                records = WfpRedirectSockets.QueryRedirectRecords(accepted);
                byte[] ctx = WfpRedirectSockets.QueryRedirectContext(accepted);
                if (WfpRedirectSockets.TryParseContext(ctx, out IPEndPoint parsed))
                {
                    Interlocked.Increment(ref _redirectContextSuccess);
                    original = parsed;
                }
                else
                {
                    Interlocked.Increment(ref _redirectContextFailures);
                    Volatile.Write(ref _lastRedirectContextError, ctx.Length == 0 ? -1 : -2);
                }
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _redirectContextFailures);
                Volatile.Write(ref _lastRedirectContextError, ex switch
                {
                    SocketException se => se.ErrorCode,
                    _ => ex.HResult,
                });
            }
        }

        if (original is null)
        {
            return;
        }

        if (original.Address.Equals(IPAddress.Loopback) && original.Port == Port)
        {
            Publish(new FlowEvent
            {
                Destination = original.Address.ToString(),
                Port = original.Port,
                Route = FlowRoute.Blocked,
                Status = "loop-rejected",
            });
            return;
        }

        int pid = 0;
        string processPath = "";
        try
        {
            if (accepted.RemoteEndPoint is IPEndPoint peer)
            {
                pid = TcpOwnerTable.PidForLocal(peer) ?? 0;
                processPath = ProcessPathResolver.TryGetPath(pid) ?? "";
            }
        }
        catch (Exception)
        {
        }

        var flow = new FlowEvent
        {
            ProcessPath = processPath,
            Pid = pid,
            Destination = original.Address.ToString(),
            Port = original.Port,
            Protocol = "TCP",
            Route = FlowRoute.Vpn,
            LocalInterface = VpnInterfaceName,
            WfpRedirect = !socks && records.Length > 0,
            RedirectRecordsApplied = false,
            Status = "connecting",
        };
        Publish(flow);

        if (!socks && pid == Environment.ProcessId)
        {
            Publish(flow with { Status = "proxy-self-rejected", Route = FlowRoute.Blocked });
            return;
        }

        try
        {
            using var outbound = new Socket(original.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            outbound.NoDelay = true;
            if (BindOutboundToVpn && VpnInterfaceIndex is int idx && idx > 0)
            {
                if (original.AddressFamily == AddressFamily.InterNetwork)
                {
                    SocketInterfaceBinder.BindIpv4UnicastIf(outbound, idx);
                }
                else
                {
                    SocketInterfaceBinder.BindIpv6UnicastIf(outbound, idx);
                }
            }

            if (records.Length > 0)
            {
                try
                {
                    WfpRedirectSockets.SetRedirectRecords(outbound, records);
                    flow = flow with { RedirectRecordsApplied = true };
                    Publish(flow);
                }
                catch (SocketException)
                {
                }
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(20));
            await outbound.ConnectAsync(original, timeoutCts.Token).ConfigureAwait(false);
            flow = flow with { Status = "open" };
            Publish(flow);

            using NetworkStream outboundStream = new(outbound, ownsSocket: false);
            Task a = inbound.CopyToAsync(outboundStream, ct);
            Task b = outboundStream.CopyToAsync(inbound, ct);
            await Task.WhenAny(a, b).ConfigureAwait(false);
            flow = flow with { Status = "closed" };
            Publish(flow);
        }
        catch (Exception ex)
        {
            Publish(flow with { Status = "error: " + ex.GetType().Name });
        }
    }

    private void Publish(FlowEvent flow)
    {
        string key = flow.Pid + "|" + flow.Destination + "|" + flow.Port + "|" + flow.Time.Ticks;
        _flows[key] = flow;
        while (_flows.Count > 300)
        {
            string? oldest = _flows.OrderBy(kv => kv.Value.Time).Select(kv => kv.Key).FirstOrDefault();
            if (oldest is null)
            {
                break;
            }

            _flows.TryRemove(oldest, out _);
        }

        FlowChanged?.Invoke(flow);
    }
}

internal static class Socks5
{
    public static async Task<IPEndPoint?> TryHandshakeAsync(NetworkStream stream, CancellationToken ct)
    {
        var buf = new byte[512];
        int n = await ReadAtLeastAsync(stream, buf, 2, ct).ConfigureAwait(false);
        if (n < 2 || buf[0] != 5)
        {
            return null;
        }

        int methods = buf[1];
        if (methods > 0)
        {
            await ReadAtLeastAsync(stream, buf, methods, ct).ConfigureAwait(false);
        }

        await stream.WriteAsync(new byte[] { 5, 0 }, ct).ConfigureAwait(false);

        n = await ReadAtLeastAsync(stream, buf, 4, ct).ConfigureAwait(false);
        if (n < 4 || buf[0] != 5 || buf[1] != 1)
        {
            await stream.WriteAsync(new byte[] { 5, 7, 0, 1, 0, 0, 0, 0, 0, 0 }, ct).ConfigureAwait(false);
            return null;
        }

        IPAddress ip;
        int atyp = buf[3];
        if (atyp == 1)
        {
            await ReadAtLeastAsync(stream, buf, 6, ct).ConfigureAwait(false);
            ip = new IPAddress(buf.AsSpan(0, 4).ToArray());
            int port = (buf[4] << 8) | buf[5];
            await ReplyAsync(stream, ct).ConfigureAwait(false);
            return new IPEndPoint(ip, port);
        }

        if (atyp == 3)
        {
            await ReadAtLeastAsync(stream, buf, 1, ct).ConfigureAwait(false);
            int len = buf[0];
            await ReadAtLeastAsync(stream, buf, len + 2, ct).ConfigureAwait(false);
            string host = System.Text.Encoding.ASCII.GetString(buf, 0, len);
            int port = (buf[len] << 8) | buf[len + 1];
            IPAddress[] addrs = await Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork, ct).ConfigureAwait(false);
            if (addrs.Length == 0)
            {
                await stream.WriteAsync(new byte[] { 5, 4, 0, 1, 0, 0, 0, 0, 0, 0 }, ct).ConfigureAwait(false);
                return null;
            }

            await ReplyAsync(stream, ct).ConfigureAwait(false);
            return new IPEndPoint(addrs[0], port);
        }

        await stream.WriteAsync(new byte[] { 5, 8, 0, 1, 0, 0, 0, 0, 0, 0 }, ct).ConfigureAwait(false);
        return null;
    }

    private static async Task ReplyAsync(NetworkStream stream, CancellationToken ct)
        => await stream.WriteAsync(new byte[] { 5, 0, 0, 1, 0, 0, 0, 0, 0, 0 }, ct).ConfigureAwait(false);

    private static async Task<int> ReadAtLeastAsync(NetworkStream stream, byte[] buf, int count, CancellationToken ct)
    {
        int got = 0;
        while (got < count)
        {
            int n = await stream.ReadAsync(buf.AsMemory(got, count - got), ct).ConfigureAwait(false);
            if (n == 0)
            {
                return got;
            }

            got += n;
        }

        return got;
    }
}
