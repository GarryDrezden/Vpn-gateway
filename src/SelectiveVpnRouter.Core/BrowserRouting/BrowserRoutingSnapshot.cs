using System.Buffers;
using System.Text.Json;

namespace SelectiveVpnRouter.Core.BrowserRouting;

/// <summary>
/// Immutable, validated view of one state version. Every rule is serialized once, so pages are
/// cut by exact byte counts and two reads of the same identity always produce identical bytes.
/// </summary>
public sealed class BrowserRoutingSnapshot
{
    private readonly byte[][] _ruleJson;

    private BrowserRoutingSnapshot(BrowserRoutingState state, byte[][] ruleJson)
    {
        State = state;
        _ruleJson = ruleJson;
    }

    public BrowserRoutingState State { get; }
    public string StateGeneration => State.StateGeneration;
    public long Revision => State.Revision;
    public int RuleCount => _ruleJson.Length;

    /// <summary>Validates the state, sorts rules into canonical order and pre-serializes them.</summary>
    public static BrowserRoutingSnapshot Create(BrowserRoutingState state)
    {
        var ordered = state.Rules.OrderBy(rule => rule.Id, StringComparer.Ordinal).ToArray();
        var canonical = state with { Rules = ordered };
        var issues = BrowserRoutingValidator.ValidateState(canonical);
        if (issues.Count > 0)
            throw new BrowserRoutingValidationException(issues);

        var json = new byte[ordered.Length][];
        for (var i = 0; i < ordered.Length; i++)
        {
            json[i] = SerializeRule(ordered[i]);
            if (json[i].Length > BrowserRoutingIpcProtocol.MaxRuleBytes)
                throw new BrowserRoutingValidationException([new("rule_too_large", "/rules/" + i)]);
        }
        return new BrowserRoutingSnapshot(canonical, json);
    }

    public ReadOnlySpan<byte> RuleJson(int index) => _ruleJson[index];

    /// <summary>
    /// End index (exclusive) of the page that starts at <paramref name="startIndex"/>: as many rules as fit
    /// into <paramref name="budgetBytes"/> of the serialized "rules" array, at least one.
    /// </summary>
    public int PageEnd(int startIndex, int budgetBytes)
    {
        if (startIndex < 0 || startIndex >= RuleCount)
            throw new ArgumentOutOfRangeException(nameof(startIndex));
        long used = 2; // "[" and "]"
        var end = startIndex;
        while (end < RuleCount)
        {
            var next = used + _ruleJson[end].Length + (end > startIndex ? 1 : 0);
            if (next > budgetBytes && end > startIndex)
                break;
            used = next;
            end++;
        }
        return end;
    }

    public static byte[] SerializeRule(BrowserRoutingRule rule)
    {
        var buffer = new ArrayBufferWriter<byte>(256);
        using (var writer = new Utf8JsonWriter(buffer))
            WriteRule(writer, rule);
        return buffer.WrittenSpan.ToArray();
    }

    public static void WriteRule(Utf8JsonWriter writer, BrowserRoutingRule rule)
    {
        writer.WriteStartObject();
        writer.WriteString("id", rule.Id);
        writer.WriteString("name", rule.Name);
        writer.WriteString("host", rule.Host);
        writer.WriteString("matchType", rule.MatchType);
        writer.WriteString("routeMode", rule.RouteMode);
        writer.WriteBoolean("enabled", rule.Enabled);
        writer.WriteString("source", rule.Source);
        if (rule.Notes is null)
            writer.WriteNull("notes");
        else
            writer.WriteString("notes", rule.Notes);
        writer.WriteEndObject();
    }
}

public sealed class BrowserRoutingValidationException(IReadOnlyList<BrowserRoutingIssue> issues)
    : Exception("Browser routing state is invalid: " + string.Join(", ", issues.Take(5).Select(i => i.Code + " " + i.Path)))
{
    public IReadOnlyList<BrowserRoutingIssue> Issues { get; } = issues;
}
