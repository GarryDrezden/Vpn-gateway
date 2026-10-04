using Microsoft.Win32;
using SelectiveVpnRouter.Core.ApplicationDiscovery;

namespace SelectiveVpnRouter.Core.ApplicationDiscovery.Windows;

internal static class AppPathsRegistryDiscovery
{
    private const string AppPathsKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths";

    public static IEnumerable<DiscoveredApplicationCandidate> Collect(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            yield break;
        }

        foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using RegistryKey? appPaths = baseKey.OpenSubKey(AppPathsKey);
            if (appPaths is null)
            {
                continue;
            }

            foreach (string subKeyName in appPaths.GetSubKeyNames())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!subKeyName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                using RegistryKey? entry = appPaths.OpenSubKey(subKeyName);
                string? path = entry?.GetValue(null) as string;
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    continue;
                }

                yield return new DiscoveredApplicationCandidate
                {
                    ExecutablePath = path,
                    DisplayName = Path.GetFileNameWithoutExtension(path),
                    IconPath = path,
                    Source = DiscoverySource.AppPaths,
                    LaunchConfidence = ApplicationDiscoveryLaunchConfidence.High,
                };
            }
        }
    }
}