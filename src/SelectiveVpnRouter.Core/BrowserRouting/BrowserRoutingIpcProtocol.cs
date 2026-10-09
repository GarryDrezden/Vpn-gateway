using System.Buffers;
using System.Globalization;
using System.Text.Json;
using System.Text.Unicode;

namespace SelectiveVpnRouter.Core.BrowserRouting;

/// <summary>
/// Browser routing Service IPC v1 (ext-vpn-route docs/service-ipc-browser-routing-v1.md).
/// A dedicated read-only endpoint: it serves the authoritative state to the browser native host
/// and nothing else. Framing is the same as the main Service pipe: Int32 little-endian length + UTF-8 JSON.
/// </summary>
public static class BrowserRoutingIpcProtocol
{
    public const string PipeName = "SelectiveVpnRouter.BrowserRouting";
    public const int Version = 1;

    public const int MaxRequestBytes = 4 * 1024;
    public const int MaxResponseBytes = 512 * 1024;
    public const int EnvelopeReserveBytes = 4 * 1024;

    /// <summary>Budget of the serialized "rules" array of one page.</summary>
    public const int PageRulesBudgetBytes = MaxResponseBytes - EnvelopeReserveBytes;

    /// <summary>Upper bound of one serialized rule allowed by the contract limits (worst case is about 7.3 KB).</summary>
    public const int MaxRuleBytes = 8 * 1024;

    /// <summary>
    /// Pages one full snapshot can take: every non-final page carries at least
    /// floor((budget - 2) / (MaxRuleBytes + 1)) = 63 rules, so 10000 rules need at most 159 pages.
    /// </summary>
    public const int MaxPagesPerSnapshot = 160;

    public const int MaxIdLength = 128;

    public static class Methods
    {
        public const string GetManifest = "getManifest";
        public const string GetPage = "getPage";
        public const string UpsertRule = "upsertRule";
        public const string DeleteRule = "deleteRule";
        public const string ResetRules = "resetRules";
    }

    public static class Errors
    {
        public const string InvalidRequest = "invalid_request";
        public const string UnsupportedVersion = "unsupported_version";
        public const string UnknownMethod = "unknown_method";
        public const string BrowserStateUnavailable = "browser_state_unavailable";
        public const string SnapshotChanged = "snapshot_changed";
        public const string InvalidCursor = "invalid_cursor";
        public const string InternalError = "internal_error";
        public const string RevisionConflict = "revision_conflict";
        public const string ValidationFailed = "validation_failed";
        public const string NotFound = "not_found";
        public const string PersistenceFailed = "persistence_failed";
    }
}

/// <summary>Handles one request frame and produces one response frame. Pure: no I/O, no logging of state data.</summary>
public sealed partial class BrowserRoutingIpcDispatcher(
    Func<BrowserRoutingSnapshot?> currentSnapshot,
    BrowserRoutingStateStore? mutationStore,
    IBrowserProxyReadiness proxyReadiness,
    IVpnTunnelEgressReadiness vpnTunnelEgressReadiness,
    BrowserClientTracker browserClientTracker,
    IBrowserIntegrationServiceVersion serviceVersion,
    IVpnInterfaceNameLookup interfaceNameLookup)
{
    private static readonly JsonDocumentOptions RequestOptions = new() { MaxDepth = 5 };
    private static readonly HashSet<string> ManifestFields = new(["version", "id", "method"], StringComparer.Ordinal);
    private static readonly HashSet<string> ManifestWithParamsFields = new(["version", "id", "method", "params"], StringComparer.Ordinal);
    private static readonly HashSet<string> ManifestParamsFields = new(["client"], StringComparer.Ordinal);
    private static readonly HashSet<string> ManifestClientFields = new(["extensionVersion", "nativeHostVersion"], StringComparer.Ordinal);
    private static readonly HashSet<string> PageFields = new(["version", "id", "method", "params"], StringComparer.Ordinal);
    private static readonly HashSet<string> PageParams = new(["stateGeneration", "revision", "startIndex"], StringComparer.Ordinal);

    public readonly record struct Outcome(byte[] Response, string Method, string Result);

    public Outcome Dispatch(ReadOnlySpan<byte> request)
    {
        if (request.Length is 0 or > BrowserRoutingIpcProtocol.MaxRequestBytes || !Utf8.IsValid(request))
            return Fail(null, "-", BrowserRoutingIpcProtocol.Errors.InvalidRequest);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(request.ToArray(), RequestOptions);
        }
        catch (JsonException)
        {
            return Fail(null, "-", BrowserRoutingIpcProtocol.Errors.InvalidRequest);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !UniqueFields(root, PageFields))
                return Fail(null, "-", BrowserRoutingIpcProtocol.Errors.InvalidRequest);

            var id = ReadId(root);
            if (id is null)
                return Fail(null, "-", BrowserRoutingIpcProtocol.Errors.InvalidRequest);
            if (!root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number)
                return Fail(id, "-", BrowserRoutingIpcProtocol.Errors.InvalidRequest);
            if (!version.TryGetInt32(out var v) || v != BrowserRoutingIpcProtocol.Version)
                return Fail(id, "-", BrowserRoutingIpcProtocol.Errors.UnsupportedVersion);
            if (!root.TryGetProperty("method", out var method) || method.ValueKind != JsonValueKind.String)
                return Fail(id, "-", BrowserRoutingIpcProtocol.Errors.InvalidRequest);

            try
            {
                // Explicit allowlist: the browser endpoint has no other method and no generic relay.
                return method.GetString() switch
                {
                    BrowserRoutingIpcProtocol.Methods.GetManifest => HandleGetManifest(id, root),
                    BrowserRoutingIpcProtocol.Methods.GetPage => Page(id, root),
                    BrowserRoutingIpcProtocol.Methods.UpsertRule => UpsertRule(id, root),
                    BrowserRoutingIpcProtocol.Methods.DeleteRule => DeleteRule(id, root),
                    BrowserRoutingIpcProtocol.Methods.ResetRules => ResetRules(id, root),
                    _ => Fail(id, "unknown", BrowserRoutingIpcProtocol.Errors.UnknownMethod)
                };
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return Fail(id, "-", BrowserRoutingIpcProtocol.Errors.InternalError, ex.GetType().Name);
            }
        }
    }

    private Outcome HandleGetManifest(string id, JsonElement root)
    {
        const string method = BrowserRoutingIpcProtocol.Methods.GetManifest;
        if (root.TryGetProperty("params", out JsonElement paramsElement))
        {
            if (!UniqueFields(root, ManifestWithParamsFields, exact: true))
                return Fail(id, method, BrowserRoutingIpcProtocol.Errors.InvalidRequest);
            if (paramsElement.ValueKind != JsonValueKind.Object || !UniqueFields(paramsElement, ManifestParamsFields, exact: true))
                return Fail(id, method, BrowserRoutingIpcProtocol.Errors.InvalidRequest);
            if (!paramsElement.TryGetProperty("client", out JsonElement client) ||
                client.ValueKind != JsonValueKind.Object ||
                !UniqueFields(client, ManifestClientFields, exact: true))
                return Fail(id, method, BrowserRoutingIpcProtocol.Errors.InvalidRequest);
            if (!TryReadClientVersion(client.GetProperty("extensionVersion"), out string? extensionVersion) ||
                !TryReadClientVersion(client.GetProperty("nativeHostVersion"), out string? nativeHostVersion))
                return Fail(id, method, BrowserRoutingIpcProtocol.Errors.InvalidRequest);

            browserClientTracker.Touch(extensionVersion!, nativeHostVersion!);
            return Manifest(id);
        }

        return UniqueFields(root, ManifestFields, exact: true)
            ? Manifest(id)
            : Fail(id, method, BrowserRoutingIpcProtocol.Errors.InvalidRequest);
    }

    private Outcome Manifest(string id)
    {
        const string method = BrowserRoutingIpcProtocol.Methods.GetManifest;
        var snapshot = currentSnapshot();
        if (snapshot is null)
            return Fail(id, method, BrowserRoutingIpcProtocol.Errors.BrowserStateUnavailable);
        BrowserProxyStatus proxy = proxyReadiness.GetStatus();
        BrowserClientSnapshot client = browserClientTracker.GetSnapshot();

        var response = Success(id, writer =>
        {
            writer.WriteNumber("schemaVersion", snapshot.State.SchemaVersion);
            writer.WriteString("stateGeneration", snapshot.StateGeneration);
            writer.WriteNumber("revision", snapshot.Revision);
            writer.WriteString("defaultRoute", snapshot.State.DefaultRoute);
            writer.WriteNumber("ruleCount", snapshot.RuleCount);
            writer.WriteNumber("pageBudgetBytes", BrowserRoutingIpcProtocol.PageRulesBudgetBytes);
            writer.WriteNumber("integrationApiVersion", BrowserIntegrationContract.IntegrationApiVersion);
            writer.WriteString("serviceVersion", serviceVersion.ServiceVersion);
            writer.WriteStartArray("capabilities");
            foreach (string capability in BrowserIntegrationContract.Capabilities)
                writer.WriteStringValue(capability);
            writer.WriteEndArray();
            WriteBrowserProxy(writer, proxy);
            WriteVpnEgress(writer);
            WriteBrowserClient(writer, client);
        });
        return new Outcome(response, method, "ok");
    }

    private void WriteBrowserProxy(Utf8JsonWriter writer, BrowserProxyStatus proxy)
    {
        writer.WriteStartObject("browserProxy");
        if (proxy.Status == BrowserProxyStatus.Ready && IsLoopback(proxy.EndpointHost) && proxy.EndpointPort is >= 1 and <= 65535)
        {
            writer.WriteString("status", BrowserProxyStatus.Ready);
            writer.WriteStartObject("endpoint");
            writer.WriteString("host", proxy.EndpointHost);
            writer.WriteNumber("port", proxy.EndpointPort.Value);
            writer.WriteEndObject();
        }
        else
        {
            writer.WriteString("status", BrowserProxyStatus.Unavailable);
            writer.WriteNull("endpoint");
        }

        writer.WriteEndObject();
    }

    private void WriteVpnEgress(Utf8JsonWriter writer)
    {
        writer.WriteStartObject("vpnEgress");
        if (vpnTunnelEgressReadiness.TryGetTunnelInterfaceIndex(out int interfaceIndex))
        {
            writer.WriteString("status", BrowserIntegrationContract.VpnEgressStatus.Ready);
            writer.WriteNumber("interfaceIndex", interfaceIndex);
            string? name = interfaceNameLookup.TryGetInterfaceName(interfaceIndex);
            if (name is null)
                writer.WriteNull("interfaceName");
            else
                writer.WriteString("interfaceName", name);
        }
        else
        {
            writer.WriteString("status", BrowserIntegrationContract.VpnEgressStatus.Unavailable);
            writer.WriteNull("interfaceIndex");
            writer.WriteNull("interfaceName");
        }

        writer.WriteEndObject();
    }

    private static void WriteBrowserClient(Utf8JsonWriter writer, BrowserClientSnapshot client)
    {
        writer.WriteStartObject("browserClient");
        writer.WriteString("status", client.Status);
        if (client.LastSeenUtc is null)
            writer.WriteNull("lastSeenUtc");
        else
            writer.WriteString("lastSeenUtc", client.LastSeenUtc.Value.ToString("O", CultureInfo.InvariantCulture));
        writer.WriteEndObject();
    }

    private static bool TryReadClientVersion(JsonElement element, out string? value)
    {
        value = null;
        if (element.ValueKind != JsonValueKind.String)
            return false;
        value = element.GetString();
        if (string.IsNullOrEmpty(value) || value.Length > BrowserIntegrationContract.ClientVersionMaxLength)
            return false;
        foreach (char c in value)
        {
            if (c < 32 || c > 126)
                return false;
        }

        return true;
    }

    private Outcome Page(string id, JsonElement root)
    {
        const string method = BrowserRoutingIpcProtocol.Methods.GetPage;
        if (!root.TryGetProperty("params", out var p) || p.ValueKind != JsonValueKind.Object || !UniqueFields(p, PageParams, exact: true))
            return Fail(id, method, BrowserRoutingIpcProtocol.Errors.InvalidRequest);

        var generation = p.GetProperty("stateGeneration");
        if (generation.ValueKind != JsonValueKind.String || !StateGenerationFormat.IsValid(generation.GetString()))
            return Fail(id, method, BrowserRoutingIpcProtocol.Errors.InvalidRequest);
        if (!BrowserRoutingValidator.TryReadSafeInteger(p.GetProperty("revision"), out var revision))
            return Fail(id, method, BrowserRoutingIpcProtocol.Errors.InvalidRequest);
        var startElement = p.GetProperty("startIndex");
        if (startElement.ValueKind != JsonValueKind.Number || !startElement.TryGetInt32(out var startIndex) ||
            startIndex is < 0 or > BrowserRoutingContract.MaxRules)
            return Fail(id, method, BrowserRoutingIpcProtocol.Errors.InvalidRequest);

        var snapshot = currentSnapshot();
        if (snapshot is null)
            return Fail(id, method, BrowserRoutingIpcProtocol.Errors.BrowserStateUnavailable);
        if (!string.Equals(snapshot.StateGeneration, generation.GetString(), StringComparison.Ordinal) || snapshot.Revision != revision)
            return Fail(id, method, BrowserRoutingIpcProtocol.Errors.SnapshotChanged);
        if (startIndex >= snapshot.RuleCount)
            return Fail(id, method, BrowserRoutingIpcProtocol.Errors.InvalidCursor);

        var end = snapshot.PageEnd(startIndex, BrowserRoutingIpcProtocol.PageRulesBudgetBytes);
        var response = Success(id, writer =>
        {
            writer.WriteString("stateGeneration", snapshot.StateGeneration);
            writer.WriteNumber("revision", snapshot.Revision);
            writer.WriteNumber("startIndex", startIndex);
            if (end < snapshot.RuleCount)
                writer.WriteNumber("nextIndex", end);
            else
                writer.WriteNull("nextIndex");
            writer.WriteStartArray("rules");
            for (var i = startIndex; i < end; i++)
                writer.WriteRawValue(snapshot.RuleJson(i), skipInputValidation: true);
            writer.WriteEndArray();
        });
        if (response.Length > BrowserRoutingIpcProtocol.MaxResponseBytes)
            return Fail(id, method, BrowserRoutingIpcProtocol.Errors.InternalError, "page_over_budget");
        return new Outcome(response, method, "ok " + startIndex.ToString(CultureInfo.InvariantCulture) + ".." +
            end.ToString(CultureInfo.InvariantCulture) + " " + response.Length.ToString(CultureInfo.InvariantCulture) + "B");
    }

    private static bool IsLoopback(string? host) =>
        host is not null && System.Net.IPAddress.TryParse(host, out var address) &&
        address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
        address.GetAddressBytes()[0] == 127 && address.ToString() == host;

    private static bool UniqueFields(JsonElement element, HashSet<string> allowed, bool exact = false)
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

    private static byte[] Success(string id, Action<Utf8JsonWriter> writeResult) => Write(writer =>
    {
        writer.WriteNumber("version", BrowserRoutingIpcProtocol.Version);
        writer.WriteString("id", id);
        writer.WriteBoolean("ok", true);
        writer.WriteStartObject("result");
        writeResult(writer);
        writer.WriteEndObject();
    });

    private static Outcome Fail(string? id, string method, string code, string? detail = null, Action<Utf8JsonWriter>? writeError = null)
    {
        var response = Write(writer =>
        {
            writer.WriteNumber("version", BrowserRoutingIpcProtocol.Version);
            if (id is null)
                writer.WriteNull("id");
            else
                writer.WriteString("id", id);
            writer.WriteBoolean("ok", false);
            writer.WriteStartObject("error");
            writer.WriteString("code", code);
            writeError?.Invoke(writer);
            writer.WriteEndObject();
        });
        return new Outcome(response, method, detail is null ? code : code + " (" + detail + ")");
    }

    private static byte[] Write(Action<Utf8JsonWriter> body)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            body(writer);
            writer.WriteEndObject();
        }
        return buffer.WrittenSpan.ToArray();
    }
}
