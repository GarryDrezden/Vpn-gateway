namespace SelectiveVpnRouter.Core.Portable;

public static class PortableLayout
{
    public const string ServiceExeName = "SelectiveVpnRouter.Service.exe";
    public const string AppExeName = "SelectiveVpnRouter.App.exe";
    public const string BootstrapExeName = "SelectiveVpnRouter.Bootstrap.exe";
    public const string ProbeExeName = "SelectiveVpnRouter.Probe.exe";
    public const string ManifestFileName = "portable-manifest.json";
    public const string ReadmeFileName = "README.txt";
    public const string DriverSubfolder = "driver";
    public const string DriverSysName = "SelectiveVpnCallout.sys";
    public const string DriverInfName = "SelectiveVpnCallout.inf";
    public const string DriverCatName = "SelectiveVpnCallout.cat";
    public const string DriverServiceName = "SelectiveVpnCallout";
    public const string ProductServiceName = "SelectiveVpnRouter";
    public const int PackageFormatVersion = 1;
    public const string InstalledPackageStampFile = "portable-package-stamp.json";

    public static string ExpectedServiceExePath(string portableRoot)
        => Path.Combine(portableRoot, ServiceExeName);

    public static string ExpectedDriverSysPath(string portableRoot)
        => Path.Combine(portableRoot, DriverSubfolder, DriverSysName);

    public static string ExpectedManifestPath(string portableRoot)
        => Path.Combine(portableRoot, ManifestFileName);

    public static string InstalledStampPath()
    {
        string? overridePath = Environment.GetEnvironmentVariable("VPN_ROUTE_PORTABLE_STAMP_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return Path.GetFullPath(overridePath);
        }

        return Path.Combine(AppPaths.RuntimeDirectory, InstalledPackageStampFile);
    }

    public static IReadOnlyList<string> RequiredBootstrapFiles(string portableRoot) =>
    [
        ExpectedServiceExePath(portableRoot),
        Path.Combine(portableRoot, BootstrapExeName),
        ExpectedManifestPath(portableRoot),
        ExpectedDriverSysPath(portableRoot),
    ];
}
