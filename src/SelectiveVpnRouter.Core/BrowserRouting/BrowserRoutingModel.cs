namespace SelectiveVpnRouter.Core.BrowserRouting;

/// <summary>
/// Constants of BrowserRoutingStateV1 (ext-vpn-route docs/browser-routing-contract-v1.md).
/// The Service is the authoritative owner of this state; browser clients only consume it.
/// </summary>
public static class BrowserRoutingContract
{
    public const int SchemaVersion = 1;
    public const int MaxRules = 10_000;
    public const int MaxIdLength = 64;
    public const int MaxNameLength = 120;
    public const int MaxNotesLength = 1000;
    public const int MaxHostLength = 253;
    public const int MaxLabelLength = 63;
    public const long MaxRevision = 9_007_199_254_740_991; // 2^53-1, Number.MAX_SAFE_INTEGER

    public const string ExactHost = "ExactHost";
    public const string DomainAndSubdomains = "DomainAndSubdomains";
    public const string RouteDefault = "Default";
    public const string RouteVpn = "VPN";
    public const string RouteDirect = "Direct";
    public const string SourceUser = "User";
    public const string SourceSystem = "System";

    public static readonly IReadOnlyList<string> MatchTypes = [ExactHost, DomainAndSubdomains];
    public static readonly IReadOnlyList<string> RouteModes = [RouteDefault, RouteVpn, RouteDirect];
    public static readonly IReadOnlyList<string> DefaultRoutes = [RouteVpn, RouteDirect];
    public static readonly IReadOnlyList<string> RuleSources = [SourceUser, SourceSystem];
}

public record BrowserRoutingRule(
    string Id,
    string Name,
    IReadOnlyList<string> Hosts,
    string MatchType,
    string RouteMode,
    bool Enabled,
    string Source,
    string? Notes)
{
    /// <summary>First canonical host; mirrors <c>hosts[0]</c> in JSON for backward compatibility.</summary>
    public string Host => Hosts[0];

    public virtual bool Equals(BrowserRoutingRule? other) =>
        other is not null
        && string.Equals(Id, other.Id, StringComparison.Ordinal)
        && string.Equals(Name, other.Name, StringComparison.Ordinal)
        && string.Equals(MatchType, other.MatchType, StringComparison.Ordinal)
        && string.Equals(RouteMode, other.RouteMode, StringComparison.Ordinal)
        && Enabled == other.Enabled
        && string.Equals(Source, other.Source, StringComparison.Ordinal)
        && string.Equals(Notes, other.Notes, StringComparison.Ordinal)
        && Hosts.SequenceEqual(other.Hosts);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Id, StringComparer.Ordinal);
        hash.Add(Name, StringComparer.Ordinal);
        foreach (var host in Hosts)
            hash.Add(host, StringComparer.Ordinal);
        hash.Add(MatchType, StringComparer.Ordinal);
        hash.Add(RouteMode, StringComparer.Ordinal);
        hash.Add(Enabled);
        hash.Add(Source, StringComparer.Ordinal);
        hash.Add(Notes);
        return hash.ToHashCode();
    }
}

/// <summary>
/// Authoritative browser routing state. <see cref="StateGeneration"/> identifies the lineage
/// (created once, persisted, changed only on creation, explicit reset or destructive migration);
/// <see cref="Revision"/> grows inside one generation. Rules are kept in canonical order (ordinal by id).
/// </summary>
public sealed record BrowserRoutingState(
    string StateGeneration,
    long Revision,
    string DefaultRoute,
    IReadOnlyList<BrowserRoutingRule> Rules)
{
    public int SchemaVersion => BrowserRoutingContract.SchemaVersion;
}

public sealed record BrowserRoutingIssue(string Code, string Path);

public static class StateGenerationFormat
{
    public static string New() => Guid.NewGuid().ToString("D");

    /// <summary>Lowercase RFC 4122 text form, exactly what <see cref="New"/> produces.</summary>
    public static bool IsValid(string? value)
    {
        if (value is null || value.Length != 36)
            return false;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            var dash = i is 8 or 13 or 18 or 23;
            if (dash ? c != '-' : !(c is >= '0' and <= '9' or >= 'a' and <= 'f'))
                return false;
        }
        return true;
    }
}
