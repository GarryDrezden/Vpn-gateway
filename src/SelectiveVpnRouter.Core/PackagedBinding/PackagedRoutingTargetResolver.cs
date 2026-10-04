namespace SelectiveVpnRouter.Core;

public static class PackagedRoutingTargetResolver
{
    public static PackagedRoutingTargetsForRule ResolveForRule(
        RoutingRule rule,
        IPackagedApplicationPathResolver? packagedResolver)
    {
        string primaryPath;
        try
        {
            primaryPath = ApplicationRulesHelper.NormalizeExePath(rule.Target);
        }
        catch (Exception)
        {
            primaryPath = rule.Target;
        }

        if (rule.PackagedBinding is null || packagedResolver is null)
        {
            return SingleTarget(rule, primaryPath, PackagedRoutingTargetKind.Primary);
        }

        PackagedApplicationPathResolveResult primaryResolved = packagedResolver.Resolve(rule.PackagedBinding);
        if (!primaryResolved.Found || string.IsNullOrWhiteSpace(primaryResolved.ResolvedExecutablePath))
        {
            return new PackagedRoutingTargetsForRule
            {
                Rule = rule,
                PrimaryExecutablePath = primaryPath,
                Targets =
                [
                    UnresolvedTarget(
                        rule.PackagedBinding.ApplicationId,
                        primaryPath,
                        PackagedRoutingTargetKind.Primary,
                        primaryResolved.Reason ?? "Primary executable not resolved."),
                ],
                VpnWfpExecutablePaths = rule.Mode == RouteMode.Vpn && File.Exists(primaryPath) ? [primaryPath] : [],
            };
        }

        string resolvedPrimary = ApplicationRulesHelper.NormalizeExePath(primaryResolved.ResolvedExecutablePath);
        string installRoot = primaryResolved.InstallRoot ?? InferInstallRoot(resolvedPrimary, primaryResolved.RelativeExecutablePath);
        string primaryRelative = primaryResolved.RelativeExecutablePath
            ?? rule.PackagedBinding.RelativeExecutablePath
            ?? string.Empty;

        var targets = new List<PackagedRoutingTarget>
        {
            new()
            {
                ApplicationId = rule.PackagedBinding.ApplicationId,
                ExecutablePath = resolvedPrimary,
                RelativeExecutablePath = primaryRelative.Replace('\\', '/'),
                Kind = PackagedRoutingTargetKind.Primary,
            },
        };

        IReadOnlyList<PackagedManifestApplicationEntry> manifestApps = AppxManifestApplicationLookup.ListApplications(installRoot);
        IReadOnlyList<PackagedManifestApplicationEntry> helpers = PackagedRoutingTargetAssociation.SelectAssociatedRoutingHelpers(
            manifestApps,
            rule.PackagedBinding.ApplicationId,
            primaryRelative);

        foreach (PackagedManifestApplicationEntry helper in helpers)
        {
            if (!PackagedInstallPathSecurity.IsSafeRelativeExecutable(helper.RelativeExecutablePath))
            {
                targets.Add(UnresolvedTarget(
                    helper.ApplicationId,
                    string.Empty,
                    PackagedRoutingTargetKind.AssociatedHelper,
                    "Unsafe helper executable path in manifest."));
                continue;
            }

            string helperPath = Path.GetFullPath(Path.Combine(installRoot, helper.RelativeExecutablePath));
            if (!PackagedInstallPathSecurity.IsExecutableWithinInstallRoot(installRoot, helperPath))
            {
                targets.Add(UnresolvedTarget(
                    helper.ApplicationId,
                    helperPath,
                    PackagedRoutingTargetKind.AssociatedHelper,
                    "Helper executable resolves outside install root."));
                continue;
            }

            if (!File.Exists(helperPath))
            {
                targets.Add(UnresolvedTarget(
                    helper.ApplicationId,
                    helperPath,
                    PackagedRoutingTargetKind.AssociatedHelper,
                    "Helper executable missing after package update."));
                continue;
            }

            targets.Add(new PackagedRoutingTarget
            {
                ApplicationId = helper.ApplicationId,
                ExecutablePath = ApplicationRulesHelper.NormalizeExePath(helperPath),
                RelativeExecutablePath = helper.RelativeExecutablePath.Replace('\\', '/'),
                Kind = PackagedRoutingTargetKind.AssociatedHelper,
            });
        }

        IReadOnlyList<string> wfpPaths = rule.Mode == RouteMode.Vpn
            ? targets.Where(t => t.Resolved && !string.IsNullOrWhiteSpace(t.ExecutablePath)).Select(t => t.ExecutablePath).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            : [];

        return new PackagedRoutingTargetsForRule
        {
            Rule = rule,
            PrimaryExecutablePath = resolvedPrimary,
            Targets = targets,
            VpnWfpExecutablePaths = wfpPaths,
        };
    }

    public static IReadOnlyList<PackagedRoutingTargetsForRule> ResolveAllVpnApplicationRules(
        IEnumerable<RoutingRule> rules,
        IPackagedApplicationPathResolver? packagedResolver) =>
        RuleEvaluator.VpnApplicationRules(rules)
            .Select(r => ResolveForRule(r, packagedResolver))
            .ToList();

    public static IEnumerable<string> EnumerateVpnWfpExecutablePaths(
        IEnumerable<RoutingRule> rules,
        bool paused,
        IPackagedApplicationPathResolver? packagedResolver,
        IEnumerable<string>? extraVpnExePaths = null)
    {
        if (paused)
        {
            yield break;
        }

        foreach (PackagedRoutingTargetsForRule plan in ResolveAllVpnApplicationRules(rules, packagedResolver))
        {
            foreach (string path in plan.VpnWfpExecutablePaths)
            {
                if (File.Exists(path))
                {
                    yield return path;
                }
            }
        }

        foreach (string extra in extraVpnExePaths ?? [])
        {
            if (string.IsNullOrWhiteSpace(extra))
            {
                continue;
            }

            string full = Path.GetFullPath(extra);
            if (File.Exists(full))
            {
                yield return full;
            }
        }
    }

    private static PackagedRoutingTargetsForRule SingleTarget(RoutingRule rule, string path, PackagedRoutingTargetKind kind) =>
        new()
        {
            Rule = rule,
            PrimaryExecutablePath = path,
            Targets =
            [
                new PackagedRoutingTarget
                {
                    ApplicationId = rule.PackagedBinding?.ApplicationId ?? rule.Name,
                    ExecutablePath = path,
                    RelativeExecutablePath = Path.GetFileName(path),
                    Kind = kind,
                },
            ],
            VpnWfpExecutablePaths = rule.Mode == RouteMode.Vpn && File.Exists(path) ? [path] : [],
        };

    private static PackagedRoutingTarget UnresolvedTarget(
        string applicationId,
        string pathHint,
        PackagedRoutingTargetKind kind,
        string reason) =>
        new()
        {
            ApplicationId = applicationId,
            ExecutablePath = pathHint,
            RelativeExecutablePath = string.Empty,
            Kind = kind,
            Resolved = false,
            UnresolvedReason = reason,
        };

    private static string InferInstallRoot(string resolvedPrimary, string? relativeExecutablePath)
    {
        if (!string.IsNullOrWhiteSpace(relativeExecutablePath))
        {
            string rel = relativeExecutablePath.Replace('/', Path.DirectorySeparatorChar);
            if (resolvedPrimary.EndsWith(rel, StringComparison.OrdinalIgnoreCase))
            {
                return resolvedPrimary[..^rel.Length].TrimEnd(Path.DirectorySeparatorChar);
            }
        }

        return Path.GetDirectoryName(resolvedPrimary) ?? resolvedPrimary;
    }
}
