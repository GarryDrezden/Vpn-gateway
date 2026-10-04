namespace SelectiveVpnRouter.Core.ApplicationDiscovery;

public static class ApplicationDiscoveryMergeIdentity
{
    public static bool TryGetMergeKey(DiscoveredApplicationCandidate candidate, out string mergeKey)
    {
        mergeKey = string.Empty;
        PackagedApplicationIdentity? identity = candidate.PackageIdentity;
        if (!string.IsNullOrWhiteSpace(identity?.PackageFamilyName)
            && !string.IsNullOrWhiteSpace(identity.ApplicationId))
        {
            mergeKey = FormattableString.Invariant(
                $"msix:{identity.PackageFamilyName}:{identity.ApplicationId}");
            return true;
        }

        return ApplicationDiscoveryMerger.TryNormalizeIdentity(candidate.ExecutablePath, out mergeKey);
    }
}
