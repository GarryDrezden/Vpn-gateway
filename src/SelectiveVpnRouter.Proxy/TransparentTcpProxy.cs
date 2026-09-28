using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using SelectiveVpnRouter.Core;
using SelectiveVpnRouter.Network;

namespace SelectiveVpnRouter.Proxy;

public sealed class TransparentTcpProxy : IAsyncDisposable
{
    private readonly ConcurrentDictionary<Guid, FlowEvent> _flows = new();
    private long _flowSequence;
    private int _acceptedConnections;
    private int _redirectContextQueries;
    private int _redirectContextSuccess;
    private int _redirectContextFailures;
    private int _lastRedirectContextError;
    private TcpListener? _listener;
    private CancellationTokenSource? _lifetimeCts;
    private CancellationToken _lifetimeToken;
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

    public IReadOnlyList<FlowEvent> Flows =>
        _flows.Values.OrderByDescending(f => f.UpdatedAt).ThenByDescending(f => f.SequenceId).Take(200).ToList();

    public async Task StartAsync(IPAddress listenAddress, int port, CancellationToken lifetimeToken)
    {
        _lifetimeToken = lifetimeToken;
        _lifetimeCts = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
        _listener = new TcpListener(listenAddress, port);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _accept = AcceptLoop(_lifetimeCts.Token);
        await Task.CompletedTask.ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            _lifetimeCts?.Cancel();
            _listener?.Stop();
            if (_accept is not null)
            {
                await _accept.ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
        }

        _lifetimeCts?.Dispose();
    }

    private async Task AcceptLoop(CancellationToken acceptToken)
    {
        while (!acceptToken.IsCancellationRequested && _listener is not null)
        {
            TcpClient incoming;
            try
            {
                incoming = await _listener.AcceptTcpClientAsync(acceptToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            _ = Task.Run(() => HandleAsync(incoming));
        }
    }

    private async Task HandleAsync(TcpClient incoming)
    {
        Interlocked.Increment(ref _acceptedConnections);
        using TcpClient client = incoming;
        client.NoDelay = true;
        Socket accepted = client.Client;
        IPEndPoint? original = null;
        byte[] records = [];
        NetworkStream inbound = client.GetStream();
        string phase = "accept";
        Guid flowId = Guid.NewGuid();
        long sequenceId = Interlocked.Increment(ref _flowSequence);
        byte[] peek = new byte[1];
        bool socks = false;
        try
        {
            int peeked = accepted.Receive(peek, 0, 1, SocketFlags.Peek);
            socks = peeked == 1 && peek[0] == 5;
        }
        catch (SocketException ex)
        {
            PublishTerminalError(new FlowEvent { FlowId = flowId, SequenceId = sequenceId }, phase, ex, null);
            return;
        }

        if (socks)
        {
            original = await Socks5.TryHandshakeAsync(inbound, _lifetimeToken).ConfigureAwait(false);
        }
        else
        {
            try
            {
                phase = "query-redirect-records";
                Interlocked.Increment(ref _redirectContextQueries);
                records = WfpRedirectSockets.QueryRedirectRecords(accepted);

                phase = "query-redirect-context";
                byte[] ctx = WfpRedirectSockets.QueryRedirectContext(accepted);

                phase = "parse-context";
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
                PublishTerminalError(new FlowEvent { FlowId = flowId, SequenceId = sequenceId }, phase, ex, null);
                return;
            }
        }

        if (original is null)
        {
            return;
        }

        string? bypassReason = FlowStatusHelper.TryGetBypassReason(original, socks, Port);
        if (bypassReason is not null)
        {
            Publish(new FlowEvent
            {
                FlowId = flowId,
                SequenceId = sequenceId,
                Destination = original.Address.ToString(),
                Port = original.Port,
                Route = FlowRoute.Blocked,
                BypassReason = bypassReason,
                Status = FlowStatusHelper.FormatBypassStatus(bypassReason),
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

        if (!socks && pid == Environment.ProcessId)
        {
            Publish(new FlowEvent
            {
                FlowId = flowId,
                SequenceId = sequenceId,
                ProcessPath = processPath,
                Pid = pid,
                Destination = original.Address.ToString(),
                Port = original.Port,
                Route = FlowRoute.Blocked,
                BypassReason = "proxy-pid",
                Status = FlowLifecycle.ProxySelfRejected,
            });
            return;
        }

        FlowEvent flow = CreateFlow(flowId, sequenceId, processPath, pid, original, socks, records);
        Publish(flow with { Status = FlowLifecycle.Accepted, ProxyAccepted = true });

        if (records.Length > 0)
        {
            Publish(flow with
            {
                Status = FlowLifecycle.RedirectContextRecovered,
                ProxyAccepted = true,
                RedirectRecordsApplied = true,
            });
        }

        try
        {
            phase = "create-outbound";
            using var outbound = new Socket(original.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            outbound.NoDelay = true;
            Publish(flow with
            {
                Status = FlowLifecycle.OutboundCreated,
                ProxyAccepted = true,
                RedirectRecordsApplied = records.Length > 0,
                VpnOutboundCreated = true,
                VpnInterfaceIndex = VpnInterfaceIndex,
            });

            string? localBind = null;
            if (BindOutboundToVpn && VpnInterfaceIndex is int idx && idx > 0)
            {
                phase = "bind-vpn";
                if (original.AddressFamily == AddressFamily.InterNetwork)
                {
                    SocketInterfaceBinder.BindIpv4UnicastIf(outbound, idx);
                }
                else
                {
                    SocketInterfaceBinder.BindIpv6UnicastIf(outbound, idx);
                }

                localBind = outbound.LocalEndPoint?.ToString();
                Publish(flow with
                {
                    Status = FlowLifecycle.OutboundBound,
                    ProxyAccepted = true,
                    RedirectRecordsApplied = records.Length > 0,
                    VpnOutboundCreated = true,
                    VpnOutboundBound = true,
                    VpnInterfaceIndex = idx,
                    OutboundLocalEndpoint = localBind,
                });
            }

            if (records.Length > 0)
            {
                phase = "set-redirect-records";
                WfpRedirectSockets.SetRedirectRecords(outbound, records);
            }

            phase = "connect-original-destination";
            Publish(flow with
            {
                Status = FlowLifecycle.Connecting,
                ProxyAccepted = true,
                RedirectRecordsApplied = records.Length > 0,
                VpnOutboundCreated = true,
                VpnOutboundBound = BindOutboundToVpn && VpnInterfaceIndex > 0,
                VpnInterfaceIndex = VpnInterfaceIndex,
                OutboundLocalEndpoint = localBind,
            });

            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeToken);
            connectCts.CancelAfter(TimeSpan.FromMilliseconds(FlowStatusHelper.DefaultConnectTimeoutMs));
            try
            {
                await outbound.ConnectAsync(original, connectCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex)
            {
                bool timeout = connectCts.IsCancellationRequested && !_lifetimeToken.IsCancellationRequested;
                bool serviceStopping = _lifetimeToken.IsCancellationRequested;
                PublishTerminalError(
                    flow with
                    {
                        ProxyAccepted = true,
                        RedirectRecordsApplied = records.Length > 0,
                        VpnOutboundCreated = true,
                        VpnOutboundBound = BindOutboundToVpn && VpnInterfaceIndex > 0,
                        VpnInterfaceIndex = VpnInterfaceIndex,
                        OutboundLocalEndpoint = localBind,
                        Destination = original.Address.ToString(),
                        Port = original.Port,
                    },
                    phase,
                    ex,
                    new ConnectErrorContext(original, localBind, timeout, serviceStopping));
                return;
            }

            string remote = outbound.RemoteEndPoint?.ToString() ?? original.ToString();
            Publish(flow with
            {
                Status = FlowLifecycle.Connected,
                ProxyAccepted = true,
                RedirectRecordsApplied = records.Length > 0,
                VpnOutboundCreated = true,
                VpnOutboundBound = BindOutboundToVpn && VpnInterfaceIndex > 0,
                VpnOutboundConnected = true,
                VpnInterfaceIndex = VpnInterfaceIndex,
                OutboundLocalEndpoint = outbound.LocalEndPoint?.ToString(),
                OutboundRemoteEndpoint = remote,
            });

            phase = "copy-client-to-remote";
            Publish(flow with
            {
                Status = FlowLifecycle.Relaying,
                ProxyAccepted = true,
                RedirectRecordsApplied = records.Length > 0,
                VpnOutboundCreated = true,
                VpnOutboundBound = BindOutboundToVpn && VpnInterfaceIndex > 0,
                VpnOutboundConnected = true,
                VpnInterfaceIndex = VpnInterfaceIndex,
                OutboundLocalEndpoint = outbound.LocalEndPoint?.ToString(),
                OutboundRemoteEndpoint = remote,
            });

            using NetworkStream outboundStream = new(outbound, ownsSocket: false);
            using var relayCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeToken);
            Task a = inbound.CopyToAsync(outboundStream, relayCts.Token);
            Task b = outboundStream.CopyToAsync(inbound, relayCts.Token);
            await Task.WhenAny(a, b).ConfigureAwait(false);
            Publish(flow with
            {
                Status = FlowLifecycle.Closed,
                ProxyAccepted = true,
                RedirectRecordsApplied = records.Length > 0,
                VpnOutboundCreated = true,
                VpnOutboundBound = BindOutboundToVpn && VpnInterfaceIndex > 0,
                VpnOutboundConnected = true,
                VpnInterfaceIndex = VpnInterfaceIndex,
                OutboundLocalEndpoint = outbound.LocalEndPoint?.ToString(),
                OutboundRemoteEndpoint = remote,
            });
        }
        catch (Exception ex)
        {
            PublishTerminalError(flow, phase, ex, null);
        }
    }

    private static FlowEvent CreateFlow(
        Guid flowId,
        long sequenceId,
        string processPath,
        int pid,
        IPEndPoint original,
        bool socks,
        byte[] records) =>
        new()
        {
            FlowId = flowId,
            SequenceId = sequenceId,
            ProcessPath = processPath,
            Pid = pid,
            Destination = original.Address.ToString(),
            Port = original.Port,
            Protocol = "TCP",
            Route = FlowRoute.Vpn,
            LocalInterface = null,
            WfpRedirect = !socks && records.Length > 0,
        };

    private sealed record ConnectErrorContext(
        IPEndPoint Destination,
        string? LocalBind,
        bool ConnectTimeout,
        bool ServiceStopping);

    private void PublishTerminalError(FlowEvent flow, string phase, Exception ex, ConnectErrorContext? ctx)
    {
        bool connectTimeout = ctx?.ConnectTimeout == true;
        bool serviceStopping = ctx?.ServiceStopping == true;
        FlowErrorDetails details = FlowStatusHelper.ErrorFromException(
            phase,
            ex,
            connectTimeout,
            serviceStopping,
            FlowStatusHelper.DefaultConnectTimeoutMs) with
        {
            Destination = ctx?.Destination.ToString() ?? flow.Destination,
            VpnInterfaceIndex = flow.VpnInterfaceIndex ?? VpnInterfaceIndex,
            LocalBindEndpoint = ctx?.LocalBind ?? flow.OutboundLocalEndpoint,
        };

        string status = connectTimeout || serviceStopping ? FlowLifecycle.Cancelled : FlowLifecycle.Error;
        Publish(flow with
        {
            ErrorDetails = details,
            Status = status,
            VpnInterfaceIndex = flow.VpnInterfaceIndex ?? VpnInterfaceIndex,
            OutboundLocalEndpoint = ctx?.LocalBind ?? flow.OutboundLocalEndpoint,
        });
    }

    private void Publish(FlowEvent flow)
    {
        if (_flows.TryGetValue(flow.FlowId, out FlowEvent? existing)
            && FlowStatusHelper.IsTerminal(existing.Status)
            && !FlowStatusHelper.IsTerminal(flow.Status))
        {
            return;
        }

        FlowEvent stamped = flow with
        {
            UpdatedAt = DateTimeOffset.UtcNow,
            LocalInterface = flow.LocalInterface ?? VpnInterfaceName,
        };
        _flows[flow.FlowId] = stamped;
        while (_flows.Count > 300)
        {
            Guid? oldest = _flows.Values
                .OrderBy(f => f.UpdatedAt)
                .Select(f => f.FlowId)
                .FirstOrDefault();
            if (oldest is null || oldest == Guid.Empty)
            {
                break;
            }

            _flows.TryRemove(oldest.Value, out _);
        }

        FlowChanged?.Invoke(stamped);
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
