namespace SelectiveVpnRouter.Core.Portable;

public static class PortableBootstrapStatusEvaluator
{
    public static PortableBootstrapStatus Evaluate(
        string portableRootCandidate,
        ISystemBootstrapProbe probe,
        AppConfiguration? config = null)
    {
        if (!PortableRootValidator.TryNormalizePortableRoot(portableRootCandidate, out string root, out string? rootError))
        {
            return Broken(rootError ?? "Invalid portable root.", portableRootCandidate);
        }

        foreach (string required in PortableLayout.RequiredBootstrapFiles(root))
        {
            if (!File.Exists(required))
            {
                return Broken("Missing required package file: " + Path.GetFileName(required), root);
            }
        }

        PortableManifestDocument? manifest = PortableManifestIO.TryRead(root);
        if (manifest is null)
        {
            return Broken("portable-manifest.json is missing or invalid.", root);
        }

        string expectedService = PortableLayout.ExpectedServiceExePath(root);
        ServiceProbeSnapshot service = probe.ProbeProductService();
        DriverProbeSnapshot driver = probe.ProbeCalloutDriver();
        (bool openVpnOk, string? openVpnPath) = PortableOpenVpnProbe.EvaluateOpenVpn(config);
        PortablePackageStamp? stamp = PortableManifestIO.TryReadInstalledStamp();

        bool pathMatches = PortableServicePathComparer.ServicePathsMatch(service.ImagePath, expectedService);
        bool versionMismatch = stamp is not null
            && (!string.Equals(stamp.ProductVersion, manifest.ProductVersion, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(stamp.BuildCommit, manifest.BuildCommit, StringComparison.OrdinalIgnoreCase));

        PortableBootstrapState state;
        string message;
        if (!service.Installed)
        {
            state = PortableBootstrapState.NeedsServiceRegistration;
            message = "Windows service SelectiveVpnRouter is not registered.";
        }
        else if (!pathMatches)
        {
            state = PortableBootstrapState.NeedsRepair;
            message = "Registered service path does not match this portable copy.";
        }
        else if (versionMismatch)
        {
            state = PortableBootstrapState.VersionMismatch;
            message = "Installed system components were registered by a different package version.";
        }
        else if (!driver.Installed)
        {
            state = PortableBootstrapState.NeedsDriverRegistration;
            message = "Product callout driver service is not registered.";
        }
        else if (service.Installed && pathMatches && !service.Running)
        {
            state = PortableBootstrapState.NeedsRepair;
            message = "Service is registered but not running.";
        }
        else
        {
            state = PortableBootstrapState.Ready;
            message = openVpnOk
                ? "Portable bootstrap is ready."
                : "Portable bootstrap is ready. Configure OpenVPN Community before connecting.";
        }

        return new PortableBootstrapStatus
        {
            BootstrapState = state,
            Message = message,
            PortableRoot = root,
            PackageVersion = manifest.ProductVersion,
            BuildCommit = manifest.BuildCommit,
            ServiceInstalled = service.Installed,
            ServiceRunning = service.Running,
            RegisteredServicePath = service.ImagePath,
            ExpectedServicePath = expectedService,
            ServicePathMatches = pathMatches,
            ServiceVersion = service.Version,
            DriverInstalled = driver.Installed,
            DriverRunning = driver.Running,
            DriverVersion = manifest.DriverVersion,
            OpenVpnAvailable = openVpnOk,
            OpenVpnPath = openVpnPath,
        };
    }

    private static PortableBootstrapStatus Broken(string message, string root) => new()
    {
        BootstrapState = PortableBootstrapState.Broken,
        Message = message,
        PortableRoot = root,
    };
}
