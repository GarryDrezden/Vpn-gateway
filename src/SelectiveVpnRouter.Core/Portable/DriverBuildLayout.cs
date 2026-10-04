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