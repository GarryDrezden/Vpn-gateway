using System.Diagnostics;
using System.ServiceProcess;

namespace SelectiveVpnRouter.Core.Portable;

public sealed class WindowsBootstrapMutator : IBootstrapSystemMutator
{
    internal const int ServiceDeletionWaitSeconds = 60;
    internal const int DriverStartWaitSeconds = 30;
    internal const int ProductServiceStartWaitSeconds = 30;
    internal const int CreateRetryAttempts = 30;
    internal static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    private readonly ISystemBootstrapProbe _probe;

    public WindowsBootstrapMutator(ISystemBootstrapProbe probe) => _probe = probe;

    public void EnsureProgramData()
    {
        ProgramDataStorage.EnsureConfigured();
    }

    public void InstallOrRepairService(string portableRoot, string serviceExePath)
    {
        if (!PortableRootValidator.PathsEqual(PortableLayout.ExpectedServiceExePath(portableRoot), serviceExePath))
        {
            throw new InvalidOperationException("Service executable path is outside the validated portable package.");
        }

        if (!PortableRootValidator.IsPathInsidePortableRoot(portableRoot, serviceExePath))
        {
            throw new InvalidOperationException("Service executable must be inside portable root.");
        }

        if (!File.Exists(serviceExePath))
        {
            throw new FileNotFoundException("Service executable not found.", serviceExePath);
        }

        string quoted = "\"" + serviceExePath + "\"";
        ServiceProbeSnapshot existing = _probe.ProbeProductService();
        if (!existing.Installed)
        {
            RunSc($"create {PortableLayout.ProductServiceName} binPath= {quoted} start= auto DisplayName= \"VPN Route\"");
            RunSc($"description {PortableLayout.ProductServiceName} \"Elevated backend for VPN Route (OpenVPN, owned routes, WFP, proxy).\"");
        }
        else if (!PortableServicePathComparer.ServicePathsMatch(existing.ImagePath, serviceExePath))
        {
            RunSc($"config {PortableLayout.ProductServiceName} binPath= {quoted} start= auto");
        }

        if (PortableBootstrapRepairPlanner.ShouldSkipProductServiceRestart(existing, serviceExePath))
        {
            return;
        }

        TryStopService(PortableLayout.ProductServiceName);
        RunSc($"start {PortableLayout.ProductServiceName}");
        WaitForServiceRunning(
            PortableLayout.ProductServiceName,
            TimeSpan.FromSeconds(ProductServiceStartWaitSeconds));
    }

    public void RemoveService()
    {
        TryStopService(PortableLayout.ProductServiceName);
        RunSc($"delete {PortableLayout.ProductServiceName}", ignoreErrors: true);
        WindowsServiceScmSync.WaitOutcome absent = WindowsServiceScmSync.WaitUntilServiceAbsent(
            PortableLayout.ProductServiceName,
            TimeSpan.FromSeconds(ServiceDeletionWaitSeconds),
            PollInterval);
        if (!absent.Success)
        {
            throw new InvalidOperationException(absent.Error ?? "Product service deletion did not complete.");
        }
    }

    public void InstallOrRepairDriver(string portableRoot, string sysPath)
    {
        if (!PortableRootValidator.PathsEqual(PortableLayout.ExpectedDriverSysPath(portableRoot), sysPath))
        {
            throw new InvalidOperationException("Driver sys path is outside the validated portable package.");
        }

        if (!PortableRootValidator.IsPathInsidePortableRoot(portableRoot, sysPath))
        {
            throw new InvalidOperationException("Driver sys must be inside portable root.");
        }

        if (!File.Exists(sysPath))
        {
            throw new FileNotFoundException("Driver sys not found.", sysPath);
        }

        string? auth = _probe.TryReadAuthenticodeStatus(sysPath);
        if (PortableDriverSigningPolicy.BlocksUnsignedInstall(auth, _probe.IsTestSigningEnabled()))
        {
            throw new PortableDriverSigningBlockedException(
                $"Driver signature is not trusted (Authenticode={auth ?? "unknown"}) and test signing is off.");
        }

        string fullSys = Path.GetFullPath(sysPath);
        DriverProbeSnapshot driver = _probe.ProbeCalloutDriver();

        if (PortableBootstrapRepairPlanner.ShouldSkipDriverRepair(driver, fullSys))
        {
            return;
        }

        if (PortableBootstrapRepairPlanner.ShouldOnlyStartDriver(driver, fullSys))
        {
            RunSc($"start {PortableLayout.DriverServiceName}");
            WaitForServiceRunning(
                PortableLayout.DriverServiceName,
                TimeSpan.FromSeconds(DriverStartWaitSeconds));
            return;
        }

        if (PortableBootstrapRepairPlanner.ShouldUpdateDriverBinPathInPlace(driver, fullSys))
        {
            TryStopService(PortableLayout.DriverServiceName);
            RunSc(
                $"config {PortableLayout.DriverServiceName} type= kernel start= demand binPath= \"{fullSys}\" DisplayName= \"VPN Route WFP callout\"");
            RunSc($"start {PortableLayout.DriverServiceName}");
            WaitForServiceRunning(
                PortableLayout.DriverServiceName,
                TimeSpan.FromSeconds(DriverStartWaitSeconds));
            return;
        }

        RecreateDriverService(fullSys);
    }

    public void RemoveDriver()
    {
        TryStopService(PortableLayout.DriverServiceName);
        RunSc($"delete {PortableLayout.DriverServiceName}", ignoreErrors: true);
        WindowsServiceScmSync.WaitOutcome absent = WindowsServiceScmSync.WaitUntilServiceAbsent(
            PortableLayout.DriverServiceName,
            TimeSpan.FromSeconds(ServiceDeletionWaitSeconds),
            PollInterval);
        if (!absent.Success)
        {
            throw new InvalidOperationException(absent.Error ?? "Callout driver service deletion did not complete.");
        }
    }

    private static void RecreateDriverService(string fullSysPath)
    {
        TryStopService(PortableLayout.DriverServiceName);
        if (WindowsServiceScmSync.IsServiceRegistered(PortableLayout.DriverServiceName))
        {
            RunSc($"delete {PortableLayout.DriverServiceName}", ignoreErrors: true);
            WindowsServiceScmSync.WaitOutcome absent = WindowsServiceScmSync.WaitUntilServiceAbsent(
                PortableLayout.DriverServiceName,
                TimeSpan.FromSeconds(ServiceDeletionWaitSeconds),
                PollInterval);
            if (!absent.Success)
            {
                throw new InvalidOperationException(absent.Error ?? "Callout driver service deletion did not complete.");
            }
        }

        CreateDriverServiceWithMarkedForDeleteRetry(fullSysPath);
        RunSc($"start {PortableLayout.DriverServiceName}");
        WaitForServiceRunning(
            PortableLayout.DriverServiceName,
            TimeSpan.FromSeconds(DriverStartWaitSeconds));
    }

    private static void CreateDriverServiceWithMarkedForDeleteRetry(string fullSysPath)
    {
        string createArgs =
            $"create {PortableLayout.DriverServiceName} type= kernel start= demand binPath= \"{fullSysPath}\" DisplayName= \"VPN Route WFP callout\"";
        for (int attempt = 1; attempt <= CreateRetryAttempts; attempt++)
        {
            ScResult result = RunScCapture(createArgs);
            if (result.Succeeded)
            {
                return;
            }

            if (!WindowsServiceScmSync.IsMarkedForDeleteScFailure(result.ExitCode, result.CombinedOutput))
            {
                throw new InvalidOperationException(
                    $"sc.exe {createArgs} failed ({result.ExitCode}): {result.CombinedOutput}".Trim());
            }

            WindowsServiceScmSync.WaitOutcome absent = WindowsServiceScmSync.WaitUntilServiceAbsent(
                PortableLayout.DriverServiceName,
                TimeSpan.FromSeconds(ServiceDeletionWaitSeconds),
                PollInterval);
            if (absent.Success)
            {
                continue;
            }

            if (attempt == CreateRetryAttempts)
            {
                throw new InvalidOperationException(
                    absent.Error
                    ?? $"sc.exe create failed with ERROR_SERVICE_MARKED_FOR_DELETE (1072) and service did not disappear.");
            }
        }
    }

    private static void TryStopService(string name)
    {
        try
        {
            using ServiceController sc = new(name);
            sc.Refresh();
            if (sc.Status == ServiceControllerStatus.Running)
            {
                sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (Exception)
        {
            RunSc($"stop {name}", ignoreErrors: true);
        }
    }

    private static void WaitForServiceRunning(string name, TimeSpan timeout)
    {
        WindowsServiceScmSync.WaitOutcome outcome = WindowsServiceScmSync.WaitUntilServiceStatus(
            name,
            ServiceControllerStatus.Running,
            timeout,
            PollInterval);
        if (!outcome.Success)
        {
            throw new InvalidOperationException(outcome.Error ?? $"Service '{name}' did not reach Running.");
        }
    }

    private static void RunSc(string args, bool ignoreErrors = false)
    {
        ScResult result = RunScCapture(args);
        if (!result.Succeeded && !ignoreErrors)
        {
            throw new InvalidOperationException($"sc.exe {args} failed ({result.ExitCode}): {result.CombinedOutput}".Trim());
        }
    }

    internal sealed record ScResult(int ExitCode, string Output, string Error)
    {
        public string CombinedOutput => (Output + " " + Error).Trim();
        public bool Succeeded => ExitCode == 0;
    }

    internal static ScResult RunScCapture(string args)
    {
        ProcessStartInfo psi = new()
        {
            FileName = "sc.exe",
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        using Process p = Process.Start(psi)!;
        string err = p.StandardError.ReadToEnd();
        string output = p.StandardOutput.ReadToEnd();
        p.WaitForExit(30_000);
        return new ScResult(p.ExitCode, output, err);
    }
}

public sealed class PortableDriverSigningBlockedException : Exception
{
    public PortableDriverSigningBlockedException(string message) : base(message)
    {
    }
}
