using SelectiveVpnRouter.Core.ApplicationDiscovery;

namespace SelectiveVpnRouter.Core.ApplicationDiscovery.Windows;

internal static class StartMenuApplicationDiscovery
{
    public static IEnumerable<DiscoveredApplicationCandidate> Collect(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            yield break;
        }

        foreach (string root in GetStartMenuRoots())
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (string shortcut in Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string? target = ShortcutTargetResolver.TryResolveExecutable(shortcut);
                if (string.IsNullOrWhiteSpace(target))
                {
                    continue;
                }

                string displayName = Path.GetFileNameWithoutExtension(shortcut);
                PackagedApplicationIdentity? packageIdentity = WindowsAppsPackageIdentityResolver.TryResolveFromExecutable(target);
                var candidate = new DiscoveredApplicationCandidate
                {
                    ExecutablePath = target,
                    DisplayName = displayName,
                    IconPath = shortcut,
                    Source = DiscoverySource.StartMenu,
                    PackageIdentity = packageIdentity,
                };

                candidate = candidate with
                {
                    LaunchConfidence = ApplicationDiscoveryInstalledHeuristics.Assess(candidate),
                };

                yield return candidate;
            }
        }
    }

    private static IEnumerable<string> GetStartMenuRoots()
    {
        string? appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrWhiteSpace(appData))
        {
            yield return Path.Combine(appData, @"Microsoft\Windows\Start Menu\Programs");
        }

        string? commonPrograms = Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);
        if (!string.IsNullOrWhiteSpace(commonPrograms))
        {
            yield return commonPrograms;
        }
    }
}
