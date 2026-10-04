using System.Diagnostics;
using SelectiveVpnRouter.Core.ApplicationDiscovery;

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
        MultiAppRoutingIsolation.TempRuleNameA,
        MultiAppRoutingIsolation.TempRuleNameB,
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

        return IsLikelyTempSessionPath(normalized) || IsProbeCopyInTemp(normalized) || IsMultiAppProbeExecutablePath(normalized);
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


    public static bool IsMultiAppProbeExecutablePath(string target) =>
        MultiAppRoutingProbePaths.IsUnderProbeRoot(target);

    public static bool IsProbeCopyInTemp(string fullPath)
    {
        if (!Path.GetFileName(fullPath).Equals(ProbeCopyHelper.ProbeExeName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string temp = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return fullPath.StartsWith(temp, StringComparison.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<RoutingRule> WithoutDiagnosticApplicationRules(IEnumerable<RoutingRule> rules) =>
        rules.Where(r => !IsDiagnosticApplicationRule(r)).ToList();

    public static AppConfiguration WithoutDiagnosticApplicationRules(AppConfiguration config) =>
        config with { Rules = WithoutDiagnosticApplicationRules(config.Rules).ToList() };

    public static IReadOnlyList<RoutingRule> GetDiagnosticApplicationRules(IEnumerable<RoutingRule> rules) =>
        rules.Where(IsDiagnosticApplicationRule).ToList();

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

    public static RoutingRule? FindApplicationRuleByPackagedBinding(
        IEnumerable<RoutingRule> rules,
        PackagedApplicationBinding binding) =>
        rules.FirstOrDefault(r =>
            r.Type == RuleType.Application
            && r.PackagedBinding is not null
            && PackagedApplicationRuleIdentity.AreSameLogicalApplication(r.PackagedBinding, binding));

    public static RoutingRule? FindApplicationRuleForDiscoveredApp(
        IEnumerable<RoutingRule> rules,
        DiscoveredApplication application)
    {
        if (TryCreateBindingFromDiscovery(application, out PackagedApplicationBinding? binding)
            && binding is not null)
        {
            RoutingRule? byBinding = FindApplicationRuleByPackagedBinding(rules, binding);
            if (byBinding is not null)
            {
                return byBinding;
            }
        }

        return FindApplicationRuleByPath(rules, application.ExecutablePath);
    }

    public static bool IsDuplicateApplicationRule(IEnumerable<RoutingRule> rules, string exePath) =>
        FindApplicationRuleByPath(rules, exePath) is not null;

    public static bool IsDuplicateApplicationRule(
        IEnumerable<RoutingRule> rules,
        string exePath,
        PackagedApplicationBinding? packagedBinding)
    {
        if (packagedBinding is not null
            && PackagedApplicationRuleIdentity.HasStableBinding(packagedBinding)
            && FindApplicationRuleByPackagedBinding(rules, packagedBinding) is not null)
        {
            return true;
        }

        return IsDuplicateApplicationRule(rules, exePath);
    }

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

    /// <summary>Unified display name for flows (Home, Connections). Does not affect path matching.</summary>
    public static string ResolveFlowApplicationDisplayName(
        FlowEvent flow,
        PackagedRoutingTargetIndex? packagedRoutingIndex = null)
    {
        if (!string.IsNullOrWhiteSpace(flow.RuleName))
        {
            return flow.RuleName.Trim();
        }

        if (packagedRoutingIndex?.TryGetLogicalRule(flow.ProcessPath, out RoutingRule? logicalRule, out _) == true
            && logicalRule is not null
            && !string.IsNullOrWhiteSpace(logicalRule.Name))
        {
            return logicalRule.Name.Trim();
        }

        if (!string.IsNullOrWhiteSpace(flow.ProcessPath))
        {
            return ResolveFriendlyAppName(flow.ProcessPath);
        }

        return "—";
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

        PackagedApplicationBinding? binding = TryCreateBindingFromExecutablePath(normalized);
        if (IsDuplicateApplicationRule(config.Rules, normalized, binding))
        {
            return ApplicationRuleAddResult.Duplicate(normalized);
        }

        string displayName = ResolveFriendlyAppName(normalized);
        RoutingRule rule = CreateApplicationRule(displayName, normalized, defaultMode, binding);
        return ApplicationRuleAddResult.Success(config with { Rules = config.Rules.Concat([rule]).ToList() }, rule);
    }

    public static ApplicationRuleAddResult TryAddApplicationRuleFromDiscovery(
        AppConfiguration config,
        DiscoveredApplication application,
        RouteMode defaultMode = RouteMode.Vpn)
    {
        if (string.IsNullOrWhiteSpace(application.ExecutablePath))
        {
            return ApplicationRuleAddResult.Fail("Path required.");
        }

        string normalized;
        try
        {
            normalized = NormalizeExePath(application.ExecutablePath);
        }
        catch (Exception ex)
        {
            return ApplicationRuleAddResult.Fail("Invalid path: " + ex.Message);
        }

        if (!File.Exists(normalized))
        {
            return ApplicationRuleAddResult.Fail("File not found: " + normalized);
        }

        PackagedApplicationBinding? binding = null;
        if (TryCreateBindingFromDiscovery(application, out PackagedApplicationBinding? fromDiscovery))
        {
            binding = fromDiscovery;
        }
        else
        {
            binding = TryCreateBindingFromExecutablePath(normalized);
        }

        if (IsDuplicateApplicationRule(config.Rules, normalized, binding))
        {
            return ApplicationRuleAddResult.Duplicate(normalized);
        }

        string displayName = string.IsNullOrWhiteSpace(application.DisplayName)
            ? ResolveFriendlyAppName(normalized)
            : application.DisplayName.Trim();
        RoutingRule rule = CreateApplicationRule(displayName, normalized, defaultMode, binding);
        return ApplicationRuleAddResult.Success(config with { Rules = config.Rules.Concat([rule]).ToList() }, rule);
    }

    public static bool TryCreateBindingFromDiscovery(
        DiscoveredApplication application,
        out PackagedApplicationBinding? binding)
    {
        binding = null;
        PackagedApplicationIdentity? identity = application.PackageIdentity;
        if (identity?.HasStablePackageIdentity != true)
        {
            return false;
        }

        string? userSid = InteractiveUserSid.TryGetCurrent();
        if (string.IsNullOrWhiteSpace(userSid))
        {
            return false;
        }

        binding = new PackagedApplicationBinding
        {
            PackageFamilyName = identity.PackageFamilyName!,
            ApplicationId = identity.ApplicationId!,
            RelativeExecutablePath = identity.RelativeExecutablePath,
            UserSid = userSid,
            ResolvedPackageFullName = identity.PackageFullName,
        };
        return true;
    }

    internal static PackagedApplicationBinding? TryCreateBindingFromExecutablePath(string normalizedExecutablePath)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        PackagedApplicationIdentity? identity = WindowsAppsPackageIdentityResolver.TryResolveFromExecutable(normalizedExecutablePath);
        if (identity?.HasStablePackageIdentity != true)
        {
            return null;
        }

        string? userSid = InteractiveUserSid.TryGetCurrent();
        if (string.IsNullOrWhiteSpace(userSid))
        {
            return null;
        }

        return new PackagedApplicationBinding
        {
            PackageFamilyName = identity.PackageFamilyName!,
            ApplicationId = identity.ApplicationId!,
            RelativeExecutablePath = identity.RelativeExecutablePath,
            UserSid = userSid,
            ResolvedPackageFullName = identity.PackageFullName,
        };
    }

    private static RoutingRule CreateApplicationRule(
        string displayName,
        string normalizedExecutablePath,
        RouteMode mode,
        PackagedApplicationBinding? packagedBinding)
    {
        RoutingRule rule = RoutingRule.Create(RuleType.Application, displayName, normalizedExecutablePath, mode);
        return packagedBinding is null ? rule : rule with { PackagedBinding = packagedBinding };
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