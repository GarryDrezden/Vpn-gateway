namespace SelectiveVpnRouter.Core;

public static class DiagnosticUserExecutablePaths
{
    public const string TelegramDesktopRelativePath = @"AppData\Roaming\Telegram Desktop\Telegram.exe";

    public static bool TryResolveTelegramExecutable(
        AppConfiguration config,
        string? explicitExePath,
        out string resolvedPath,
        out string resolutionSource)
    {
        resolvedPath = string.Empty;
        resolutionSource = string.Empty;

        if (TryNormalizeExistingFile(explicitExePath, out string explicitFull))
        {
            resolvedPath = explicitFull;
            resolutionSource = "explicit-exePath";
            return true;
        }

        foreach (string configured in CollectConfiguredExecutablePaths(config, "Telegram.exe"))
        {
            resolvedPath = configured;
            resolutionSource = "configured-vpn-app";
            return true;
        }

        if (TryDiscoverUnderUserProfiles(TelegramDesktopRelativePath, out string discovered))
        {
            resolvedPath = discovered;
            resolutionSource = "user-profile-discovery";
            return true;
        }

        return false;
    }

    public static IEnumerable<string> CollectConfiguredExecutablePaths(AppConfiguration config, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            yield break;
        }

        foreach (string path in VpnApplicationPathCollector.Collect(config.Rules, paused: false))
        {
            if (string.Equals(Path.GetFileName(path), fileName, StringComparison.OrdinalIgnoreCase)
                && File.Exists(path))
            {
                yield return path;
            }
        }

        foreach (RoutingRule rule in config.Rules)
        {
            if (rule.Type != RuleType.Application)
            {
                continue;
            }

            string target = rule.Target.Trim().Trim('"');
            if (!string.Equals(Path.GetFileName(target), fileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!TryNormalizeExistingFile(target, out string full))
            {
                continue;
            }

            yield return full;
        }
    }

    public static bool TryDiscoverUnderUserProfiles(string relativePathFromUserHome, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(relativePathFromUserHome))
        {
            return false;
        }

        string? drive = Environment.GetEnvironmentVariable("SystemDrive");
        if (string.IsNullOrWhiteSpace(drive))
        {
            drive = "C:";
        }

        string usersRoot = Path.Combine(drive, "Users");
        if (!Directory.Exists(usersRoot))
        {
            return false;
        }

        foreach (string userHome in Directory.EnumerateDirectories(usersRoot))
        {
            string profileName = Path.GetFileName(userHome);
            if (IsSkippedUserProfileDirectory(profileName))
            {
                continue;
            }

            string candidate = Path.Combine(userHome, relativePathFromUserHome);
            if (File.Exists(candidate))
            {
                fullPath = candidate;
                return true;
            }
        }

        return false;
    }

    public static bool IsSkippedUserProfileDirectory(string profileName)
        => profileName.Equals("Public", StringComparison.OrdinalIgnoreCase)
           || profileName.Equals("Default", StringComparison.OrdinalIgnoreCase)
           || profileName.Equals("Default User", StringComparison.OrdinalIgnoreCase)
           || profileName.Equals("All Users", StringComparison.OrdinalIgnoreCase)
           || profileName.Equals("defaultuser0", StringComparison.OrdinalIgnoreCase);

    private static bool TryNormalizeExistingFile(string? path, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            fullPath = Path.GetFullPath(path.Trim().Trim('"'));
            return File.Exists(fullPath);
        }
        catch
        {
            return false;
        }
    }
}