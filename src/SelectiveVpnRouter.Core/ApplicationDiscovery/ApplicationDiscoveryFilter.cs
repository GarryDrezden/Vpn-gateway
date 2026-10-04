namespace SelectiveVpnRouter.Core.ApplicationDiscovery;

public static class ApplicationDiscoveryFilter
{
    private static readonly HashSet<string> BlockedExecutables = new(StringComparer.OrdinalIgnoreCase)
    {
        "cmd.exe",
        "conhost.exe",
        "dllhost.exe",
        "explorer.exe",
        "fontdrvhost.exe",
        "lsass.exe",
        "msiexec.exe",
        "rundll32.exe",
        "RuntimeBroker.exe",
        "SearchHost.exe",
        "ShellExperienceHost.exe",
        "sihost.exe",
        "smartscreen.exe",
        "svchost.exe",
        "taskhostw.exe",
        "WerFault.exe",
        "WmiPrvSE.exe",
    };

    public static IReadOnlyList<DiscoveredApplication> FilterInstalled(
        IEnumerable<DiscoveredApplication> applications,
        ApplicationDiscoveryViewOptions? options = null)
    {
        options ??= ApplicationDiscoveryViewOptions.Default;
        return applications
            .Where(app => ApplicationDiscoveryInstalledHeuristics.IsVisibleByDefault(app, options.ShowSystemAndServiceEntries))
            .ToList();
    }

    public static IReadOnlyList<DiscoveredApplication> FilterRunning(
        IEnumerable<DiscoveredApplication> applications,
        ApplicationDiscoveryViewOptions? options = null)
    {
        options ??= ApplicationDiscoveryViewOptions.Default;
        return applications
            .Where(a => a.IsRunning)
            .Where(a => a.IsAlreadyConfigured || IsDefaultRunningEntry(a, options.ShowBackgroundProcesses))
            .ToList();
    }

    public static bool IsDefaultRunningEntry(DiscoveredApplication app, bool includeBackgroundProcesses)
    {
        if (!IsUsableExecutablePath(app.ExecutablePath, requireExistingFile: true))
        {
            return false;
        }

        if (BlockedExecutables.Contains(app.ExecutableFileName))
        {
            return false;
        }

        if (IsUnderWindowsDirectory(app.ExecutablePath))
        {
            return false;
        }

        if (includeBackgroundProcesses)
        {
            return app.Sources.HasFlag(DiscoverySource.RunningProcess)
                || app.Sources.HasFlag(DiscoverySource.InteractiveWindow);
        }

        return app.Sources.HasFlag(DiscoverySource.InteractiveWindow);
    }

    internal static bool IsUsableExecutablePath(string path, bool requireExistingFile)
    {
        if (!ApplicationDiscoveryMerger.TryNormalizeIdentity(path, out _))
        {
            return false;
        }

        if (requireExistingFile && !File.Exists(path))
        {
            return false;
        }

        return true;
    }

    internal static bool IsUnderWindowsDirectory(string fullPath)
    {
        try
        {
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (string.IsNullOrWhiteSpace(windows))
            {
                return false;
            }

            string normalizedWindows = Path.GetFullPath(windows)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedPath = Path.GetFullPath(fullPath);
            return normalizedPath.StartsWith(normalizedWindows + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalizedPath, normalizedWindows, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }
}