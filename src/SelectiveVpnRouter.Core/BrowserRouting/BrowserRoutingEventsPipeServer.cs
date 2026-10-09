using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Unicode;

namespace SelectiveVpnRouter.Core.BrowserRouting;

/// <summary>
/// Named pipe server for browser routing push events. After a valid <c>subscribeEvents</c> request
/// the connection stays open and the server pushes lightweight event frames until disconnect.
/// </summary>
public sealed class BrowserRoutingEventsPipeServer(
    string pipeName,
    Func<BrowserRoutingSnapshot?> currentSnapshot,
    BrowserRoutingChangeNotifier changeNotifier,
    Action<string> log)
{
    public const int MaxConcurrentSubscribers = 4;
    public static readonly TimeSpan ClientTimeout = TimeSpan.FromMinutes(30);

    private static readonly JsonDocumentOptions RequestOptions = new() { MaxDepth = 4 };
    private static readonly HashSet<string> SubscribeFields =
        new(["version", "id", "method"], StringComparer.Ordinal);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var creator = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("No user SID.");
        var security = BrowserRoutingPipeServer.CreatePipeSecurity(creator);
        using var slots = new SemaphoreSlim(MaxConcurrentSubscribers, MaxConcurrentSubscribers);
        var first = true;
        while (!cancellationToken.IsCancellationRequested)
        {
            await slots.WaitAsync(cancellationToken).ConfigureAwait(false);
            NamedPipeServerStream pipe;
            try
            {
                pipe = NamedPipeServerStreamAcl.Create(
                    pipeName,
                    PipeDirection.InOut,
                    MaxConcurrentSubscribers,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | (first ? PipeOptions.FirstPipeInstance : PipeOptions.None),
                    inBufferSize: 0,
                    outBufferSize: 0,
                    security);
            }
            catch
            {
                slots.Release();
                throw;
            }
            first = false;

            try
            {
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
                slots.Release();
                throw;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await ServeSubscriberAsync(pipe, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    await pipe.DisposeAsync().ConfigureAwait(false);
                    slots.Release();
                }
            }, CancellationToken.None);
        }
    }

    private async Task ServeSubscriberAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ClientTimeout);
        try
        {
            var request = await ReadFrameAsync(pipe, timeout.Token).ConfigureAwait(false);
            if (request is null)
                return;
            var subscribe = ParseSubscribe(request);
            if (subscribe.ErrorCode is { } code)
            {
                await WriteFrameAsync(pipe, ErrorFrame(subscribe.RequestId, code), timeout.Token).ConfigureAwait(false);
                return;
            }

            await WriteFrameAsync(pipe, AckFrame(subscribe.RequestId!), timeout.Token).ConfigureAwait(false);
            var snapshot = currentSnapshot();
            if (snapshot is not null)
            {
                await WriteFrameAsync(pipe,
                    EventFrame(BrowserRoutingEventsIpcProtocol.EventTypes.ServiceAvailable,
                        snapshot.StateGeneration, snapshot.Revision),
                    timeout.Token).ConfigureAwait(false);
            }

            var writeLock = new SemaphoreSlim(1, 1);
            using var subscription = changeNotifier.Subscribe(evt =>
            {
                try
                {
                    writeLock.Wait(timeout.Token);
                    WriteFrameAsync(pipe, EventFrame(evt.Type, evt.StateGeneration, evt.Revision), timeout.Token)
                        .GetAwaiter().GetResult();
                }
                catch
                {
                    /* disconnect */
                }
                finally
                {
                    if (writeLock.CurrentCount == 0)
                        writeLock.Release();
                }
            });

            while (pipe.IsConnected && !timeout.Token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), timeout.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            log("browser-routing-events client timed out");
        }
        catch (IOException)
        {
            log("browser-routing-events client disconnected");
        }
    }

    private static SubscribeParse ParseSubscribe(byte[] request)
    {
        if (request.Length is 0 or > BrowserRoutingEventsIpcProtocol.MaxRequestBytes || !Utf8.IsValid(request))
            return new(null, BrowserRoutingIpcProtocol.Errors.InvalidRequest);
        try
        {
            using var document = JsonDocument.Parse(request, RequestOptions);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !UniqueFields(root, SubscribeFields, exact: true))
                return new(null, BrowserRoutingIpcProtocol.Errors.InvalidRequest);
            var id = ReadId(root);
            if (id is null)
                return new(null, BrowserRoutingIpcProtocol.Errors.InvalidRequest);
            if (!root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number ||
                !version.TryGetInt32(out var v) || v != BrowserRoutingEventsIpcProtocol.Version)
                return new(id, BrowserRoutingIpcProtocol.Errors.UnsupportedVersion);
            if (!root.TryGetProperty("method", out var method) || method.ValueKind != JsonValueKind.String ||
                method.GetString() != BrowserRoutingEventsIpcProtocol.Methods.SubscribeEvents)
                return new(id, BrowserRoutingIpcProtocol.Errors.UnknownMethod);
            return new(id, null);
        }
        catch (JsonException)
        {
            return new(null, BrowserRoutingIpcProtocol.Errors.InvalidRequest);
        }
    }

    private readonly record struct SubscribeParse(string? RequestId, string? ErrorCode);

    private static byte[] AckFrame(string id) => WriteEnvelope(id, ok: true, writer =>
    {
        writer.WriteStartObject("result");
        writer.WriteEndObject();
    });

    private static byte[] ErrorFrame(string? id, string code) => WriteEnvelope(id, ok: false, writer =>
    {
        writer.WriteStartObject("error");
        writer.WriteString("code", code);
        writer.WriteEndObject();
    });

    internal static byte[] EventFrame(string type, string stateGeneration, long revision)
    {
        var buffer = new ArrayBufferWriter<byte>(128);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("type", type);
            writer.WriteString("stateGeneration", stateGeneration);
            writer.WriteNumber("revision", revision);
            writer.WriteEndObject();
        }
        return buffer.WrittenSpan.ToArray();
    }

    private static byte[] WriteEnvelope(string? id, bool ok, Action<Utf8JsonWriter> body)
    {
        var buffer = new ArrayBufferWriter<byte>(256);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", BrowserRoutingEventsIpcProtocol.Version);
            if (id is null)
                writer.WriteNull("id");
            else
                writer.WriteString("id", id);
            writer.WriteBoolean("ok", ok);
            body(writer);
            writer.WriteEndObject();
        }
        return buffer.WrittenSpan.ToArray();
    }

    private static async Task WriteFrameAsync(Stream pipe, byte[] body, CancellationToken cancellationToken)
    {
        if (body.Length > BrowserRoutingEventsIpcProtocol.MaxEventBytes)
            throw new InvalidOperationException("Event frame exceeds budget.");
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, body.Length);
        await pipe.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await pipe.WriteAsync(body, cancellationToken).ConfigureAwait(false);
        await pipe.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<byte[]?> ReadFrameAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[4];
        if (!await ReadExactlyAsync(stream, header, cancellationToken).ConfigureAwait(false))
            return null;
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is <= 0 or > BrowserRoutingEventsIpcProtocol.MaxRequestBytes)
            return [];
        var body = new byte[length];
        if (!await ReadExactlyAsync(stream, body, cancellationToken).ConfigureAwait(false))
            return null;
        return body;
    }

    private static async Task<bool> ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                return false;
            offset += read;
        }
        return true;
    }

    private static bool UniqueFields(JsonElement element, HashSet<string> allowed, bool exact)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!allowed.Contains(property.Name) || !seen.Add(property.Name))
                return false;
        }
        return !exact || seen.Count == allowed.Count;
    }

    private static string? ReadId(JsonElement root)
    {
        if (!root.TryGetProperty("id", out var element) || element.ValueKind != JsonValueKind.String)
            return null;
        var value = element.GetString();
        if (string.IsNullOrEmpty(value) || value.Length > BrowserRoutingIpcProtocol.MaxIdLength)
            return null;
        foreach (var c in value)
        {
            if (!(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_' or '.' or ':'))
                return null;
        }
        return value;
    }
}
