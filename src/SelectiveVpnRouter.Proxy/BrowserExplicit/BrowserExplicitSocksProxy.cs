using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SelectiveVpnRouter.Proxy.BrowserExplicit;

/// <summary>Production browser explicit SOCKS5 frontend (127.0.0.1 only). Uses <see cref="IVpnTcpEgress"/>; never falls back to direct connect.</summary>
public sealed class BrowserExplicitSocksProxy(IVpnTcpEgress egress) : IBrowserExplicitSocksProxyRuntime, IAsyncDisposable
{
    public const int MaxConcurrentConnections = 128;
    public const int MaxDomainLength = 253;
    public const int HandshakeTimeoutMs = 10_000;

    private readonly SemaphoreSlim _admit = new(MaxConcurrentConnections, MaxConcurrentConnections);
    private TcpListener? _listener;
    private CancellationTokenSource? _lifetimeCts;
    private Task? _accept;
    private int _activeConnections;
    private bool _disposed;

    public int Port { get; private set; }

    public int ActiveConnections => Volatile.Read(ref _activeConnections);

    public async Task StartAsync(CancellationToken lifetimeToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_listener is not null)
            await StopListeningAsync().ConfigureAwait(false);

        _lifetimeCts = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Server.ExclusiveAddressUse = true;
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _accept = AcceptLoopAsync(_lifetimeCts.Token);
        await Task.CompletedTask.ConfigureAwait(false);
    }

    public async Task WaitForRuntimeAsync(CancellationToken stoppingToken)
    {
        Task accept = _accept ?? throw new InvalidOperationException("Browser explicit proxy is not started.");
        try
        {
            await accept.WaitAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }

        if (!stoppingToken.IsCancellationRequested)
            throw new InvalidOperationException("browser-explicit-proxy accept loop stopped");
    }

    public async Task StopListeningAsync()
    {
        if (_disposed)
            return;

        try
        {
            _lifetimeCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        try
        {
            _listener?.Stop();
        }
        catch (Exception)
        {
        }

        if (_accept is not null)
        {
            try
            {
                await _accept.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }

            _accept = null;
        }

        _lifetimeCts?.Dispose();
        _lifetimeCts = null;
        _listener = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        await StopListeningAsync().ConfigureAwait(false);
        _admit.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken acceptToken)
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

            if (!await _admit.WaitAsync(0, acceptToken).ConfigureAwait(false))
            {
                incoming.Dispose();
                continue;
            }

            _ = Task.Run(() => HandleClientAsync(incoming, acceptToken));
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken serviceToken)
    {
        Interlocked.Increment(ref _activeConnections);
        try
        {
            using (client)
            {
                client.NoDelay = true;
                using NetworkStream stream = client.GetStream();
                using var handshakeTimeout = CancellationTokenSource.CreateLinkedTokenSource(serviceToken);
                handshakeTimeout.CancelAfter(HandshakeTimeoutMs);

                if (!await Socks5Handshake.ReadAndReplyNoAuthAsync(stream, handshakeTimeout.Token).ConfigureAwait(false))
                    return;

                Socks5ConnectReadResult read = await Socks5ConnectRequest.ReadAsync(stream, handshakeTimeout.Token)
                    .ConfigureAwait(false);
                if (read.Status == Socks5ConnectReadStatus.UnsupportedAddressType)
                {
                    await Socks5Reply.WriteAsync(stream, Socks5ReplyCode.AddressTypeNotSupported, serviceToken)
                        .ConfigureAwait(false);
                    return;
                }

                if (read.Status != Socks5ConnectReadStatus.Ok || read.Request is null)
                    return;

                Socks5ConnectRequest request = read.Request;
                if (request.Command != Socks5Command.Connect)
                {
                    await Socks5Reply.WriteAsync(stream, Socks5ReplyCode.CommandNotSupported, serviceToken)
                        .ConfigureAwait(false);
                    return;
                }

                if (request.Port is < 1 or > 65535)
                {
                    await Socks5Reply.WriteAsync(stream, Socks5ReplyCode.HostUnreachable, serviceToken)
                        .ConfigureAwait(false);
                    return;
                }

                VpnConnectTarget target = request.ToTarget();
                VpnConnectOutcome outcome = await egress.ConnectAsync(target, serviceToken).ConfigureAwait(false);
                if (!outcome.Success || outcome.Socket is null)
                {
                    await Socks5Reply.WriteAsync(stream, outcome.FailureCode, serviceToken).ConfigureAwait(false);
                    return;
                }

                using Socket remote = outcome.Socket;
                await Socks5Reply.WriteAsync(stream, Socks5ReplyCode.Success, serviceToken, remote).ConfigureAwait(false);
                using NetworkStream remoteStream = new(remote, ownsSocket: false);
                using var relayCts = CancellationTokenSource.CreateLinkedTokenSource(serviceToken);
                Task a = stream.CopyToAsync(remoteStream, relayCts.Token);
                Task b = remoteStream.CopyToAsync(stream, relayCts.Token);
                await Task.WhenAny(a, b).ConfigureAwait(false);
                relayCts.Cancel();
                try
                {
                    await Task.WhenAll(a, b).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
                catch (IOException)
                {
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
        }
        finally
        {
            Interlocked.Decrement(ref _activeConnections);
            _admit.Release();
        }
    }
}

internal enum Socks5Command : byte
{
    Connect = 1,
    Bind = 2,
    UdpAssociate = 3,
}

internal enum Socks5ConnectReadStatus
{
    Ok,
    Malformed,
    UnsupportedAddressType,
}

internal sealed record Socks5ConnectReadResult(Socks5ConnectReadStatus Status, Socks5ConnectRequest? Request);

internal sealed record Socks5ConnectRequest(
    Socks5Command Command,
    VpnConnectTargetKind AddressKind,
    string? Host,
    byte[]? AddressBytes,
    int Port)
{
    public VpnConnectTarget ToTarget() => new(AddressKind, Host, AddressBytes, Port);

    public static async Task<Socks5ConnectReadResult> ReadAsync(NetworkStream stream, CancellationToken ct)
    {
        var header = new byte[4];
        if (!await ReadExactAsync(stream, header, ct).ConfigureAwait(false))
            return new Socks5ConnectReadResult(Socks5ConnectReadStatus.Malformed, null);
        if (header[0] != 5)
            return new Socks5ConnectReadResult(Socks5ConnectReadStatus.Malformed, null);
        var command = (Socks5Command)header[1];
        if (header[2] != 0)
            return new Socks5ConnectReadResult(Socks5ConnectReadStatus.Malformed, null);
        return header[3] switch
        {
            1 => await ReadIpv4Async(stream, command, ct).ConfigureAwait(false),
            3 => await ReadDomainAsync(stream, command, ct).ConfigureAwait(false),
            4 => await ReadIpv6Async(stream, command, ct).ConfigureAwait(false),
            _ => new Socks5ConnectReadResult(Socks5ConnectReadStatus.UnsupportedAddressType, null),
        };
    }

    private static async Task<Socks5ConnectReadResult> ReadIpv4Async(
        NetworkStream stream,
        Socks5Command command,
        CancellationToken ct)
    {
        var buf = new byte[6];
        if (!await ReadExactAsync(stream, buf, ct).ConfigureAwait(false))
            return new Socks5ConnectReadResult(Socks5ConnectReadStatus.Malformed, null);
        int port = (buf[4] << 8) | buf[5];
        return new Socks5ConnectReadResult(
            Socks5ConnectReadStatus.Ok,
            new Socks5ConnectRequest(command, VpnConnectTargetKind.Ipv4, null, buf.AsSpan(0, 4).ToArray(), port));
    }

    private static async Task<Socks5ConnectReadResult> ReadIpv6Async(
        NetworkStream stream,
        Socks5Command command,
        CancellationToken ct)
    {
        var buf = new byte[18];
        if (!await ReadExactAsync(stream, buf, ct).ConfigureAwait(false))
            return new Socks5ConnectReadResult(Socks5ConnectReadStatus.Malformed, null);
        int port = (buf[16] << 8) | buf[17];
        return new Socks5ConnectReadResult(
            Socks5ConnectReadStatus.Ok,
            new Socks5ConnectRequest(command, VpnConnectTargetKind.Ipv6, null, buf.AsSpan(0, 16).ToArray(), port));
    }

    private static async Task<Socks5ConnectReadResult> ReadDomainAsync(
        NetworkStream stream,
        Socks5Command command,
        CancellationToken ct)
    {
        var lenBuf = new byte[1];
        if (!await ReadExactAsync(stream, lenBuf, ct).ConfigureAwait(false))
            return new Socks5ConnectReadResult(Socks5ConnectReadStatus.Malformed, null);
        int len = lenBuf[0];
        if (len is 0 or > BrowserExplicitSocksProxy.MaxDomainLength)
            return new Socks5ConnectReadResult(Socks5ConnectReadStatus.Malformed, null);
        var buf = new byte[len + 2];
        if (!await ReadExactAsync(stream, buf, ct).ConfigureAwait(false))
            return new Socks5ConnectReadResult(Socks5ConnectReadStatus.Malformed, null);
        string host = Encoding.ASCII.GetString(buf, 0, len);
        int port = (buf[len] << 8) | buf[len + 1];
        return new Socks5ConnectReadResult(
            Socks5ConnectReadStatus.Ok,
            new Socks5ConnectRequest(command, VpnConnectTargetKind.Domain, host, null, port));
    }

    private static async Task<bool> ReadExactAsync(NetworkStream stream, byte[] buffer, CancellationToken ct)
    {
        int got = 0;
        while (got < buffer.Length)
        {
            int n = await stream.ReadAsync(buffer.AsMemory(got, buffer.Length - got), ct).ConfigureAwait(false);
            if (n == 0)
                return false;
            got += n;
        }

        return true;
    }
}

internal static class Socks5Handshake
{
    public static async Task<bool> ReadAndReplyNoAuthAsync(NetworkStream stream, CancellationToken ct)
    {
        var head = new byte[2];
        if (!await ReadExactAsync(stream, head, ct).ConfigureAwait(false))
            return false;
        if (head[0] != 5)
            return false;
        int nMethods = head[1];
        if (nMethods > 0)
        {
            var methods = new byte[nMethods];
            if (!await ReadExactAsync(stream, methods, ct).ConfigureAwait(false))
                return false;
            if (!methods.Contains((byte)0))
            {
                await stream.WriteAsync(new byte[] { 5, 0xFF }, ct).ConfigureAwait(false);
                return false;
            }
        }

        await stream.WriteAsync(new byte[] { 5, 0 }, ct).ConfigureAwait(false);
        return true;
    }

    private static async Task<bool> ReadExactAsync(NetworkStream stream, byte[] buffer, CancellationToken ct)
    {
        int got = 0;
        while (got < buffer.Length)
        {
            int n = await stream.ReadAsync(buffer.AsMemory(got, buffer.Length - got), ct).ConfigureAwait(false);
            if (n == 0)
                return false;
            got += n;
        }

        return true;
    }
}

internal static class Socks5Reply
{
    public static ValueTask WriteAsync(
        NetworkStream stream,
        Socks5ReplyCode code,
        CancellationToken ct,
        Socket? bound = null)
    {
        if (code == Socks5ReplyCode.Success && bound?.LocalEndPoint is IPEndPoint ep)
            return stream.WriteAsync(BuildSuccess(ep), ct);

        return stream.WriteAsync(new byte[] { 5, (byte)code, 0, 1, 0, 0, 0, 0, 0, 0 }, ct);
    }

    private static byte[] BuildSuccess(IPEndPoint ep)
    {
        if (ep.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = new byte[4 + 16 + 2];
            bytes[0] = 5;
            bytes[1] = 0;
            bytes[2] = 0;
            bytes[3] = 4;
            ep.Address.GetAddressBytes().CopyTo(bytes.AsSpan(4, 16));
            bytes[20] = (byte)(ep.Port >> 8);
            bytes[21] = (byte)(ep.Port & 0xFF);
            return bytes;
        }

        var v4 = new byte[10];
        v4[0] = 5;
        v4[1] = 0;
        v4[2] = 0;
        v4[3] = 1;
        if (ep.Address.TryWriteBytes(v4.AsSpan(4, 4), out _))
        {
            v4[8] = (byte)(ep.Port >> 8);
            v4[9] = (byte)(ep.Port & 0xFF);
        }

        return v4;
    }
}
