namespace SelectiveVpnRouter.Core.ApplicationDiscovery;

public static class PackagedAssetResolver
{
    public static string? ResolveExistingAssetPath(string installRoot, string? relativeAssetPath)
    {
        if (string.IsNullOrWhiteSpace(installRoot) || string.IsNullOrWhiteSpace(relativeAssetPath))
        {
            return null;
        }

        string normalizedRelative = relativeAssetPath.Replace('/', Path.DirectorySeparatorChar);
        string baseFull = Path.GetFullPath(Path.Combine(installRoot, normalizedRelative));
        if (File.Exists(baseFull))
        {
            return baseFull;
        }

        string? directory = Path.GetDirectoryName(baseFull);
        string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(baseFull);
        string extension = Path.GetExtension(baseFull);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileNameWithoutExtension))
        {
            return null;
        }

        ReadOnlySpan<string> qualifiers =
        [
            ".scale-200",
            ".scale-100",
            ".targetsize-32",
            ".targetsize-44",
            ".targetsize-48",
            ".targetsize-24",
            ".targetsize-16",
        ];

        foreach (string qualifier in qualifiers)
        {
            string candidate = Path.Combine(directory, fileNameWithoutExtension + qualifier + extension);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
