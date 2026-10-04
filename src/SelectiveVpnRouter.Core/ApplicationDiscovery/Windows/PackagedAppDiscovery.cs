using System.Xml.Linq;
using SelectiveVpnRouter.Core.ApplicationDiscovery;
using Windows.ApplicationModel;
using Windows.Management.Deployment;

namespace SelectiveVpnRouter.Core.ApplicationDiscovery.Windows;

internal static class PackagedAppDiscovery
{
    private static readonly XNamespace Uap = "http://schemas.microsoft.com/appx/manifest/uap/windows10";

    public static IEnumerable<DiscoveredApplicationCandidate> Collect(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            yield break;
        }

        PackageManager manager = new();
        IReadOnlyList<Package> packages;
        try
        {
            packages = manager.FindPackagesForUser(string.Empty).ToList();
        }
        catch (Exception)
        {
            yield break;
        }

        foreach (Package package in packages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (DiscoveredApplicationCandidate candidate in EnumeratePackageCandidates(package))
            {
                yield return candidate;
            }
        }
    }

    private static IEnumerable<DiscoveredApplicationCandidate> EnumeratePackageCandidates(Package package)
    {
        PackageId id;
        string installRoot;
        try
        {
            id = package.Id;
            installRoot = package.InstalledLocation.Path;
        }
        catch (Exception)
        {
            yield break;
        }

        if (string.IsNullOrWhiteSpace(installRoot) || !Directory.Exists(installRoot))
        {
            yield break;
        }

        if (SafeBool(() => package.IsFramework) || SafeBool(() => package.IsResourcePackage))
        {
            yield break;
        }

        string manifestPath = Path.Combine(installRoot, "AppxManifest.xml");
        if (!File.Exists(manifestPath))
        {
            yield break;
        }

        XDocument manifest;
        try
        {
            manifest = XDocument.Load(manifestPath);
        }
        catch (Exception)
        {
            yield break;
        }

        XNamespace ns = manifest.Root?.Name.Namespace ?? XNamespace.None;
        string packageDisplayName = ReadPackageDisplayName(manifest, ns, package);
        string publisher = SafeString(() => package.PublisherDisplayName) ?? id.Publisher ?? string.Empty;
        bool isSystemPackage = PackagedApplicationHeuristics.IsSystemPackageInstallRoot(installRoot);

        foreach (XElement appElement in manifest.Descendants().Where(e => e.Name.LocalName == "Application"))
        {
            string? relativeExecutable = appElement.Attribute("Executable")?.Value;
            if (string.IsNullOrWhiteSpace(relativeExecutable))
            {
                continue;
            }

            string applicationId = appElement.Attribute("Id")?.Value ?? string.Empty;
            string? appListEntry = appElement.Attribute(Uap + "AppListEntry")?.Value
                ?? appElement.Attribute("AppListEntry")?.Value;

            if (!PackagedApplicationHeuristics.IsUserFacingApplicationEntry(applicationId, appListEntry, relativeExecutable))
            {
                continue;
            }

            string executablePath = Path.GetFullPath(Path.Combine(installRoot, relativeExecutable.Replace('/', Path.DirectorySeparatorChar)));
            if (!File.Exists(executablePath))
            {
                continue;
            }

            string appDisplayName = ReadApplicationDisplayName(appElement) ?? packageDisplayName;
            if (string.IsNullOrWhiteSpace(appDisplayName))
            {
                appDisplayName = Path.GetFileNameWithoutExtension(executablePath);
            }

            string? logoPath = TryResolvePackageLogo(installRoot, appElement, package);
            ApplicationDiscoveryLaunchConfidence confidence = isSystemPackage
                ? ApplicationDiscoveryLaunchConfidence.Low
                : ApplicationDiscoveryLaunchConfidence.High;

            yield return new DiscoveredApplicationCandidate
            {
                ExecutablePath = executablePath,
                DisplayName = appDisplayName,
                Publisher = publisher,
                IconPath = logoPath,
                Source = DiscoverySource.PackagedApp,
                LaunchConfidence = confidence,
                InstallLocation = installRoot,
                IsSystemComponent = isSystemPackage,
                PackageIdentity = new PackagedApplicationIdentity
                {
                    PackageFamilyName = id.FamilyName,
                    PackageFullName = id.FullName,
                    ApplicationId = applicationId,
                    AppUserModelId = id.FamilyName + "!" + applicationId,
                    RelativeExecutablePath = relativeExecutable.Replace('/', Path.DirectorySeparatorChar),
                },
            };
        }
    }

    private static string? ReadPackageDisplayName(XDocument manifest, XNamespace ns, Package package)
    {
        string? fromManifest = manifest.Descendants(ns + "DisplayName").Select(x => x.Value?.Trim()).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        if (!string.IsNullOrWhiteSpace(fromManifest) && !fromManifest.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
        {
            return fromManifest;
        }

        return SafeString(() => package.DisplayName);
    }

    private static string? ReadApplicationDisplayName(XElement appElement)
    {
        XElement? visualElements = appElement.Elements().FirstOrDefault(e => e.Name.LocalName == "VisualElements")
            ?? appElement.Descendants().FirstOrDefault(e => e.Name.LocalName == "VisualElements");

        string? value = visualElements?.Attribute("DisplayName")?.Value?.Trim();
        if (!string.IsNullOrWhiteSpace(value) && !value.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        return null;
    }

    private static string? TryResolvePackageLogo(string installRoot, XElement appElement, Package package)
    {
        XElement? visualElements = appElement.Elements().FirstOrDefault(e => e.Name.LocalName == "VisualElements")
            ?? appElement.Descendants().FirstOrDefault(e => e.Name.LocalName == "VisualElements");

        string? logoRelative = visualElements?.Attribute("Square44x44Logo")?.Value
            ?? visualElements?.Attribute("Square150x150Logo")?.Value;

        if (!string.IsNullOrWhiteSpace(logoRelative))
        {
            string? resolved = PackagedAssetResolver.ResolveExistingAssetPath(installRoot, logoRelative);
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                return resolved;
            }
        }

        try
        {
            string logo = package.Logo.LocalPath;
            if (File.Exists(logo))
            {
                return logo;
            }

            if (logo.StartsWith(installRoot, StringComparison.OrdinalIgnoreCase))
            {
                string relative = logo[installRoot.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return PackagedAssetResolver.ResolveExistingAssetPath(installRoot, relative);
            }

            return null;
        }
        catch (Exception)
        {
            return null;
        }

        return null;
    }

    private static string? SafeString(Func<string> read)
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool SafeBool(Func<bool> read)
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            return false;
        }
    }
}
