namespace SelectiveVpnRouter.Core.Portable;

public static class PortableOpenVpnProbe
{
    public static (bool Available, string? Path) EvaluateOpenVpn(AppConfiguration? config)
    {
        AppConfiguration cfg = config ?? ConfigSerializer.LoadOrDefault(AppPaths.ConfigFile);
        string? path = cfg.Vpn.OpenVpnPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return (false, null);
        }

        path = path.Trim();
        return (File.Exists(path), path);
    }
}
