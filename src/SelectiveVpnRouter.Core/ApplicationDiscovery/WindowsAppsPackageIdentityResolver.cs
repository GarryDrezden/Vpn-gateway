using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SelectiveVpnRouter.Core.ApplicationDiscovery;

internal static class WindowsAppsPackageIdentityResolver
{
    public static PackagedApplicationIdentity? TryResolveFromExecutable(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return null;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(executablePath);
        }
        catch (Exception)
        {
            return null;
        }

        if (!fullPath.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string? installRoot = TryGetPackageInstallRoot(fullPath);
        if (installRoot is null)
        {
            return null;
        }

        string manifestPath = Path.Combine(installRoot, "AppxManifest.xml");
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        string relativeExecutable;
        try
        {
            relativeExecutable = Path.GetRelativePath(installRoot, fullPath).Replace('\\', '/');
        }
        catch (Exception)
        {
            return null;
        }

        XDocument manifest;
        try
        {
            manifest = XDocument.Load(manifestPath);
        }
        catch (Exception)
        {
            return null;
        }

        XNamespace ns = manifest.Root?.Name.Namespace ?? XNamespace.None;
        foreach (XElement appElement in manifest.Descendants().Where(e => e.Name.LocalName == "Application"))
        {
            string? manifestExe = appElement.Attribute("Executable")?.Value;
            if (string.IsNullOrWhiteSpace(manifestExe))
            {
                continue;
            }

            if (!string.Equals(manifestExe, relativeExecutable, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string? applicationId = appElement.Attribute("Id")?.Value;
            if (string.IsNullOrWhiteSpace(applicationId))
            {
                continue;
            }

            if (!TryParsePackageFolderName(Path.GetFileName(installRoot), out string? familyName, out string? fullName))
            {
                return null;
            }

            return new PackagedApplicationIdentity
            {
                PackageFamilyName = familyName,
                PackageFullName = fullName,
                ApplicationId = applicationId,
                AppUserModelId = familyName + "!" + applicationId,
                RelativeExecutablePath = relativeExecutable.Replace('/', Path.DirectorySeparatorChar),
            };
        }

        return null;
    }

    private static readonly Regex PackageFolderPattern = new(
        @"^(.+)_(\d+(?:\.\d+)+)_([^_]+)__(.+)$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool TryParsePackageFolderName(string folderName, out string? packageFamilyName, out string? packageFullName)
    {
        packageFamilyName = null;
        packageFullName = null;
        if (string.IsNullOrWhiteSpace(folderName))
        {
            return false;
        }

        Match match = PackageFolderPattern.Match(folderName);
        if (!match.Success)
        {
            return false;
        }

        string packageName = match.Groups[1].Value;
        string publisherHash = match.Groups[4].Value;
        packageFullName = folderName;
        packageFamilyName = packageName + "_" + publisherHash;
        return !string.IsNullOrWhiteSpace(packageFamilyName);
    }

    private static string? TryGetPackageInstallRoot(string executablePath)
    {
        string? directory = Path.GetDirectoryName(executablePath);
        while (!string.IsNullOrWhiteSpace(directory))
        {
            if (Directory.Exists(Path.Combine(directory, "AppxManifest.xml"))
                && TryParsePackageFolderName(Path.GetFileName(directory), out _, out _))
            {
                return directory;
            }

            string? parent = Path.GetDirectoryName(directory);
            if (string.Equals(parent, directory, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            directory = parent;
        }

        return null;
    }
}
