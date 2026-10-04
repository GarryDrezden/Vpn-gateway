namespace SelectiveVpnRouter.Core;

public sealed record PackagedApplicationPathResolveResult
{
    public bool Found { get; init; }
    public string? ResolvedExecutablePath { get; init; }
    public string? RelativeExecutablePath { get; init; }
    public string? PackageFullName { get; init; }
    public string? InstallRoot { get; init; }
    public string? Reason { get; init; }

    public static PackagedApplicationPathResolveResult Success(
        string executablePath,
        string relativeExecutablePath,
        string? packageFullName,
        string? installRoot = null) =>
        new()
        {
            Found = true,
            ResolvedExecutablePath = executablePath,
            RelativeExecutablePath = relativeExecutablePath,
            PackageFullName = packageFullName,
            InstallRoot = installRoot,
        };

    public static PackagedApplicationPathResolveResult NotFound(string reason) =>
        new() { Found = false, Reason = reason };
}
