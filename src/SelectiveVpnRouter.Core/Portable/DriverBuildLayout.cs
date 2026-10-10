namespace SelectiveVpnRouter.Core.Portable;

public static class DriverBuildLayout
{
    public const string StagingFolderName = "staging";

    public static string StagingConfigurationDirectory(string repoRoot, string configuration = "Release")
        => Path.Combine(repoRoot, "artifacts", "driver", StagingFolderName, configuration);

    public static string StagingSysPath(string repoRoot, string configuration = "Release")
        => Path.Combine(StagingConfigurationDirectory(repoRoot, configuration), PortableLayout.DriverSysName);

    public static string RuntimeSysPath(string publishRoot)
        => PortableLayout.ExpectedDriverSysPath(publishRoot);

    public static string LegacyReleaseBuildSysPath(string repoRoot)
        => Path.Combine(repoRoot, "artifacts", "driver", "Release", PortableLayout.DriverSysName);

    public static string RollbackSysPath(string repoRoot)
        => Path.Combine(repoRoot, "artifacts", "driver", "rollback", PortableLayout.DriverSysName);

    public static bool IsEphemeralDriverBuildPath(string? imagePath)
    {
        string? normalized = PublishRuntimeLifecyclePlanner.TryNormalizeDriverImagePath(imagePath);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        string full;
        try
        {
            full = Path.GetFullPath(normalized);
        }
        catch (Exception)
        {
            return false;
        }

        string[] segments = full.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < segments.Length - 2; i++)
        {
            if (!string.Equals(segments[i], "artifacts", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(segments[i + 1], "driver", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string bucket = segments[i + 2];
            if (string.Equals(bucket, StagingFolderName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(bucket, "Release", StringComparison.OrdinalIgnoreCase)
                || string.Equals(bucket, "Debug", StringComparison.OrdinalIgnoreCase)
                || string.Equals(bucket, "rollback", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static bool LinkerOutputMustNotTargetRegisteredImagePath(string linkerOutputPath, string? registeredImagePath)
    {
        if (string.IsNullOrWhiteSpace(registeredImagePath))
        {
            return true;
        }

        string linkerFull = Path.GetFullPath(linkerOutputPath);
        string? registeredFull = PublishRuntimeLifecyclePlanner.TryNormalizeDriverImagePath(registeredImagePath);
        if (registeredFull is null)
        {
            return true;
        }

        return !string.Equals(linkerFull, registeredFull, StringComparison.OrdinalIgnoreCase);
    }
}