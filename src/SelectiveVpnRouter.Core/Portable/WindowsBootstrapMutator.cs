using System.Diagnostics;
using System.ServiceProcess;

namespace SelectiveVpnRouter.Core.Portable;

public sealed class WindowsBootstrapMutator
{
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
        else
        {
            RunSc($"config {PortableLayout.ProductServiceName} binPath= {quoted} start= auto");
        }

        TryStopService(PortableLayout.ProductServiceName);
        RunSc($"start {PortableLayout.ProductServiceName}");
        WaitForService(PortableLayout.ProductServiceName, ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
    }

    public void RemoveService()
    {
        TryStopService(PortableLayout.ProductServiceName);
        RunSc($"delete {PortableLayout.ProductServiceName}");
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
        bool valid = string.Equals(auth, "Valid", StringComparison.OrdinalIgnoreCase);
        if (!valid)
        {
            bool testsigning = TryTestSigningEnabled();
            if (!testsigning)
            {
                throw new PortableDriverSigningBlockedException(
                    $"Driver signature is not trusted (Authenticode={auth ?? "unknown"}) and test signing is off.");
            }
        }

        string fullSys = Path.GetFullPath(sysPath);
        TryStopService(PortableLayout.DriverServiceName);
        RunSc($"delete {PortableLayout.DriverServiceName}", ignoreErrors: true);
        RunSc($"create {PortableLayout.DriverServiceName} type= kernel start= demand binPath= \"{fullSys}\" DisplayName= \"VPN Route WFP callout\"");
        RunSc($"start {PortableLayout.DriverServiceName}");
        WaitForService(PortableLayout.DriverServiceName, ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
    }

    public void RemoveDriver()
    {
        TryStopService(PortableLayout.DriverServiceName);
        RunSc($"delete {PortableLayout.DriverServiceName}", ignoreErrors: true);
    }

    private static bool TryTestSigningEnabled()
    {
        try
        {
            ProcessStartInfo psi = new()
            {
                FileName = "bcdedit.exe",
                Arguments = "/enum {current}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };
            using Process p = Process.Start(psi)!;
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            return output.Contains("testsigning", StringComparison.OrdinalIgnoreCase)
                && output.Contains("Yes", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void TryStopService(string name)
    {
        try
        {
            using ServiceController sc = new(name);
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

    private static void WaitForService(string name, ServiceControllerStatus status, TimeSpan timeout)
    {
        using ServiceController sc = new(name);
        sc.WaitForStatus(status, timeout);
    }

    private static void RunSc(string args, bool ignoreErrors = false)
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
        if (p.ExitCode != 0 && !ignoreErrors)
        {
            throw new InvalidOperationException($"sc.exe {args} failed ({p.ExitCode}): {output} {err}".Trim());
        }
    }
}

public sealed class PortableDriverSigningBlockedException : Exception
{
    public PortableDriverSigningBlockedException(string message) : base(message)
    {
    }
}
