namespace SelectiveVpnRouter.Core;

/// <summary>
/// Stable ProgramData probe slots for multi-app routing diagnostic (distinct ALE_APP_ID paths).
/// </summary>
public static class MultiAppRoutingProbePaths
{
    public const string TestRootFolderName = "VPN Route Tests";
    public const string SlotA = "AppA";
    public const string SlotB = "AppB";
    public const string SlotC = "AppC";

    public static string TestRootDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), TestRootFolderName);

    public static string SlotDirectory(string slot) => Path.Combine(TestRootDirectory, slot);

    public static bool IsUnderProbeRoot(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        try
        {
            string full = Path.GetFullPath(executablePath.Trim());
            string root = TestRootDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
