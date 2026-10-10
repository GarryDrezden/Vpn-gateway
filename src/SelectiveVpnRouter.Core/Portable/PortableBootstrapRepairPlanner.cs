namespace SelectiveVpnRouter.Core.Portable;

public static class PortableBootstrapRepairPlanner
{
    public static bool DriverImagePathMatches(DriverProbeSnapshot driver, string expectedSysFullPath)
    {
        string? registered = PublishRuntimeLifecyclePlanner.TryNormalizeDriverImagePath(driver.BinaryPath);
        return registered is not null
            && PortableRootValidator.PathsEqual(registered, expectedSysFullPath);
    }

    /// <summary>Driver registered on the canonical sys path and already running — no SCM mutation.</summary>
    public static bool ShouldSkipDriverRepair(DriverProbeSnapshot driver, string expectedSysFullPath) =>
        driver.Installed && DriverImagePathMatches(driver, expectedSysFullPath) && driver.Running;

    /// <summary>Path is correct but driver service is stopped — start only.</summary>
    public static bool ShouldOnlyStartDriver(DriverProbeSnapshot driver, string expectedSysFullPath) =>
        driver.Installed && DriverImagePathMatches(driver, expectedSysFullPath) && !driver.Running;

    /// <summary>Driver exists but ImagePath differs — update binPath in place (no delete/recreate).</summary>
    public static bool ShouldUpdateDriverBinPathInPlace(DriverProbeSnapshot driver, string expectedSysFullPath) =>
        driver.Installed && !DriverImagePathMatches(driver, expectedSysFullPath);

    public static bool ShouldSkipProductServiceRestart(ServiceProbeSnapshot service, string expectedServiceExePath)
    {
        if (!service.Installed)
        {
            return false;
        }

        if (!PortableServicePathComparer.ServicePathsMatch(service.ImagePath, expectedServiceExePath))
        {
            return false;
        }

        return service.Running;
    }
}
