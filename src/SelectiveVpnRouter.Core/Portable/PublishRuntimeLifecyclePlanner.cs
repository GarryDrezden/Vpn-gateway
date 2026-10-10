namespace SelectiveVpnRouter.Core.Portable;

public static class PublishRuntimeLifecyclePlanner
{
    public static PublishRuntimeRestorePlan PlanRestore(PublishRuntimeSnapshot snapshot) =>
        new(
            StartDriver: snapshot.DriverInstalled && snapshot.DriverWasRunning,
            StartProduct: snapshot.ProductInstalled && snapshot.ProductWasRunning);

    public static bool ShouldStopDriverForPublishSwap(PublishRuntimeSnapshot snapshot, string publishRoot) =>
        snapshot.DriverInstalled
        && snapshot.DriverWasRunning
        && IsPathUnderPublishRoot(snapshot.DriverImagePath, publishRoot);

    public static bool ShouldStopDriverForRetiredCleanup(
        bool driverInstalled,
        bool driverRunning,
        string? driverImagePath,
        string retiredRoot) =>
        driverInstalled
        && driverRunning
        && IsPathUnderPublishRoot(driverImagePath, retiredRoot);

    public static bool ShouldParticipateInPublishSwap(string? driverImagePath, string publishRoot) =>
        IsPathUnderPublishRoot(driverImagePath, publishRoot);

    /// <summary>
    /// Repoint ephemeral build/staging registrations to the publish-tree runtime copy during install/update.
    /// </summary>
    public static bool ShouldRepairDriverImagePathToPublishLayout(PublishRuntimeSnapshot snapshot, string publishRoot) =>
        !snapshot.DriverInstalled
        || IsPathUnderPublishRoot(snapshot.DriverImagePath, publishRoot)
        || DriverBuildLayout.IsEphemeralDriverBuildPath(snapshot.DriverImagePath);

    internal static bool IsPathUnderPublishRoot(string? candidatePath, string publishRoot)
    {
        string? normalized = TryNormalizeDriverImagePath(candidatePath);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        string root;
        try
        {
            root = Path.GetFullPath(publishRoot.Trim());
        }
        catch (Exception)
        {
            return false;
        }

        string rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return normalized.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                normalized,
                root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
    }

    internal static string? TryNormalizeDriverImagePath(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return null;
        }

        string trimmed = imagePath.Trim().Trim('"');
        if (trimmed.StartsWith(@"\??\", StringComparison.Ordinal))
        {
            trimmed = trimmed[4..];
        }

        try
        {
            return Path.GetFullPath(trimmed);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
