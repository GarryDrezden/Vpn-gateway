using Microsoft.Win32;
using SelectiveVpnRouter.Core.ApplicationDiscovery;

namespace SelectiveVpnRouter.Core.ApplicationDiscovery.Windows;

internal static class UninstallRegistryDiscovery
{
    private static readonly string[] UninstallRoots =
    [
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
    ];

    public static IEnumerable<DiscoveredApplicationCandidate> Collect(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            yield break;
        }

        foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            foreach (string rootPath in UninstallRoots)
            {
                foreach (DiscoveredApplicationCandidate candidate in ReadRoot(RegistryHive.LocalMachine, view, rootPath, cancellationToken))
                {
                    yield return candidate;
                }

                foreach (DiscoveredApplicationCandidate candidate in ReadRoot(RegistryHive.CurrentUser, view, rootPath, cancellationToken))
                {
                    yield return candidate;
                }
            }
        }
    }

    private static IEnumerable<DiscoveredApplicationCandidate> ReadRoot(
        RegistryHive hive,
        RegistryView view,
        string rootPath,
        CancellationToken cancellationToken)
    {
        using RegistryKey? baseKey = RegistryKey.OpenBaseKey(hive, view);
        using RegistryKey? uninstall = baseKey.OpenSubKey(rootPath);
        if (uninstall is null)
        {
            yield break;
        }

        foreach (string subKeyName in uninstall.GetSubKeyNames())
        {
            cancellationToken.ThrowIfCancellationRequested();
            using RegistryKey? entry = uninstall.OpenSubKey(subKeyName);
            if (entry is null)
            {
                continue;
            }

            DiscoveredApplicationCandidate? candidate = TryReadEntry(entry);
            if (candidate is not null)
            {
                yield return candidate;
            }
        }
    }

    private static DiscoveredApplicationCandidate? TryReadEntry(RegistryKey entry)
    {
        string? displayName = entry.GetValue("DisplayName") as string;
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return null;
        }

        bool systemComponent = ReadBool(entry, "SystemComponent");
        string? publisher = entry.GetValue("Publisher") as string;
        string? installLocation = entry.GetValue("InstallLocation") as string;
        string? exePath = TryResolveExecutable(entry, installLocation);
        if (string.IsNullOrWhiteSpace(exePath))
        {
            return null;
        }

        var candidate = new DiscoveredApplicationCandidate
        {
            ExecutablePath = exePath,
            DisplayName = displayName.Trim(),
            Publisher = string.IsNullOrWhiteSpace(publisher) ? null : publisher.Trim(),
            IconPath = exePath,
            Source = DiscoverySource.Uninstall,
            InstallLocation = string.IsNullOrWhiteSpace(installLocation) ? null : installLocation.Trim(),
            IsSystemComponent = systemComponent,
            LaunchConfidence = ApplicationDiscoveryInstalledHeuristics.Assess(new DiscoveredApplicationCandidate
            {
                ExecutablePath = exePath,
                DisplayName = displayName,
                Publisher = publisher,
                Source = DiscoverySource.Uninstall,
                InstallLocation = installLocation,
                IsSystemComponent = systemComponent,
            }),
        };

        return candidate;
    }

    private static string? TryResolveExecutable(RegistryKey entry, string? installLocation)
    {
        string? fromDisplayIcon = ExtractExecutableFromDisplayIcon(entry.GetValue("DisplayIcon") as string);
        if (!string.IsNullOrWhiteSpace(fromDisplayIcon))
        {
            return fromDisplayIcon;
        }

        if (!string.IsNullOrWhiteSpace(installLocation) && Directory.Exists(installLocation))
        {
            string? single = TryFindSingleTopLevelExecutable(installLocation);
            if (!string.IsNullOrWhiteSpace(single))
            {
                return single;
            }
        }

        return null;
    }

    private static string? ExtractExecutableFromDisplayIcon(string? displayIcon)
    {
        if (string.IsNullOrWhiteSpace(displayIcon))
        {
            return null;
        }

        string trimmed = displayIcon.Trim().Trim('"');
        int comma = trimmed.IndexOf(',');
        if (comma >= 0)
        {
            trimmed = trimmed[..comma].Trim().Trim('"');
        }

        if (!trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return File.Exists(trimmed) ? trimmed : null;
    }

    private static string? TryFindSingleTopLevelExecutable(string installLocation)
    {
        try
        {
            string[] exeFiles = Directory.GetFiles(installLocation, "*.exe", SearchOption.TopDirectoryOnly);
            if (exeFiles.Length != 1)
            {
                return null;
            }

            return exeFiles[0];
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool ReadBool(RegistryKey entry, string valueName)
    {
        object? value = entry.GetValue(valueName);
        return value switch
        {
            int i => i != 0,
            string s => s == "1",
            _ => false,
        };
    }
}