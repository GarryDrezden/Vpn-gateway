using System.Globalization;
using System.Text.Json;

namespace SelectiveVpnRouter.Core.BrowserRouting;

/// <summary>
/// Structural and domain validation of BrowserRoutingStateV1 on the Service side.
///
/// The Service only stores canonical state: hosts must already be in canonical ASCII form
/// (lowercase, Punycode, no trailing dot). It never normalizes or repairs input — the reference
/// normalizer (UTS #46) lives in the ext-vpn-route domain layer, and both sides are pinned to the
/// same golden vectors. Issue codes match the JS validator.
/// </summary>
public static class BrowserRoutingValidator
{
    public static class Codes
    {
        public const string InvalidType = "invalid_type";
        public const string MissingField = "missing_field";
        public const string UnknownField = "unknown_field";
        public const string DuplicateField = "duplicate_field";
        public const string InvalidId = "invalid_id";
        public const string InvalidName = "invalid_name";
        public const string InvalidHost = "invalid_host";
        public const string UnknownMatchType = "unknown_match_type";
        public const string UnknownRouteMode = "unknown_route_mode";
        public const string InvalidEnabled = "invalid_enabled";
        public const string UnknownSource = "unknown_source";
        public const string InvalidNotes = "invalid_notes";
        public const string DuplicateRuleId = "duplicate_rule_id";
        public const string ConflictingRules = "conflicting_rules";
        public const string TooManyRules = "too_many_rules";
        public const string UnsupportedSchemaVersion = "unsupported_schema_version";
        public const string InvalidRevision = "invalid_revision";
        public const string InvalidDefaultRoute = "invalid_default_route";
        public const string InvalidStateGeneration = "invalid_state_generation";
    }

    private static readonly string[] StateFields = ["schemaVersion", "revision", "defaultRoute", "rules"];
    private static readonly string[] RuleRequiredFields = ["id", "name", "host", "matchType", "routeMode", "enabled", "source"];
    private static readonly HashSet<string> RuleFields = new([.. RuleRequiredFields, "notes"], StringComparer.Ordinal);

    private static readonly IdnMapping Idn = new() { AllowUnassigned = false, UseStd3AsciiRules = false };

    /// <summary>True when <paramref name="host"/> is a canonical ASCII domain host of the contract.</summary>
    public static bool IsCanonicalHost(string? host)
    {
        if (string.IsNullOrEmpty(host) || host.Length > BrowserRoutingContract.MaxHostLength)
            return false;

        var labels = host.Split('.');
        foreach (var label in labels)
        {
            if (!IsCanonicalLabel(label))
                return false;
        }

        // WHATWG URL treats a host whose last label is a number (decimal or 0x-hex) as IPv4.
        var last = labels[^1];
        if (last.All(char.IsAsciiDigit))
            return false;
        if (last.StartsWith("0x", StringComparison.Ordinal) && last.Skip(2).All(char.IsAsciiHexDigitLower))
            return false;
        return true;
    }

    private static bool IsCanonicalLabel(string label)
    {
        if (label.Length is 0 or > BrowserRoutingContract.MaxLabelLength)
            return false;
        foreach (var c in label)
        {
            if (!(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-'))
                return false;
        }
        if (label[0] == '-' || label[^1] == '-' || label == "xn--")
            return false;

        if (label.StartsWith("xn--", StringComparison.Ordinal))
        {
            try
            {
                var unicode = Idn.GetUnicode(label);
                if (!string.Equals(Idn.GetAscii(unicode), label, StringComparison.Ordinal))
                    return false;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
        return true;
    }

    public static IReadOnlyList<BrowserRoutingIssue> ValidateRule(BrowserRoutingRule rule, string path)
    {
        var issues = new List<BrowserRoutingIssue>();
        if (!IsValidId(rule.Id))
            issues.Add(new(Codes.InvalidId, path + "/id"));
        if (!IsValidName(rule.Name))
            issues.Add(new(Codes.InvalidName, path + "/name"));
        if (!IsCanonicalHost(rule.Host))
            issues.Add(new(Codes.InvalidHost, path + "/host"));
        if (!BrowserRoutingContract.MatchTypes.Contains(rule.MatchType))
            issues.Add(new(Codes.UnknownMatchType, path + "/matchType"));
        if (!BrowserRoutingContract.RouteModes.Contains(rule.RouteMode))
            issues.Add(new(Codes.UnknownRouteMode, path + "/routeMode"));
        if (!BrowserRoutingContract.RuleSources.Contains(rule.Source))
            issues.Add(new(Codes.UnknownSource, path + "/source"));
        if (rule.Notes is not null && !IsValidNotes(rule.Notes))
            issues.Add(new(Codes.InvalidNotes, path + "/notes"));
        return issues;
    }

    /// <summary>Validates every rule, id uniqueness (disabled rules included) and conflicts between enabled rules.</summary>
    public static IReadOnlyList<BrowserRoutingIssue> ValidateRuleSet(IReadOnlyList<BrowserRoutingRule> rules, string path = "/rules")
    {
        var issues = new List<BrowserRoutingIssue>();
        if (rules.Count > BrowserRoutingContract.MaxRules)
        {
            issues.Add(new(Codes.TooManyRules, path));
            return issues;
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var active = new Dictionary<(string, string), int>();
        for (var i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            var rulePath = path + "/" + i.ToString(CultureInfo.InvariantCulture);
            var ruleIssues = ValidateRule(rule, rulePath);
            issues.AddRange(ruleIssues);
            if (rule.Id is not null && !ids.Add(rule.Id))
                issues.Add(new(Codes.DuplicateRuleId, rulePath + "/id"));
            if (ruleIssues.Count > 0 || !rule.Enabled)
                continue;
            var key = (rule.MatchType, rule.Host);
            active[key] = active.TryGetValue(key, out var count) ? count + 1 : 1;
        }

        foreach (var (_, count) in active.OrderBy(entry => entry.Key))
        {
            if (count > 1)
                issues.Add(new(Codes.ConflictingRules, path));
        }
        return issues;
    }

    public static IReadOnlyList<BrowserRoutingIssue> ValidateState(BrowserRoutingState state)
    {
        var issues = new List<BrowserRoutingIssue>();
        if (!StateGenerationFormat.IsValid(state.StateGeneration))
            issues.Add(new(Codes.InvalidStateGeneration, "/stateGeneration"));
        if (state.Revision is < 0 or > BrowserRoutingContract.MaxRevision)
            issues.Add(new(Codes.InvalidRevision, "/revision"));
        if (!BrowserRoutingContract.DefaultRoutes.Contains(state.DefaultRoute))
            issues.Add(new(Codes.InvalidDefaultRoute, "/defaultRoute"));
        issues.AddRange(ValidateRuleSet(state.Rules));
        return issues;
    }

    /// <summary>
    /// Strictly parses a BrowserRoutingStateV1 JSON object (schemaVersion, revision, defaultRoute, rules).
    /// Unknown, duplicate or mistyped fields are issues; nothing is coerced.
    /// </summary>
    public static (long Revision, string DefaultRoute, IReadOnlyList<BrowserRoutingRule> Rules)? TryParseStateV1(
        JsonElement element, List<BrowserRoutingIssue> issues, ISet<string>? extraAllowedFields = null)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            issues.Add(new(Codes.InvalidType, ""));
            return null;
        }

        var fields = ReadFields(element, "", issues, StateFields.ToHashSet(StringComparer.Ordinal), extraAllowedFields);
        foreach (var name in StateFields)
        {
            if (!fields.ContainsKey(name))
                issues.Add(new(Codes.MissingField, "/" + name));
        }

        if (fields.TryGetValue("schemaVersion", out var schema) &&
            !(TryReadSafeInteger(schema, out var schemaValue) && schemaValue == BrowserRoutingContract.SchemaVersion))
            issues.Add(new(Codes.UnsupportedSchemaVersion, "/schemaVersion"));

        long revision = 0;
        if (fields.TryGetValue("revision", out var rev) && !(TryReadSafeInteger(rev, out revision) && revision >= 0))
            issues.Add(new(Codes.InvalidRevision, "/revision"));

        string? defaultRoute = null;
        if (fields.TryGetValue("defaultRoute", out var route))
        {
            defaultRoute = route.ValueKind == JsonValueKind.String ? route.GetString() : null;
            if (defaultRoute is null || !BrowserRoutingContract.DefaultRoutes.Contains(defaultRoute))
                issues.Add(new(Codes.InvalidDefaultRoute, "/defaultRoute"));
        }

        List<BrowserRoutingRule>? rules = null;
        if (fields.TryGetValue("rules", out var rulesElement))
            rules = TryParseRules(rulesElement, "/rules", issues);

        if (issues.Count > 0 || defaultRoute is null || rules is null)
            return null;
        return (revision, defaultRoute, rules);
    }

    private static List<BrowserRoutingRule>? TryParseRules(JsonElement element, string path, List<BrowserRoutingIssue> issues)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            issues.Add(new(Codes.InvalidType, path));
            return null;
        }
        if (element.GetArrayLength() > BrowserRoutingContract.MaxRules)
        {
            issues.Add(new(Codes.TooManyRules, path));
            return null;
        }

        var rules = new List<BrowserRoutingRule>();
        var structural = issues.Count;
        var index = 0;
        foreach (var item in element.EnumerateArray())
        {
            var rule = TryParseRule(item, path + "/" + index.ToString(CultureInfo.InvariantCulture), issues);
            if (rule is not null)
                rules.Add(rule);
            index++;
        }

        // Domain checks run on the full set so duplicate ids and conflicts are reported like in JS.
        var parsedAll = rules.Count == element.GetArrayLength();
        if (parsedAll)
            issues.AddRange(ValidateRuleSet(rules, path));
        return issues.Count == structural && parsedAll ? rules : null;
    }

    public static BrowserRoutingRule? TryParseRule(JsonElement element, string path, List<BrowserRoutingIssue> issues)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            issues.Add(new(Codes.InvalidType, path));
            return null;
        }

        var before = issues.Count;
        var fields = ReadFields(element, path, issues, RuleFields, null);
        foreach (var required in RuleRequiredFields)
        {
            if (!fields.ContainsKey(required))
                issues.Add(new(Codes.MissingField, path + "/" + required));
        }

        string? Str(string field, string code)
        {
            if (!fields.TryGetValue(field, out var value))
                return null;
            if (value.ValueKind == JsonValueKind.String)
                return value.GetString();
            issues.Add(new(code, path + "/" + field));
            return null;
        }

        var id = Str("id", Codes.InvalidId);
        var name = Str("name", Codes.InvalidName);
        var host = Str("host", Codes.InvalidHost);
        var matchType = Str("matchType", Codes.UnknownMatchType);
        var routeMode = Str("routeMode", Codes.UnknownRouteMode);
        var source = Str("source", Codes.UnknownSource);

        var enabled = false;
        if (fields.TryGetValue("enabled", out var enabledElement))
        {
            if (enabledElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
                enabled = enabledElement.GetBoolean();
            else
                issues.Add(new(Codes.InvalidEnabled, path + "/enabled"));
        }

        string? notes = null;
        if (fields.TryGetValue("notes", out var notesElement) && notesElement.ValueKind != JsonValueKind.Null)
        {
            if (notesElement.ValueKind == JsonValueKind.String)
                notes = notesElement.GetString();
            else
                issues.Add(new(Codes.InvalidNotes, path + "/notes"));
        }

        if (issues.Count > before || id is null || name is null || host is null ||
            matchType is null || routeMode is null || source is null)
            return null;

        var rule = new BrowserRoutingRule(id, name, host, matchType, routeMode, enabled, source, notes);
        var ruleIssues = ValidateRule(rule, path);
        if (ruleIssues.Count > 0)
        {
            issues.AddRange(ruleIssues);
            return null;
        }
        return rule;
    }

    private static Dictionary<string, JsonElement> ReadFields(
        JsonElement element, string path, List<BrowserRoutingIssue> issues, ISet<string> allowed, ISet<string>? extraAllowed)
    {
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (extraAllowed is not null && extraAllowed.Contains(property.Name))
                continue;
            if (!allowed.Contains(property.Name))
                issues.Add(new(Codes.UnknownField, path + "/" + property.Name));
            else if (!fields.TryAdd(property.Name, property.Value))
                issues.Add(new(Codes.DuplicateField, path + "/" + property.Name));
        }
        return fields;
    }

    /// <summary>JSON number that is an integer in 0..2^53-1 (1.0 counts, like Number.isSafeInteger).</summary>
    public static bool TryReadSafeInteger(JsonElement element, out long value)
    {
        value = 0;
        if (element.ValueKind != JsonValueKind.Number)
            return false;
        if (element.TryGetInt64(out value))
            return value is >= 0 and <= BrowserRoutingContract.MaxRevision;
        if (element.TryGetDouble(out var d) && d >= 0 && d <= BrowserRoutingContract.MaxRevision && Math.Floor(d) == d)
        {
            value = (long)d;
            return true;
        }
        return false;
    }

    private static bool IsValidId(string? id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > BrowserRoutingContract.MaxIdLength)
            return false;
        foreach (var c in id)
        {
            if (!(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-'))
                return false;
        }
        return true;
    }

    private static bool IsValidName(string? name)
    {
        if (name is null || name.Length > BrowserRoutingContract.MaxNameLength)
            return false;
        var blank = true;
        foreach (var c in name)
        {
            if (c is <= '\u001F' or >= '\u007F' and <= '\u009F')
                return false;
            if (!IsJsWhitespace(c))
                blank = false;
        }
        return !blank;
    }

    private static bool IsValidNotes(string notes)
    {
        if (notes.Length > BrowserRoutingContract.MaxNotesLength)
            return false;
        foreach (var c in notes)
        {
            if (c is '\t' or '\n' or '\r')
                continue;
            if (c is <= '\u001F' or >= '\u007F' and <= '\u009F')
                return false;
        }
        return true;
    }

    // String.prototype.trim() whitespace: char.IsWhiteSpace plus U+FEFF, minus U+0085 (a control in JS).
    private static bool IsJsWhitespace(char c) => c == '\uFEFF' || (c != '\u0085' && char.IsWhiteSpace(c));
}
