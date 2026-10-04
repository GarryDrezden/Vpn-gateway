namespace SelectiveVpnRouter.Core;

public sealed class FakePackagedApplicationPathResolver : IPackagedApplicationPathResolver
{
    private readonly Dictionary<string, FakeInstalledPackage> _packages = new(StringComparer.OrdinalIgnoreCase);

    public FakePackagedApplicationPathResolver AddPackage(FakeInstalledPackage package)
    {
        string key = FormattableString.Invariant($"{package.UserSid}|{package.PackageFamilyName}");
        _packages[key] = package;
        return this;
    }

    public PackagedApplicationPathResolveResult Resolve(PackagedApplicationBinding binding)
    {
        if (!PackagedApplicationRuleIdentity.HasStableBinding(binding))
        {
            return PackagedApplicationPathResolveResult.NotFound("Invalid binding.");
        }

        string key = FormattableString.Invariant($"{binding.UserSid}|{binding.PackageFamilyName}");
        if (!_packages.TryGetValue(key, out FakeInstalledPackage? package))
        {
            return PackagedApplicationPathResolveResult.NotFound("Package not installed.");
        }

        if (!package.Applications.TryGetValue(binding.ApplicationId, out string? relativeExecutable))
        {
            return PackagedApplicationPathResolveResult.NotFound("ApplicationId not found.");
        }

        if (!PackagedInstallPathSecurity.IsSafeRelativeExecutable(relativeExecutable))
        {
            return PackagedApplicationPathResolveResult.NotFound("Unsafe relative executable.");
        }

        string executablePath = Path.GetFullPath(Path.Combine(package.InstallRoot, relativeExecutable));
        if (!PackagedInstallPathSecurity.IsExecutableWithinInstallRoot(package.InstallRoot, executablePath))
        {
            return PackagedApplicationPathResolveResult.NotFound("Resolved path outside install root.");
        }

        if (!File.Exists(executablePath))
        {
            return PackagedApplicationPathResolveResult.NotFound("Executable missing.");
        }

        return PackagedApplicationPathResolveResult.Success(
            executablePath,
            relativeExecutable.Replace('\\', '/'),
            package.PackageFullName,
            package.InstallRoot);
    }
}

public sealed record FakeInstalledPackage
{
    public required string UserSid { get; init; }
    public required string PackageFamilyName { get; init; }
    public required string PackageFullName { get; init; }
    public required string InstallRoot { get; init; }
    public required Dictionary<string, string> Applications { get; init; }
}
