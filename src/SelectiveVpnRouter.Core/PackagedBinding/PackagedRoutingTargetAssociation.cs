using SelectiveVpnRouter.Core.ApplicationDiscovery;

namespace SelectiveVpnRouter.Core;

public static class PackagedRoutingTargetAssociation
{
    /// <summary>
    /// Hidden manifest applications that should share the same route as the user-facing primary application.
    /// V1: single visible app in package 뿯↽ all non-user-facing manifest entries; multi-visible 뿯↽ id/exe stem match only.
    /// </summary>
    public static IReadOnlyList<PackagedManifestApplicationEntry> SelectAssociatedRoutingHelpers(
        IReadOnlyList<PackagedManifestApplicationEntry> manifestApplications,
        string primaryApplicationId,
        string primaryRelativeExecutablePath)
    {
        if (manifestApplications.Count == 0 || string.IsNullOrWhiteSpace(primaryApplicationId))
        {
            return [];
        }

        List<PackagedManifestApplicationEntry> visible = manifestApplications
            .Where(e => PackagedApplicationHeuristics.IsUserFacingApplicationEntry(
                e.ApplicationId,
                e.AppListEntry,
                e.RelativeExecutablePath))
            .ToList();

        List<PackagedManifestApplicationEntry> hidden = manifestApplications
            .Where(e => !PackagedApplicationHeuristics.IsUserFacingApplicationEntry(
                e.ApplicationId,
                e.AppListEntry,
                e.RelativeExecutablePath))
            .ToList();

        if (hidden.Count == 0)
        {
            return [];
        }

        if (visible.Count == 1
            && string.Equals(visible[0].ApplicationId, primaryApplicationId, StringComparison.OrdinalIgnoreCase))
        {
            return hidden;
        }

        return hidden
            .Where(h => AssociatesWithPrimaryInMultiVisiblePackage(h, primaryApplicationId, primaryRelativeExecutablePath, visible))
            .ToList();
    }

    private static bool AssociatesWithPrimaryInMultiVisiblePackage(
        PackagedManifestApplicationEntry helper,
        string primaryApplicationId,
        string primaryRelativeExecutablePath,
        IReadOnlyList<PackagedManifestApplicationEntry> visibleApplications)
    {
        foreach (PackagedManifestApplicationEntry other in visibleApplications)
        {
            if (string.Equals(other.ApplicationId, primaryApplicationId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (helper.ApplicationId.Contains(other.ApplicationId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (helper.ApplicationId.Contains(primaryApplicationId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string primaryStem = Path.GetFileNameWithoutExtension(primaryRelativeExecutablePath);
        if (!string.IsNullOrWhiteSpace(primaryStem)
            && helper.ApplicationId.Contains(primaryStem, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }
}
