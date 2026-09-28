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

        try
        {
            using var pipe = new NamedPipeClientStream(".", AppPaths.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(IpcTimeouts.PipeConnectMs, timeoutCts.Token).ConfigureAwait(false);
            using var writer = new BinaryWriter(pipe, Encoding.UTF8, leaveOpen: true);
            using var reader = new BinaryReader(pipe, Encoding.UTF8, leaveOpen: true);
            writer.Write(body.Length);
            writer.Write(body);
            writer.Flush();
            int len = reader.ReadInt32();
            byte[] respBody = reader.ReadBytes(len);
            return JsonSerializer.Deserialize<IpcResponse>(respBody, ConfigSerializer.JsonOptions)
                ?? new IpcResponse { Id = req.Id, Ok = false, Error = "Empty IPC response." };
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            throw new IpcTimeoutException(method, operationMs);
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
}
