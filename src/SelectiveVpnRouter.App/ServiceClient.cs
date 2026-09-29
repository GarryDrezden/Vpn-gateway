using System.Buffers.Binary;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using SelectiveVpnRouter.Core;

namespace SelectiveVpnRouter.App;

public sealed class ServiceClient
{
    public async Task<IpcResponse> SendAsync(string method, object? payload, CancellationToken ct)
    {
        string? payloadJson = payload is null ? null : JsonSerializer.Serialize(payload, ConfigSerializer.JsonOptions);
        var req = new IpcRequest { Id = Guid.NewGuid().ToString("N"), Method = method, PayloadJson = payloadJson };
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(req, ConfigSerializer.JsonOptions);
        int operationMs = IpcTimeouts.OperationTimeoutMs(method);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(operationMs);

        NamedPipeClientStream? pipe = null;
        try
        {
            pipe = new NamedPipeClientStream(".", AppPaths.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(IpcTimeouts.PipeConnectMs, timeoutCts.Token).ConfigureAwait(false);
            await WriteFrameAsync(pipe, body, timeoutCts.Token).ConfigureAwait(false);
            byte[] respBody = await ReadFrameAsync(pipe, timeoutCts.Token).ConfigureAwait(false);
            return JsonSerializer.Deserialize<IpcResponse>(respBody, ConfigSerializer.JsonOptions)
                ?? new IpcResponse { Id = req.Id, Ok = false, Error = "Empty IPC response." };
        }
        catch (TimeoutException) when (pipe is null || !pipe.IsConnected)
        {
            throw new IpcTimeoutException(method, IpcTimeouts.PipeConnectMs);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            throw new IpcTimeoutException(method, operationMs);
        }
        finally
        {
            pipe?.Dispose();
        }
    }

    public async Task<T?> SendOkAsync<T>(string method, object? payload, CancellationToken ct)
    {
        IpcResponse r = await SendAsync(method, payload, ct).ConfigureAwait(false);
        if (!r.Ok)
        {
            throw new InvalidOperationException(r.Error ?? "Service error.");
        }

        if (string.IsNullOrWhiteSpace(r.PayloadJson))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(r.PayloadJson, ConfigSerializer.JsonOptions);
    }

    public async Task<bool> TryPingAsync(CancellationToken ct)
    {
        try
        {
            _ = await SendAsync(IpcMethods.GetStatus, null, ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static async Task WriteFrameAsync(Stream pipe, byte[] body, CancellationToken ct)
    {
        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, body.Length);
        await pipe.WriteAsync(header, ct).ConfigureAwait(false);
        await pipe.WriteAsync(body, ct).ConfigureAwait(false);
        await pipe.FlushAsync(ct).ConfigureAwait(false);
    }

    private static async Task<byte[]> ReadFrameAsync(Stream pipe, CancellationToken ct)
    {
        byte[] header = new byte[4];
        await ReadExactlyAsync(pipe, header, ct).ConfigureAwait(false);
        int len = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (len <= 0 || len > 4_000_000)
        {
            throw new InvalidOperationException("Invalid IPC response length.");
        }

        byte[] body = new byte[len];
        await ReadExactlyAsync(pipe, body, ct).ConfigureAwait(false);
        return body;
    }

    private static async Task ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset), ct).ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException("IPC stream ended before frame completed.");
            }

            offset += read;
        }
    }
}
