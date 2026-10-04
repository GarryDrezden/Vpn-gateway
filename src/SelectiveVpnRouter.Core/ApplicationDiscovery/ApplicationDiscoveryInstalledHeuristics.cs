namespace SelectiveVpnRouter.Core.ApplicationDiscovery;

public static class ApplicationDiscoveryInstalledHeuristics
{
    public static ApplicationDiscoveryLaunchConfidence Assess(DiscoveredApplicationCandidate candidate)
    {
        if (candidate.IsSystemComponent)
        {
            return ApplicationDiscoveryLaunchConfidence.Low;
        }

        if (PackagedApplicationHeuristics.IsLowConfidenceStartMenuCandidate(candidate))
        {
            return ApplicationDiscoveryLaunchConfidence.Low;
        }

        if (PackagedApplicationHeuristics.IsLowConfidencePackagedCandidate(candidate))
        {
            return ApplicationDiscoveryLaunchConfidence.Low;
        }

        if (candidate.Source.HasFlag(DiscoverySource.StartMenu)
            || candidate.Source.HasFlag(DiscoverySource.AppPaths))
        {
            return ApplicationDiscoveryLaunchConfidence.High;
        }

        if (candidate.Source.HasFlag(DiscoverySource.PackagedApp)
            && ApplicationDiscoveryFilter.IsUsableExecutablePath(candidate.ExecutablePath, requireExistingFile: true))
        {
            return ApplicationDiscoveryLaunchConfidence.High;
        }

        if (candidate.Source.HasFlag(DiscoverySource.Uninstall))
        {
            if (IsLowConfidenceUninstall(candidate))
            {
                return ApplicationDiscoveryLaunchConfidence.Low;
            }

            return ApplicationDiscoveryLaunchConfidence.Medium;
        }

        return ApplicationDiscoveryLaunchConfidence.Low;
    }

    public static ApplicationDiscoveryLaunchConfidence AssessMerged(DiscoveredApplication application)
    {
        if (application.LaunchConfidence >= ApplicationDiscoveryLaunchConfidence.Medium)
        {
            return application.LaunchConfidence;
        }

        if (application.Sources.HasFlag(DiscoverySource.StartMenu)
            || application.Sources.HasFlag(DiscoverySource.AppPaths))
        {
            return ApplicationDiscoveryLaunchConfidence.High;
        }

        if (application.Sources.HasFlag(DiscoverySource.PackagedApp))
        {
            return application.LaunchConfidence >= ApplicationDiscoveryLaunchConfidence.Medium
                ? application.LaunchConfidence
                : ApplicationDiscoveryLaunchConfidence.Low;
        }

        return application.LaunchConfidence;
    }

    public static bool IsVisibleByDefault(DiscoveredApplication application, bool showSystemAndServiceEntries)
    {
        ApplicationDiscoveryLaunchConfidence confidence = AssessMerged(application);
        if (showSystemAndServiceEntries)
        {
            return confidence >= ApplicationDiscoveryLaunchConfidence.Low
                && ApplicationDiscoveryFilter.IsUsableExecutablePath(application.ExecutablePath, requireExistingFile: true);
        }

        return confidence >= ApplicationDiscoveryLaunchConfidence.Medium
            && !IsStructuralSystemRuntimeEntry(application);
    }

    internal static bool IsLowConfidenceUninstall(DiscoveredApplicationCandidate candidate)
    {
        if (candidate.Source.HasFlag(DiscoverySource.PackagedApp))
        {
            return false;
        }

        string fileName = Path.GetFileName(candidate.ExecutablePath);
        if (IsHelperOrRuntimeExecutableName(fileName))
        {
            return true;
        }

        if (ApplicationDiscoveryFilter.IsUnderWindowsDirectory(candidate.ExecutablePath))
        {
            return true;
        }

        if (IsLikelyRuntimeInstallLocation(candidate.InstallLocation))
        {
            return true;
        }

        if (IsLikelyRuntimeInstallLocation(Path.GetDirectoryName(candidate.ExecutablePath)))
        {
            return true;
        }

        return false;
    }

    private static bool IsStructuralSystemRuntimeEntry(DiscoveredApplication application)
    {
        if (application.LaunchConfidence <= ApplicationDiscoveryLaunchConfidence.Low)
        {
            return true;
        }

        if (application.Sources.HasFlag(DiscoverySource.PackagedApp)
            && application.PackageIdentity is not null
            && PackagedApplicationHeuristics.IsSystemPackageInstallRoot(
                Path.GetDirectoryName(application.ExecutablePath) ?? string.Empty))
        {
            return true;
        }

        if (IsHelperOrRuntimeExecutableName(application.ExecutableFileName))
        {
            return true;
        }

        if (ApplicationDiscoveryFilter.IsUnderWindowsDirectory(application.ExecutablePath))
        {
            return true;
        }

        if (IsLikelyRuntimeInstallLocation(Path.GetDirectoryName(application.ExecutablePath)))
        {
            if (application.Sources.HasFlag(DiscoverySource.PackagedApp)
                && application.LaunchConfidence >= ApplicationDiscoveryLaunchConfidence.Medium)
            {
                return false;
            }

            return true;
        }

        return false;
    }

    private static bool IsHelperOrRuntimeExecutableName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return true;
        }

        if (fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        ReadOnlySpan<string> suffixes =
        [
            "Helper.exe",
            "Updater.exe",
            "Update.exe",
            "Installer.exe",
            "Uninstall.exe",
            "Setup.exe",
            "Bootstrapper.exe",
            "Subprocess.exe",
            "CrashHandler.exe",
        ];

        foreach (string suffix in suffixes)
        {
            if (fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsLikelyRuntimeInstallLocation(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        string normalized = directory.Replace('/', '\\');
        ReadOnlySpan<string> markers =
        [
            "\\Windows\\",
            "\\WinSxS\\",
            "\\WindowsApps\\",
            "\\Microsoft\\EdgeUpdate\\",
            "\\Microsoft\\EdgeWebView\\",
            "\\Microsoft\\Edge\\Application\\",
            "\\Microsoft\\OneDrive\\",
            "\\Microsoft\\Windows Defender\\",
            "\\Package Cache\\",
            "\\Reference Assemblies\\",
            "\\MSBuild\\",
            "\\Windows Kits\\",
            "\\Driver Package\\",
            "\\DriverStore\\",
        ];

        foreach (string marker in markers)
        {
            if (normalized.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}