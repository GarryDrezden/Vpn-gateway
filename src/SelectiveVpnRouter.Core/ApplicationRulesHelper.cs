using System.Diagnostics;

namespace SelectiveVpnRouter.Core;

public static class ApplicationRulesHelper
{
    private static readonly string[] KnownDiagnosticRuleNames =
    [
        "tmp-probe-vpn",
        "tmp-iso-vpn",
        "tmp-parent-vpn",
        "tmp-child-direct",
        "tmp-v6",
    ];

    public static string NormalizeExePath(string exePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exePath);
        return Path.GetFullPath(exePath.Trim());
    }

    public static bool PathsEqual(string left, string right) =>
        string.Equals(NormalizeExePath(left), NormalizeExePath(right), StringComparison.OrdinalIgnoreCase);

    public static bool IsDiagnosticApplicationRule(RoutingRule rule)
    {
        if (rule.Type != RuleType.Application)
        {
            return false;
        }

        if (KnownDiagnosticRuleNames.Contains(rule.Name, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        if (IsDiagnosticExecutablePath(rule.Target))
        {
            return true;
        }

        return rule.Name.StartsWith("tmp-", StringComparison.OrdinalIgnoreCase)
            && IsLikelyTempSessionPath(rule.Target);
    }

    public static bool IsPersistentUserApplicationRule(RoutingRule rule) =>
        rule.Type == RuleType.Application && !IsDiagnosticApplicationRule(rule);

    public static bool IsDiagnosticExecutablePath(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return false;
        }

        string normalized;
        try
        {
            normalized = Path.GetFullPath(target.Trim());
        }
        catch (Exception)
        {
            return false;
        }

        return IsLikelyTempSessionPath(normalized) || IsProbeCopyInTemp(normalized);
    }

    public static bool IsLikelyTempSessionPath(string fullPath)
    {
        string temp = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!fullPath.StartsWith(temp, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return fullPath.Contains(@"\svr-iso-", StringComparison.OrdinalIgnoreCase)
            || fullPath.Contains(@"\svr-pc-", StringComparison.OrdinalIgnoreCase)
            || fullPath.Contains(@"\svr-v6-", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsProbeCopyInTemp(string fullPath)
    {
        if (!Path.GetFileName(fullPath).Equals(ProbeCopyHelper.ProbeExeName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string temp = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return fullPath.StartsWith(temp, StringComparison.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<RoutingRule> GetPermanentApplicationRules(AppConfiguration config) =>
        config.Rules.Where(IsPersistentUserApplicationRule).ToList();

    public static IEnumerable<RoutingRule> FilterApplicationRules(
        IEnumerable<RoutingRule> rules,
        string? search,
        RouteMode? modeFilter)
    {
        IEnumerable<RoutingRule> query = rules;
        if (modeFilter is RouteMode mode)
        {
            query = query.Where(r => r.Mode == mode);
        }

        if (string.IsNullOrWhiteSpace(search))
        {
            return query;
        }

        string term = search.Trim();
        return query.Where(r =>
            r.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
            || r.Target.Contains(term, StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(r.Target).Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    public static RoutingRule? FindApplicationRuleByPath(IEnumerable<RoutingRule> rules, string exePath)
    {
        string normalized = NormalizeExePath(exePath);
        return rules.FirstOrDefault(r =>
            r.Type == RuleType.Application
            && string.Equals(NormalizeExePath(r.Target), normalized, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsDuplicateApplicationRule(IEnumerable<RoutingRule> rules, string exePath) =>
        FindApplicationRuleByPath(rules, exePath) is not null;

    public static string ResolveFriendlyAppName(string exePath)
    {
        string fileName = Path.GetFileName(exePath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return exePath;
        }

        try
        {
            if (!File.Exists(exePath))
            {
                return Path.GetFileNameWithoutExtension(fileName);
            }

            FileVersionInfo info = FileVersionInfo.GetVersionInfo(exePath);
            if (IsUsableDisplayName(info.FileDescription))
            {
                return info.FileDescription!.Trim();
            }

            if (IsUsableDisplayName(info.ProductName))
            {
                return info.ProductName!.Trim();
            }
        }
        catch (Exception)
        {
        }

        return Path.GetFileNameWithoutExtension(fileName);
    }

    public static ApplicationRuleAddResult TryAddApplicationRule(AppConfiguration config, string exePath, RouteMode defaultMode = RouteMode.Vpn)
    {
        if (string.IsNullOrWhiteSpace(exePath))
        {
            return ApplicationRuleAddResult.Fail("Path required.");
        }

        string normalized;
        try
        {
            normalized = NormalizeExePath(exePath);
        }
        catch (Exception ex)
        {
            return ApplicationRuleAddResult.Fail("Invalid path: " + ex.Message);
        }

        if (!File.Exists(normalized))
        {
            return ApplicationRuleAddResult.Fail("File not found: " + normalized);
        }

        if (IsDuplicateApplicationRule(config.Rules, normalized))
        {
            return ApplicationRuleAddResult.Duplicate(normalized);
        }

        string displayName = ResolveFriendlyAppName(normalized);
        RoutingRule rule = RoutingRule.Create(RuleType.Application, displayName, normalized, defaultMode);
        return ApplicationRuleAddResult.Success(config with { Rules = config.Rules.Concat([rule]).ToList() }, rule);
    }

    public static AppConfiguration? TrySetApplicationRouteMode(AppConfiguration config, Guid ruleId, RouteMode mode)
    {
        int index = config.Rules.ToList().FindIndex(r => r.Id == ruleId && r.Type == RuleType.Application);
        if (index < 0)
        {
            return null;
        }

        List<RoutingRule> rules = config.Rules.ToList();
        RoutingRule existing = rules[index];
        rules[index] = existing with { Mode = mode };
        return config with { Rules = rules };
    }

    public static AppConfiguration? TryRemoveApplicationRule(AppConfiguration config, Guid ruleId)
    {
        RoutingRule? rule = config.Rules.FirstOrDefault(r => r.Id == ruleId && r.Type == RuleType.Application);
        if (rule is null)
        {
            return null;
        }

        return config with { Rules = config.Rules.Where(r => r.Id != ruleId).ToList() };
    }

    public static int CountPermanentApplications(IEnumerable<RoutingRule> rules) =>
        rules.Count(IsPersistentUserApplicationRule);

    public static int CountVpnRoutedApplications(IEnumerable<RoutingRule> rules) =>
        rules.Count(r => IsPersistentUserApplicationRule(r) && r.Enabled && r.Mode == RouteMode.Vpn);

    public static int CountVpnRoutedApplications(AppConfiguration config) =>
        CountVpnRoutedApplications(config.Rules);

    public static AppConfiguration MergeRules(
        AppConfiguration config,
        IEnumerable<RoutingRule> applicationRules,
        IEnumerable<RoutingRule> advancedRules) =>
        config with { Rules = applicationRules.Concat(advancedRules).ToList() };

    private static bool IsUsableDisplayName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string trimmed = value.Trim();
        if (trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return trimmed.Length >= 2;
    }
}

public sealed record ApplicationRuleAddResult
{
    public bool Ok { get; init; }
    public bool IsDuplicate { get; init; }
    public string? Error { get; init; }
    public AppConfiguration? Config { get; init; }
    public RoutingRule? Rule { get; init; }

    public static ApplicationRuleAddResult Success(AppConfiguration config, RoutingRule rule) =>
        new() { Ok = true, Config = config, Rule = rule };

    public static ApplicationRuleAddResult Duplicate(string exePath) =>
        new() { IsDuplicate = true, Error = "Duplicate application.", Rule = null };

    public static ApplicationRuleAddResult Fail(string error) =>
        new() { Error = error };
}