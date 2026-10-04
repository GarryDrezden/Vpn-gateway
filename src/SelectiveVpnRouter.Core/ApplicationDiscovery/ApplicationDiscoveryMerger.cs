namespace SelectiveVpnRouter.Core.ApplicationDiscovery;

public static class ApplicationDiscoveryMerger
{
    public static IReadOnlyList<DiscoveredApplication> Merge(IEnumerable<DiscoveredApplicationCandidate> candidates)
    {
        Dictionary<string, List<DiscoveredApplicationCandidate>> groups = new(StringComparer.OrdinalIgnoreCase);
        foreach (DiscoveredApplicationCandidate candidate in candidates)
        {
            if (!ApplicationDiscoveryMergeIdentity.TryGetMergeKey(candidate, out string key))
            {
                continue;
            }

            if (!groups.TryGetValue(key, out List<DiscoveredApplicationCandidate>? list))
            {
                list = [];
                groups[key] = list;
            }

            list.Add(candidate);
        }

        List<DiscoveredApplication> merged = [];
        foreach ((string key, List<DiscoveredApplicationCandidate> group) in groups)
        {
            merged.Add(MergeGroup(key, group));
        }

        return merged
            .OrderBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(a => a.ExecutablePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static bool TryNormalizeIdentity(string executablePath, out string normalizedKey)
    {
        normalizedKey = "";
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        try
        {
            normalizedKey = ApplicationRulesHelper.NormalizeExePath(executablePath);
            return normalizedKey.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static DiscoveredApplication MergeGroup(string normalizedKey, IReadOnlyList<DiscoveredApplicationCandidate> group)
    {
        DiscoverySource sources = DiscoverySource.None;
        bool isRunning = false;
        string? publisher = null;
        PackagedApplicationIdentity? packageIdentity = null;
        ApplicationDiscoveryLaunchConfidence confidence = ApplicationDiscoveryLaunchConfidence.Low;

        foreach (DiscoveredApplicationCandidate item in group)
        {
            sources |= item.Source;
            isRunning |= item.IsRunning;
            publisher ??= NormalizePublisher(item.Publisher);
            confidence = MaxConfidence(confidence, item.LaunchConfidence);
            packageIdentity = SelectBetterPackageIdentity(packageIdentity, item.PackageIdentity);
        }

        string displayName = SelectBestDisplayName(group);
        string executablePath = SelectBestExecutablePath(group);
        string? iconPath = SelectBestIconPath(group);

        return new DiscoveredApplication
        {
            Id = normalizedKey,
            DisplayName = displayName,
            ExecutablePath = executablePath,
            Publisher = publisher,
            IconPath = iconPath ?? executablePath,
            Sources = sources,
            IsRunning = isRunning,
            LaunchConfidence = confidence,
            PackageIdentity = packageIdentity,
        };
    }

    private static PackagedApplicationIdentity? SelectBetterPackageIdentity(
        PackagedApplicationIdentity? current,
        PackagedApplicationIdentity? next)
    {
        if (next is null)
        {
            return current;
        }

        if (current is null)
        {
            return next;
        }

        if (next.HasStablePackageIdentity && !current.HasStablePackageIdentity)
        {
            return next;
        }

        if (current.HasStablePackageIdentity && !next.HasStablePackageIdentity)
        {
            return current;
        }

        return next;
    }

    private static string SelectBestExecutablePath(IReadOnlyList<DiscoveredApplicationCandidate> group)
    {
        DiscoveredApplicationCandidate? packaged = group
            .Where(c => c.Source.HasFlag(DiscoverySource.PackagedApp) && c.PackageIdentity?.HasStablePackageIdentity == true)
            .OrderByDescending(c => c.ExecutablePath.Length)
            .FirstOrDefault();

        if (packaged is not null)
        {
            return packaged.ExecutablePath;
        }

        return group
            .Select(c => c.ExecutablePath)
            .OrderByDescending(p => p.Length)
            .First();
    }

    private static string? SelectBestIconPath(IReadOnlyList<DiscoveredApplicationCandidate> group)
    {
        string? best = null;
        int bestScore = int.MinValue;
        foreach (DiscoveredApplicationCandidate item in group)
        {
            int score = ScoreIconPath(item.IconPath, item);
            if (score > bestScore)
            {
                bestScore = score;
                best = item.IconPath;
            }
        }

        return bestScore >= 0 ? best : null;
    }

    private static int ScoreIconPath(string? path, DiscoveredApplicationCandidate candidate)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return -1;
        }

        int score = 0;
        if (IsRasterOrIconFile(path))
        {
            score += 200;
        }

        if (candidate.Source.HasFlag(DiscoverySource.PackagedApp))
        {
            score += 100;
        }

        if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            score += 10;
        }

        if (string.Equals(path, candidate.ExecutablePath, StringComparison.OrdinalIgnoreCase))
        {
            score -= 50;
        }

        score += Math.Min(path.Length, 120);
        return score;
    }

    private static bool IsRasterOrIconFile(string path)
    {
        ReadOnlySpan<string> extensions = [".png", ".jpg", ".jpeg", ".ico", ".bmp"];
        foreach (string extension in extensions)
        {
            if (path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string SelectBestDisplayName(IReadOnlyList<DiscoveredApplicationCandidate> group)
    {
        static int Score(DiscoveredApplicationCandidate c)
        {
            int score = SourcePriority(c.Source);
            string? name = c.DisplayName?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                return score;
            }

            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                score -= 20;
            }

            if (c.Source.HasFlag(DiscoverySource.InteractiveWindow))
            {
                score -= 5;
            }

            score += Math.Min(name.Length, 80);
            return score;
        }

        DiscoveredApplicationCandidate? best = group
            .OrderByDescending(Score)
            .ThenBy(c => c.DisplayName ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
            .FirstOrDefault();

        if (best is null)
        {
            return Path.GetFileNameWithoutExtension(group[0].ExecutablePath);
        }

        if (!string.IsNullOrWhiteSpace(best.DisplayName))
        {
            return best.DisplayName.Trim();
        }

        return ApplicationRulesHelper.ResolveFriendlyAppName(best.ExecutablePath);
    }

    private static int SourcePriority(DiscoverySource source)
    {
        if (source.HasFlag(DiscoverySource.Uninstall))
        {
            return 400;
        }

        if (source.HasFlag(DiscoverySource.PackagedApp))
        {
            return 350;
        }

        if (source.HasFlag(DiscoverySource.StartMenu))
        {
            return 300;
        }

        if (source.HasFlag(DiscoverySource.AppPaths))
        {
            return 250;
        }

        if (source.HasFlag(DiscoverySource.InteractiveWindow))
        {
            return 150;
        }

        if (source.HasFlag(DiscoverySource.RunningProcess))
        {
            return 100;
        }

        return 0;
    }

    private static ApplicationDiscoveryLaunchConfidence MaxConfidence(
        ApplicationDiscoveryLaunchConfidence current,
        ApplicationDiscoveryLaunchConfidence next)
        => next > current ? next : current;

    private static string? NormalizePublisher(string? publisher)
        => string.IsNullOrWhiteSpace(publisher) ? null : publisher.Trim();
}
