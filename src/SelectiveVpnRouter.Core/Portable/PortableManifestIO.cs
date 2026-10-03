using System.Text.Json;

namespace SelectiveVpnRouter.Core.Portable;

public static class PortableManifestIO
{
    public static PortableManifestDocument? TryRead(string portableRoot)
    {
        string path = PortableLayout.ExpectedManifestPath(portableRoot);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<PortableManifestDocument>(json, ConfigSerializer.JsonOptions);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static PortablePackageStamp? TryReadInstalledStamp()
    {
        string path = PortableLayout.InstalledStampPath();
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<PortablePackageStamp>(File.ReadAllText(path), ConfigSerializer.JsonOptions);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static void WriteInstalledStamp(PortableManifestDocument manifest, string servicePath)
    {
        Directory.CreateDirectory(AppPaths.RuntimeDirectory);
        var stamp = new PortablePackageStamp
        {
            ProductVersion = manifest.ProductVersion,
            BuildCommit = manifest.BuildCommit,
            ServicePath = servicePath,
            InstalledAt = DateTimeOffset.UtcNow,
        };
        File.WriteAllText(
            PortableLayout.InstalledStampPath(),
            JsonSerializer.Serialize(stamp, ConfigSerializer.JsonOptions));
    }
}
