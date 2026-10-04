using System.Text.RegularExpressions;
using Windows.ApplicationModel;
using Windows.Management.Deployment;

namespace SelectiveVpnRouter.Core;

public sealed class WindowsPackagedApplicationPathResolver : IPackagedApplicationPathResolver
{
    private static readonly Regex PackageVersionPattern = new(
        @"^.+_(\d+(?:\.\d+)+)_[^_]+__",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public PackagedApplicationPathResolveResult Resolve(PackagedApplicationBinding binding)
    {
        if (!PackagedApplicationRuleIdentity.HasStableBinding(binding))
        {
            return PackagedApplicationPathResolveResult.NotFound("Packaged binding is incomplete.");
        }

        if (!OperatingSystem.IsWindows())
        {
            return PackagedApplicationPathResolveResult.NotFound("Packaged apps are supported on Windows only.");
        }

        PackageManager manager = new();
        IReadOnlyList<Package> candidates;
        try
        {
            candidates = manager.FindPackagesForUser(binding.UserSid)
                .Where(p => string.Equals(TryGetFamilyName(p), binding.PackageFamilyName, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        catch (Exception ex)
        {
            return PackagedApplicationPathResolveResult.NotFound("Package query failed: " + ex.Message);
        }

        if (candidates.Count == 0)
        {
            return PackagedApplicationPathResolveResult.NotFound("Package is not installed for the configured user.");
        }

        foreach (Package package in OrderCandidates(candidates))
        {
            if (IsFrameworkOrResourcePackage(package))
            {
                continue;
            }

            string installRoot;
            PackageId packageId;
            try
            {
                installRoot = package.InstalledLocation.Path;
                packageId = package.Id;
            }
            catch (Exception)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(installRoot) || !Directory.Exists(installRoot))
            {
                continue;
            }

            if (!AppxManifestApplicationLookup.TryGetExecutableRelativePath(
                    installRoot,
                    binding.ApplicationId,
                    out string relativeExecutable))
            {
                continue;
            }

            string executablePath = Path.GetFullPath(Path.Combine(installRoot, relativeExecutable));
            if (!PackagedInstallPathSecurity.IsExecutableWithinInstallRoot(installRoot, executablePath))
            {
                return PackagedApplicationPathResolveResult.NotFound("Resolved executable is outside the package install location.");
            }

            if (!File.Exists(executablePath))
            {
                continue;
            }

            return PackagedApplicationPathResolveResult.Success(
                executablePath,
                relativeExecutable.Replace('\\', '/'),
                packageId.FullName,
                installRoot);
        }

        return PackagedApplicationPathResolveResult.NotFound("No installed package version exposes the configured application.");
    }

    private static IEnumerable<Package> OrderCandidates(IEnumerable<Package> packages) =>
        packages
            .OrderByDescending(TryGetPackageVersionScore)
            .ThenByDescending(p => TryGetFullName(p), StringComparer.OrdinalIgnoreCase);

    private static int TryGetPackageVersionScore(Package package)
    {
        string? fullName = TryGetFullName(package);
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return 0;
        }

        Match match = PackageVersionPattern.Match(fullName);
        if (!match.Success)
        {
            return 0;
        }

        int[] parts = new int[8];
        int count = 0;
        foreach (string segment in match.Groups[1].Value.Split('.'))
        {
            if (count >= parts.Length)
            {
                break;
            }

            if (int.TryParse(segment, out int value))
            {
                parts[count++] = value;
            }
        }

        int score = 0;
        for (int i = 0; i < count; i++)
        {
            score = score * 1000 + parts[i];
        }

        return score;
    }

    private static bool IsFrameworkOrResourcePackage(Package package)
    {
        try
        {
            return package.IsFramework || package.IsResourcePackage;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string? TryGetFamilyName(Package package)
    {
        try
        {
            return package.Id.FamilyName;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? TryGetFullName(Package package)
    {
        try
        {
            return package.Id.FullName;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
