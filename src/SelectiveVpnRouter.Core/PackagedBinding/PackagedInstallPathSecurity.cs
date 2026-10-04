namespace SelectiveVpnRouter.Core;

internal static class PackagedInstallPathSecurity
{
    public static bool IsExecutableWithinInstallRoot(string installRoot, string executablePath)
    {
        if (string.IsNullOrWhiteSpace(installRoot) || string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        try
        {
            string normalizedRoot = Path.GetFullPath(installRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                + Path.DirectorySeparatorChar;
            string normalizedExe = Path.GetFullPath(executablePath);
            return normalizedExe.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static bool IsSafeRelativeExecutable(string? relativeExecutable)
    {
        if (string.IsNullOrWhiteSpace(relativeExecutable))
        {
            return false;
        }

        if (relativeExecutable.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        return !Path.IsPathRooted(relativeExecutable);
    }
}
