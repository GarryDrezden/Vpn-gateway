namespace SelectiveVpnRouter.Core.ApplicationDiscovery;

public static class PackagedApplicationHeuristics
{
    public static bool IsSystemPackageInstallRoot(string installRoot)
    {
        if (string.IsNullOrWhiteSpace(installRoot))
        {
            return false;
        }

        try
        {
            string full = Path.GetFullPath(installRoot);
            return full.Contains(@"\Windows\SystemApps\", StringComparison.OrdinalIgnoreCase)
                || full.Contains(@"\Windows\WinSxS\", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static bool IsUserFacingApplicationEntry(
        string? applicationId,
        string? appListEntry,
        string relativeExecutablePath)
    {
        if (string.Equals(appListEntry, "none", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(applicationId)
            && IsInternalApplicationId(applicationId))
        {
            return false;
        }

        string normalizedExe = relativeExecutablePath.Replace('/', '\\');
        if (normalizedExe.Contains(@"\resources\", StringComparison.OrdinalIgnoreCase)
            || normalizedExe.Contains(@"\tools\", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string fileName = Path.GetFileName(normalizedExe);
        if (IsInternalExecutableName(fileName))
        {
            return false;
        }

        return true;
    }

    public static bool IsLowConfidencePackagedCandidate(DiscoveredApplicationCandidate candidate)
    {
        if (!candidate.Source.HasFlag(DiscoverySource.PackagedApp))
        {
            return false;
        }

        if (candidate.IsSystemComponent || IsSystemPackageInstallRoot(candidate.InstallLocation))
        {
            return true;
        }

        return false;
    }

    public static bool IsLowConfidenceStartMenuCandidate(DiscoveredApplicationCandidate candidate)
    {
        if (!candidate.Source.HasFlag(DiscoverySource.StartMenu))
        {
            return false;
        }

        string fileName = Path.GetFileName(candidate.ExecutablePath);
        if (IsInternalExecutableName(fileName))
        {
            return true;
        }

        if (candidate.DisplayName.Contains("virtual network adapter", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (candidate.DisplayName.Contains("App Recovery", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            string full = Path.GetFullPath(candidate.ExecutablePath);
            if (full.Contains(@"\OpenVPN\bin\", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        catch (Exception)
        {
        }

        return false;
    }

    internal static bool IsInternalApplicationId(string applicationId)
    {
        ReadOnlySpan<string> markers =
        [
            "CommandRunner",
            "BackgroundTask",
            "BackgroundWorker",
            "PreInstall",
            "UpdateTask",
        ];

        foreach (string marker in markers)
        {
            if (applicationId.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool IsInternalExecutableName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return true;
        }

        ReadOnlySpan<string> names =
        [
            "tapctl.exe",
            "chrome_proxy.exe",
            "codex-command-runner.exe",
        ];

        foreach (string name in names)
        {
            if (fileName.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
