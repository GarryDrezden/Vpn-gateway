namespace SelectiveVpnRouter.Core.Portable;

public static class PortableRuntimeLayoutValidator
{
    public static IReadOnlyList<string> RequiredDevPublishRelativePaths { get; } =
    [
        PortableLayout.AppExeName,
        PortableLayout.ServiceExeName,
        PortableLayout.BootstrapExeName,
        PortableLayout.ProbeExeName,
        PortableLayout.ManifestFileName,
        Path.Combine(PortableLayout.DriverSubfolder, PortableLayout.DriverSysName),
        Path.Combine(PortableLayout.DriverSubfolder, PortableLayout.DriverInfName),
    ];

    public static bool TryValidate(string directory, out IReadOnlyList<string> missingRelativePaths)
    {
        missingRelativePaths = Array.Empty<string>();
        if (string.IsNullOrWhiteSpace(directory))
        {
            missingRelativePaths = RequiredDevPublishRelativePaths;
            return false;
        }

        string root = Path.GetFullPath(directory);
        if (!Directory.Exists(root))
        {
            missingRelativePaths = RequiredDevPublishRelativePaths;
            return false;
        }

        List<string> missing = new();
        foreach (string relative in RequiredDevPublishRelativePaths)
        {
            string full = Path.Combine(root, relative);
            if (!File.Exists(full))
            {
                missing.Add(relative);
            }
        }

        missingRelativePaths = missing;
        return missing.Count == 0;
    }
}