using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;

namespace SelectiveVpnRouter.Core.Portable;

public sealed record PortableIpcSmokeResult
{
    public bool Ready { get; init; }
    public int Attempts { get; init; }
    public string? LastError { get; init; }
    public int ElapsedMs { get; init; }
    public bool NonRetryable { get; init; }
    public bool ServiceAlive { get; init; }
}

public static class PortableIpcProbe
{
    internal static Func<int, GetStatusAttemptResult>? GetStatusAttemptOverrideForTests;

    public const int DefaultReadinessTimeoutMs = 15_000;
    public const int DefaultPollIntervalMs = 250;
    public const int PerAttemptConnectTimeoutMs = 3_000;

    public static bool TryPingService(int timeoutMs = 5000)
        => WaitForGetStatusReady(totalTimeoutMs: timeoutMs, pollIntervalMs: DefaultPollIntervalMs).Ready;

    public static PortableIpcSmokeResult WaitForGetStatusReady(
        int totalTimeoutMs = DefaultReadinessTimeoutMs,
        int pollIntervalMs = DefaultPollIntervalMs,
        int perAttemptConnectTimeoutMs = PerAttemptConnectTimeoutMs)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new PortableIpcSmokeResult
            {
                Ready = false,
                LastError = "IPC smoke is supported on Windows only.",
                NonRetryable = true,
            };
        }

        Stopwatch sw = Stopwatch.StartNew();
        int attempts = 0;
        string lastError = "";
        bool nonRetryable = false;

        while (sw.ElapsedMilliseconds < totalTimeoutMs)
        {
            attempts++;
            GetStatusAttemptResult attempt = GetStatusAttemptOverrideForTests?.Invoke(perAttemptConnectTimeoutMs)
                ?? TryGetStatusOnce(perAttemptConnectTimeoutMs);
            if (attempt.Ready)
            {
                return new PortableIpcSmokeResult
                {
                    Ready = true,
                    Attempts = attempts,
                    ElapsedMs = (int)sw.ElapsedMilliseconds,
                    ServiceAlive = attempt.ServiceAlive,
                };
            }

            lastError = attempt.Error ?? "GetStatus not ready";
            nonRetryable = attempt.NonRetryable;
            if (nonRetryable)
            {
                break;
            }

            if (sw.ElapsedMilliseconds + pollIntervalMs >= totalTimeoutMs)
            {
                break;
            }

            Thread.Sleep(pollIntervalMs);
        }

        return new PortableIpcSmokeResult
        {
            Ready = false,
            Attempts = attempts,
            LastError = lastError,
            ElapsedMs = (int)sw.ElapsedMilliseconds,
            NonRetryable = nonRetryable,
        };
    }

    internal sealed record GetStatusAttemptResult(bool Ready, bool ServiceAlive, string? Error, bool NonRetryable);

    internal static GetStatusAttemptResult TryGetStatusOnce(int connectTimeoutMs)
    {
        try
        {
            using CancellationTokenSource cts = new(connectTimeoutMs + 5_000);
            using NamedPipeClientStream pipe = new(".", AppPaths.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            pipe.ConnectAsync(connectTimeoutMs, cts.Token).GetAwaiter().GetResult();
            var req = new IpcRequest
            {
                Id = Guid.NewGuid().ToString("N"),
                Method = IpcMethods.GetStatus,
            };
            byte[] body = JsonSerializer.SerializeToUtf8Bytes(req, ConfigSerializer.JsonOptions);
            byte[] header = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(header, body.Length);
            pipe.WriteAsync(header, cts.Token).GetAwaiter().GetResult();
            pipe.WriteAsync(body, cts.Token).GetAwaiter().GetResult();
            byte[] lenBuf = new byte[4];
            ReadExact(pipe, lenBuf, cts.Token);
            int len = BinaryPrimitives.ReadInt32LittleEndian(lenBuf);
            if (len <= 0 || len > 8_000_000)
            {
                return new GetStatusAttemptResult(false, false, "Invalid IPC response length.", true);
            }

            byte[] resp = new byte[len];
            ReadExact(pipe, resp, cts.Token);
            IpcResponse? r = JsonSerializer.Deserialize<IpcResponse>(resp, ConfigSerializer.JsonOptions);
            if (r is not { Ok: true })
            {
                string err = r?.Error ?? "GetStatus Ok=false";
                return new GetStatusAttemptResult(false, false, err, !IpcReadinessHelper.IsTransientIpcError(err));
            }

            ServiceSnapshot? snap = null;
            if (!string.IsNullOrWhiteSpace(r.PayloadJson))
            {
                snap = JsonSerializer.Deserialize<ServiceSnapshot>(r.PayloadJson, ConfigSerializer.JsonOptions);
            }

            return new GetStatusAttemptResult(true, snap?.ServiceAlive ?? true, null, false);
        }
        catch (Exception ex)
        {
            string err = ex.Message;
            return new GetStatusAttemptResult(false, false, err, !IpcReadinessHelper.IsTransientIpcError(err));
        }
    }

    public static bool TryEmergencyRestore(int timeoutMs = 30_000)
    {
        try
        {
            using CancellationTokenSource cts = new(timeoutMs);
            using NamedPipeClientStream pipe = new(".", AppPaths.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            pipe.ConnectAsync(IpcTimeouts.PipeConnectMs, cts.Token).GetAwaiter().GetResult();
            var req = new IpcRequest
            {
                Id = Guid.NewGuid().ToString("N"),
                Method = IpcMethods.EmergencyRestore,
            };
            byte[] body = JsonSerializer.SerializeToUtf8Bytes(req, ConfigSerializer.JsonOptions);
            byte[] header = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(header, body.Length);
            pipe.WriteAsync(header, cts.Token).GetAwaiter().GetResult();
            pipe.WriteAsync(body, cts.Token).GetAwaiter().GetResult();
            byte[] lenBuf = new byte[4];
            ReadExact(pipe, lenBuf, cts.Token);
            int len = BinaryPrimitives.ReadInt32LittleEndian(lenBuf);
            byte[] resp = new byte[len];
            ReadExact(pipe, resp, cts.Token);
            IpcResponse? r = JsonSerializer.Deserialize<IpcResponse>(resp, ConfigSerializer.JsonOptions);
            return r is { Ok: true };
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void ReadExact(Stream stream, byte[] buffer, CancellationToken ct)
    {
        int read = 0;
        while (read < buffer.Length)
        {
            int n = stream.ReadAsync(buffer.AsMemory(read), ct).GetAwaiter().GetResult();
            if (n == 0)
            {
                throw new EndOfStreamException();
            }

            read += n;
        }
    }
}
