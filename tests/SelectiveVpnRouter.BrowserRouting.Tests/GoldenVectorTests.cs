using System.Text.Json;
using SelectiveVpnRouter.Core.BrowserRouting;
using Xunit;

namespace SelectiveVpnRouter.BrowserRouting.Tests;

public class GoldenVectorTests
{
    private static readonly JsonElement Vectors = JsonDocument.Parse(File.ReadAllBytes(Repo.VectorsPath)).RootElement.Clone();

    public static IEnumerable<object?[]> Hosts() =>
        Vectors.GetProperty("hosts").EnumerateArray()
            .Select(v => new object?[] { v.GetProperty("input").GetString()!, v.GetProperty("canonical").GetString() });

    public static IEnumerable<object[]> States() =>
        Vectors.GetProperty("states").EnumerateArray().Select(v => new object[] { v.GetProperty("name").GetString()! });

    [Theory]
    [MemberData(nameof(Hosts))]
    public void Service_accepts_exactly_the_canonical_hosts(string input, string? canonical)
    {
        Assert.Equal(canonical == input, BrowserRoutingValidator.IsCanonicalHost(input));
        if (canonical is not null)
            Assert.True(BrowserRoutingValidator.IsCanonicalHost(canonical), canonical);
    }

    [Theory]
    [MemberData(nameof(States))]
    public void Service_state_validation_matches_the_vector(string name)
    {
        var vector = Vectors.GetProperty("states").EnumerateArray().Single(v => v.GetProperty("name").GetString() == name);
        var expected = vector.GetProperty("service");
        var issues = new List<BrowserRoutingIssue>();
        var parsed = BrowserRoutingValidator.TryParseStateV1(vector.GetProperty("state"), issues);

        Assert.Equal(expected.GetProperty("valid").GetBoolean(), parsed is not null);
        var codes = issues.Select(i => i.Code).Distinct().Order(StringComparer.Ordinal).ToArray();
        var expectedCodes = expected.GetProperty("codes").EnumerateArray().Select(c => c.GetString()!).ToArray();
        Assert.Equal(expectedCodes, codes);
    }

    [Fact]
    public void Vectors_cover_the_required_cases()
    {
        var names = Vectors.GetProperty("states").EnumerateArray().Select(v => v.GetProperty("name").GetString()).ToHashSet();
        foreach (var required in new[] { "valid_empty_direct", "valid_mixed_rules", "noncanonical_host_case_and_dot",
                     "noncanonical_unicode_host", "duplicate_conflict", "invalid_enums", "invalid_default_route_default" })
            Assert.Contains(required, names);
        Assert.Contains(Vectors.GetProperty("hosts").EnumerateArray(), v => v.GetProperty("canonical").GetString() == "xn--e1afmkfd.xn--p1ai");
    }

    [Fact]
    public void Copy_is_byte_identical_to_ext_vpn_route_when_present()
    {
        var extRoot = Environment.GetEnvironmentVariable("EXT_VPN_ROUTE_ROOT") ?? Path.Combine(Repo.Root, "..", "ext-vpn-route");
        var reference = Path.Combine(extRoot, "contracts", "browser-routing-v1", "golden-vectors.json");
        if (!File.Exists(reference))
            return;
        Assert.Equal(File.ReadAllBytes(reference), File.ReadAllBytes(Repo.VectorsPath));
    }
}

public class ValidatorTests
{
    [Fact]
    public void Too_many_rules_is_rejected_without_truncation()
    {
        var issues = BrowserRoutingValidator.ValidateRuleSet(Rules.Many(BrowserRoutingContract.MaxRules + 1));
        Assert.Equal(BrowserRoutingValidator.Codes.TooManyRules, Assert.Single(issues).Code);
        Assert.Empty(BrowserRoutingValidator.ValidateRuleSet(Rules.Many(BrowserRoutingContract.MaxRules)));
    }

    [Fact]
    public void Worst_case_rule_fits_the_rule_byte_bound()
    {
        var worst = Rules.Worst(1);
        Assert.Empty(BrowserRoutingValidator.ValidateRule(worst, "/rules/0"));
        var bytes = BrowserRoutingSnapshot.SerializeRule(worst).Length;
        Assert.InRange(bytes, 7000, BrowserRoutingIpcProtocol.MaxRuleBytes);
    }

    [Fact]
    public void Page_count_bound_covers_10000_worst_case_rules()
    {
        var minRulesPerPage = (BrowserRoutingIpcProtocol.PageRulesBudgetBytes - 2) / (BrowserRoutingIpcProtocol.MaxRuleBytes + 1);
        var maxPages = (BrowserRoutingContract.MaxRules + minRulesPerPage - 1) / minRulesPerPage;
        Assert.True(maxPages <= BrowserRoutingIpcProtocol.MaxPagesPerSnapshot, $"{maxPages} pages");
    }

    [Fact]
    public void Snapshot_rejects_invalid_state_and_orders_rules_by_id()
    {
        Assert.Throws<BrowserRoutingValidationException>(() => Rules.Snapshot([Rules.Make(1, "Example.com")]));
        Assert.Throws<BrowserRoutingValidationException>(() => Rules.Snapshot([Rules.Make(1, "a.example"), Rules.Make(1, "b.example")]));
        Assert.Throws<BrowserRoutingValidationException>(() =>
            BrowserRoutingSnapshot.Create(new BrowserRoutingState("not-a-guid", 0, "Direct", [])));
        Assert.Throws<BrowserRoutingValidationException>(() =>
            BrowserRoutingSnapshot.Create(new BrowserRoutingState(StateGenerationFormat.New(), 0, "Default", [])));

        var snapshot = Rules.Snapshot([Rules.Make(3), Rules.Make(1), Rules.Make(2)]);
        Assert.Equal(["r00001", "r00002", "r00003"], snapshot.State.Rules.Select(r => r.Id));
    }

    [Fact]
    public void Generation_format_is_lowercase_uuid()
    {
        Assert.True(StateGenerationFormat.IsValid(StateGenerationFormat.New()));
        Assert.False(StateGenerationFormat.IsValid("9B2F6C1E-1D2A-4F57-9A43-3F2A9D7C1B10"));
        Assert.False(StateGenerationFormat.IsValid("9b2f6c1e1d2a4f579a433f2a9d7c1b10"));
        Assert.False(StateGenerationFormat.IsValid(null));
    }
}
