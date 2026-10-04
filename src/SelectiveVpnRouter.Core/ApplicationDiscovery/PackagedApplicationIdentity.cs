namespace SelectiveVpnRouter.Core.ApplicationDiscovery;

public sealed record PackagedApplicationIdentity
{
    public string? PackageFamilyName { get; init; }
    public string? PackageFullName { get; init; }
    public string? ApplicationId { get; init; }
    public string? AppUserModelId { get; init; }
    public string? RelativeExecutablePath { get; init; }

    /// <summary>
    /// V1 routing still persists resolved exe paths; package identity is retained for future auto-rebind after Store updates.
    /// </summary>
    public bool HasStablePackageIdentity =>
        !string.IsNullOrWhiteSpace(PackageFamilyName)
        && !string.IsNullOrWhiteSpace(ApplicationId);

    public string? BuildAppUserModelId()
    {
        if (!string.IsNullOrWhiteSpace(AppUserModelId))
        {
            return AppUserModelId;
        }

        if (string.IsNullOrWhiteSpace(PackageFamilyName) || string.IsNullOrWhiteSpace(ApplicationId))
        {
            return null;
        }

        return PackageFamilyName + "!" + ApplicationId;
    }
}
