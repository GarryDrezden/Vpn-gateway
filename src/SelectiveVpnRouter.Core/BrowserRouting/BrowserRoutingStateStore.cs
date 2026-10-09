using System.Buffers;
using System.Text.Json;

namespace SelectiveVpnRouter.Core.BrowserRouting;

public enum BrowserRoutingStoreStatus
{
    NotLoaded,
    Available,
    Unavailable
}

/// <summary>Why the authoritative browser state cannot be served. Never contains rule data.</summary>
public static class BrowserRoutingUnavailableReason
{
    public const string NotLoaded = "not_loaded";
    public const string Corrupt = "state_corrupt";
    public const string PrimaryMissing = "state_missing_backup_present";
    public const string UnsupportedDocument = "unsupported_document_version";
    public const string PersistenceError = "persistence_error";
}

/// <summary>
/// Owns <c>browser-routing-state.json</c>: the only persisted copy of the authoritative browser routing state.
///
/// - First start (no file, no backup): creates schemaVersion 1, a new stateGeneration, revision 0,
///   defaultRoute Direct, no rules — and persists it before serving it.
/// - Restart: generation, revision and rules are read back unchanged.
/// - Corrupt / unreadable / invalid file: the store becomes Unavailable. It never replaces the file
///   with an empty Direct state; only an explicit <see cref="Reset"/> does that (new generation).
/// - Legacy document without a generation (bare BrowserRoutingStateV1): migrated once, keeping
///   revision and rules and assigning a new generation.
/// - Writes are atomic (temp file + File.Replace with backup); a failed write keeps the old state
///   in memory and on disk.
/// </summary>
public sealed class BrowserRoutingStateStore
{
    public const string DocumentType = "VpnRoute.BrowserRoutingState";
    public const int DocumentVersion = 1;

    private static readonly JsonDocumentOptions ReadOptions = new() { MaxDepth = 16 };
    private static readonly HashSet<string> EnvelopeFields =
        new(["documentType", "documentVersion", "stateGeneration"], StringComparer.Ordinal);

    private readonly object _writeLock = new();
    private readonly string _path;
    private readonly string _backupPath;
    private readonly string _tempPath;
    private readonly Func<string> _newGeneration;
    private volatile BrowserRoutingSnapshot? _current;
    private volatile string _unavailableReason = BrowserRoutingUnavailableReason.NotLoaded;

    public BrowserRoutingStateStore(string path, Func<string>? newGeneration = null)
    {
        _path = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(_path) ?? throw new ArgumentException("State path must include a directory.", nameof(path));
        _backupPath = Path.Combine(dir, Path.GetFileNameWithoutExtension(_path) + ".bak.json");
        _tempPath = _path + ".tmp";
        _newGeneration = newGeneration ?? StateGenerationFormat.New;
    }

    public static string DefaultPath => Path.Combine(AppPaths.ProgramData, "browser-routing-state.json");

    /// <summary>Test hook: runs after the temp file is written and before it replaces the state file.</summary>
    internal Action<string>? BeforeCommitForTests { get; set; }

    public string StatePath => _path;
    public BrowserRoutingStoreStatus Status => _current is not null
        ? BrowserRoutingStoreStatus.Available
        : _unavailableReason == BrowserRoutingUnavailableReason.NotLoaded ? BrowserRoutingStoreStatus.NotLoaded : BrowserRoutingStoreStatus.Unavailable;

    /// <summary>The current immutable snapshot, or null when the state is unavailable.</summary>
    public BrowserRoutingSnapshot? Current => _current;
    public string? UnavailableReason => _current is null ? _unavailableReason : null;

    public BrowserRoutingStoreStatus Load()
    {
        lock (_writeLock)
        {
            _current = null;
            try
            {
                if (!File.Exists(_path))
                {
                    if (File.Exists(_backupPath))
                        return MarkUnavailable(BrowserRoutingUnavailableReason.PrimaryMissing);
                    var initial = BrowserRoutingSnapshot.Create(new BrowserRoutingState(
                        _newGeneration(), 0, BrowserRoutingContract.RouteDirect, []));
                    return Commit(initial) ? BrowserRoutingStoreStatus.Available
                        : MarkUnavailable(BrowserRoutingUnavailableReason.PersistenceError);
                }

                var bytes = File.ReadAllBytes(_path);
                var parsed = Parse(bytes, out var legacy, out var reason);
                if (parsed is null)
                    return MarkUnavailable(reason);
                if (legacy)
                {
                    return Commit(parsed) ? BrowserRoutingStoreStatus.Available
                        : MarkUnavailable(BrowserRoutingUnavailableReason.PersistenceError);
                }
                _current = parsed;
                return BrowserRoutingStoreStatus.Available;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return MarkUnavailable(BrowserRoutingUnavailableReason.PersistenceError);
            }
        }
    }

    /// <summary>
    /// Replaces default route and rules, bumping revision by one in the same generation.
    /// Fails with <see cref="BrowserRoutingConcurrencyException"/> when <paramref name="expectedRevision"/> is stale.
    /// </summary>
    public BrowserRoutingSnapshot Update(long expectedRevision, string defaultRoute, IReadOnlyList<BrowserRoutingRule> rules)
    {
        lock (_writeLock)
        {
            var current = _current ?? throw new BrowserRoutingUnavailableException(_unavailableReason);
            if (current.Revision != expectedRevision)
                throw new BrowserRoutingConcurrencyException(current.Revision);
            if (current.Revision >= BrowserRoutingContract.MaxRevision)
                throw new BrowserRoutingValidationException([new(BrowserRoutingValidator.Codes.InvalidRevision, "/revision")]);
            var next = BrowserRoutingSnapshot.Create(new BrowserRoutingState(
                current.StateGeneration, current.Revision + 1, defaultRoute, rules));
            if (!Commit(next))
                throw new BrowserRoutingPersistenceException();
            return next;
        }
    }

    /// <summary>Upserts one rule by id in the current generation; bumps revision on success.</summary>
    public BrowserRoutingSnapshot UpsertRule(long expectedRevision, BrowserRoutingRule rule)
    {
        lock (_writeLock)
        {
            var current = _current ?? throw new BrowserRoutingUnavailableException(_unavailableReason);
            if (current.Revision != expectedRevision)
                throw new BrowserRoutingConcurrencyException(current.Revision);
            if (current.Revision >= BrowserRoutingContract.MaxRevision)
                throw new BrowserRoutingValidationException([new(BrowserRoutingValidator.Codes.InvalidRevision, "/revision")]);
            var rules = current.State.Rules.ToList();
            var index = rules.FindIndex(r => string.Equals(r.Id, rule.Id, StringComparison.Ordinal));
            if (index >= 0)
                rules[index] = rule;
            else
                rules.Add(rule);
            var next = BrowserRoutingSnapshot.Create(new BrowserRoutingState(
                current.StateGeneration, current.Revision + 1, current.State.DefaultRoute, rules));
            if (!Commit(next))
                throw new BrowserRoutingPersistenceException();
            return next;
        }
    }

    /// <summary>Removes a rule by id; <see cref="BrowserRoutingRuleNotFoundException"/> when missing.</summary>
    public BrowserRoutingSnapshot DeleteRule(long expectedRevision, string id)
    {
        lock (_writeLock)
        {
            var current = _current ?? throw new BrowserRoutingUnavailableException(_unavailableReason);
            if (current.Revision != expectedRevision)
                throw new BrowserRoutingConcurrencyException(current.Revision);
            var rules = current.State.Rules.ToList();
            var index = rules.FindIndex(r => string.Equals(r.Id, id, StringComparison.Ordinal));
            if (index < 0)
                throw new BrowserRoutingRuleNotFoundException(id);
            rules.RemoveAt(index);
            var next = BrowserRoutingSnapshot.Create(new BrowserRoutingState(
                current.StateGeneration, current.Revision + 1, current.State.DefaultRoute, rules));
            if (!Commit(next))
                throw new BrowserRoutingPersistenceException();
            return next;
        }
    }

    /// <summary>Clears all rules and sets defaultRoute Direct in the same generation (revision bump unless already empty).</summary>
    public BrowserRoutingSnapshot ResetRules(long expectedRevision)
    {
        lock (_writeLock)
        {
            var current = _current ?? throw new BrowserRoutingUnavailableException(_unavailableReason);
            if (current.Revision != expectedRevision)
                throw new BrowserRoutingConcurrencyException(current.Revision);
            if (current.State.DefaultRoute == BrowserRoutingContract.RouteDirect && current.RuleCount == 0)
                return current;
            if (current.Revision >= BrowserRoutingContract.MaxRevision)
                throw new BrowserRoutingValidationException([new(BrowserRoutingValidator.Codes.InvalidRevision, "/revision")]);
            var next = BrowserRoutingSnapshot.Create(new BrowserRoutingState(
                current.StateGeneration, current.Revision + 1, BrowserRoutingContract.RouteDirect, []));
            if (!Commit(next))
                throw new BrowserRoutingPersistenceException();
            return next;
        }
    }

    /// <summary>
    /// Explicit destructive reset: a new generation, revision 0, Direct, no rules.
    /// The only way to recover from a corrupt file; the old file stays as the backup.
    /// </summary>
    public BrowserRoutingSnapshot Reset()
    {
        lock (_writeLock)
        {
            var fresh = BrowserRoutingSnapshot.Create(new BrowserRoutingState(
                _newGeneration(), 0, BrowserRoutingContract.RouteDirect, []));
            if (!Commit(fresh))
                throw new BrowserRoutingPersistenceException();
            return fresh;
        }
    }

    private BrowserRoutingStoreStatus MarkUnavailable(string reason)
    {
        _current = null;
        _unavailableReason = reason;
        return BrowserRoutingStoreStatus.Unavailable;
    }

    private bool Commit(BrowserRoutingSnapshot snapshot)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var bytes = Serialize(snapshot.State);
            using (var stream = new FileStream(_tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            BeforeCommitForTests?.Invoke(_tempPath);
            if (File.Exists(_path))
                File.Replace(_tempPath, _path, _backupPath, ignoreMetadataErrors: true);
            else
                File.Move(_tempPath, _path);
            _current = snapshot;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            TryDelete(_tempPath);
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    internal BrowserRoutingSnapshot? Parse(byte[] bytes, out bool legacy, out string reason)
    {
        legacy = false;
        reason = BrowserRoutingUnavailableReason.Corrupt;
        try
        {
            using var document = JsonDocument.Parse(bytes, ReadOptions);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            string generation;
            if (root.TryGetProperty("documentVersion", out var version))
            {
                if (!(BrowserRoutingValidator.TryReadSafeInteger(version, out var v) && v == DocumentVersion))
                {
                    reason = BrowserRoutingUnavailableReason.UnsupportedDocument;
                    return null;
                }
                if (!root.TryGetProperty("documentType", out var type) || type.ValueKind != JsonValueKind.String ||
                    type.GetString() != DocumentType)
                    return null;
                if (!root.TryGetProperty("stateGeneration", out var gen) || gen.ValueKind != JsonValueKind.String ||
                    !StateGenerationFormat.IsValid(gen.GetString()))
                    return null;
                generation = gen.GetString()!;
            }
            else
            {
                if (root.TryGetProperty("documentType", out _) || root.TryGetProperty("stateGeneration", out _))
                    return null;
                legacy = true;
                generation = _newGeneration();
            }

            var issues = new List<BrowserRoutingIssue>();
            var state = BrowserRoutingValidator.TryParseStateV1(root, issues, legacy ? null : EnvelopeFields);
            if (state is null)
                return null;
            var (revision, defaultRoute, rules) = state.Value;
            return BrowserRoutingSnapshot.Create(new BrowserRoutingState(generation, revision, defaultRoute, rules));
        }
        catch (JsonException)
        {
            return null;
        }
        catch (BrowserRoutingValidationException)
        {
            return null;
        }
    }

    public static byte[] Serialize(BrowserRoutingState state)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("documentType", DocumentType);
            writer.WriteNumber("documentVersion", DocumentVersion);
            writer.WriteString("stateGeneration", state.StateGeneration);
            writer.WriteNumber("schemaVersion", state.SchemaVersion);
            writer.WriteNumber("revision", state.Revision);
            writer.WriteString("defaultRoute", state.DefaultRoute);
            writer.WriteStartArray("rules");
            foreach (var rule in state.Rules)
                BrowserRoutingSnapshot.WriteRule(writer, rule);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return buffer.WrittenSpan.ToArray();
    }
}

public sealed class BrowserRoutingUnavailableException(string reason)
    : Exception("Browser routing state is unavailable: " + reason)
{
    public string Reason { get; } = reason;
}

public sealed class BrowserRoutingConcurrencyException(long currentRevision)
    : Exception("Browser routing state revision has changed.")
{
    public long CurrentRevision { get; } = currentRevision;
}

public sealed class BrowserRoutingPersistenceException()
    : Exception("Browser routing state could not be persisted; the previous state is kept.");

public sealed class BrowserRoutingRuleNotFoundException(string ruleId)
    : Exception("Browser routing rule was not found.")
{
    public string RuleId { get; } = ruleId;
}
