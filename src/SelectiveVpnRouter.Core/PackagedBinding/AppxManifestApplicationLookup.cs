using System.Xml.Linq;

namespace SelectiveVpnRouter.Core;

public sealed record PackagedManifestApplicationEntry(
    string ApplicationId,
    string RelativeExecutablePath,
    string? AppListEntry);

internal static class AppxManifestApplicationLookup
{
    public static IReadOnlyList<PackagedManifestApplicationEntry> ListApplications(string installRoot)
    {
        if (string.IsNullOrWhiteSpace(installRoot))
        {
            return [];
        }

        string manifestPath = Path.Combine(installRoot, "AppxManifest.xml");
        if (!File.Exists(manifestPath))
        {
            return [];
        }

        XDocument manifest;
        try
        {
            manifest = XDocument.Load(manifestPath);
        }
        catch (Exception)
        {
            return [];
        }

        List<PackagedManifestApplicationEntry> entries = [];
        foreach (XElement appElement in manifest.Descendants().Where(e => e.Name.LocalName == "Application"))
        {
            string? id = appElement.Attribute("Id")?.Value;
            string? executable = appElement.Attribute("Executable")?.Value;
            if (string.IsNullOrWhiteSpace(id) || !PackagedInstallPathSecurity.IsSafeRelativeExecutable(executable))
            {
                continue;
            }

            string? appListEntry = appElement.Attribute("AppListEntry")?.Value
                ?? appElement.Attribute(XName.Get("AppListEntry", "http://schemas.microsoft.com/appx/manifest/uap/windows10"))?.Value;

            entries.Add(new PackagedManifestApplicationEntry(
                id,
                executable!.Replace('/', Path.DirectorySeparatorChar),
                appListEntry));
        }

        return entries;
    }

    public static bool TryGetExecutableRelativePath(string installRoot, string applicationId, out string relativeExecutablePath)
    {
        relativeExecutablePath = string.Empty;
        if (string.IsNullOrWhiteSpace(installRoot) || string.IsNullOrWhiteSpace(applicationId))
        {
            return false;
        }

        string manifestPath = Path.Combine(installRoot, "AppxManifest.xml");
        if (!File.Exists(manifestPath))
        {
            return false;
        }

        XDocument manifest;
        try
        {
            manifest = XDocument.Load(manifestPath);
        }
        catch (Exception)
        {
            return false;
        }

        foreach (XElement appElement in manifest.Descendants().Where(e => e.Name.LocalName == "Application"))
        {
            string? id = appElement.Attribute("Id")?.Value;
            if (!string.Equals(id, applicationId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string? executable = appElement.Attribute("Executable")?.Value;
            if (!PackagedInstallPathSecurity.IsSafeRelativeExecutable(executable))
            {
                return false;
            }

            relativeExecutablePath = executable!.Replace('/', Path.DirectorySeparatorChar);
            return true;
        }

        return false;
    }
}
