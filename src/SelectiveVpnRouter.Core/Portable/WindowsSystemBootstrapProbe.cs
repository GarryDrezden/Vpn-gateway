using System.Diagnostics;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;

namespace SelectiveVpnRouter.Core.Portable;

public sealed class WindowsSystemBootstrapProbe : ISystemBootstrapProbe
{
    public bool IsElevated()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        WindowsPrincipal principal = new(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public ServiceProbeSnapshot ProbeProductService()
        => ProbeWindowsService(PortableLayout.ProductServiceName);

    public DriverProbeSnapshot ProbeCalloutDriver()
    {
        ServiceProbeSnapshot svc = ProbeWindowsService(PortableLayout.DriverServiceName);
        return new DriverProbeSnapshot
        {
            Installed = svc.Installed,
            Running = svc.Running,
            BinaryPath = svc.ImagePath,
            Version = svc.Version,
        };
    }

    private static ServiceProbeSnapshot ProbeWindowsService(string serviceName)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new ServiceProbeSnapshot();
        }

        try
        {
            using ServiceController sc = new(serviceName);
            bool installed = true;
            bool running = sc.Status == ServiceControllerStatus.Running;
            string? path = WindowsServiceImagePathReader.TryReadImagePath(serviceName);
            string? version = null;
            if (PortableServicePathComparer.TryExtractExecutablePath(path, out string normalizedExe, out _)
                && File.Exists(normalizedExe))
            {
                version = FileVersionInfo.GetVersionInfo(normalizedExe).FileVersion;
            }

            return new ServiceProbeSnapshot
            {
                Installed = installed,
                Running = running,
                ImagePath = path,
                Version = version,
            };
        }
        catch (InvalidOperationException)
        {
            return new ServiceProbeSnapshot { Installed = false };
        }
    }

    internal static string? TryParseScQcImagePath(string serviceName)
    {
        try
        {
            ProcessStartInfo psi = new()
            {
                FileName = "sc.exe",
                Arguments = "qc " + serviceName,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
            };
            using Process p = Process.Start(psi)!;
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            foreach (string line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                if (!TryParseScQcPathLine(line, out string? value))
                {
                    continue;
                }

                return value;
            }
        }
        catch (Exception)
        {
        }

        return null;
    }

    internal static bool TryParseScQcPathLine(string line, out string? imagePath)
    {
        imagePath = null;
        ReadOnlySpan<string> labels =
        [
            "BINARY_PATH_NAME",
            "BINPATH",
            "\u0418\u043C\u044F_\u0434\u0432\u043E\u0438\u0447\u043D\u043E\u0433\u043E_\u0444\u0430\u0439\u043B\u0430",
        ];

        foreach (string label in labels)
        {
            int idx = line.IndexOf(label, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                continue;
            }

            int colon = line.IndexOf(':', idx + label.Length);
            if (colon < 0)
            {
                continue;
            }

            imagePath = line[(colon + 1)..].Trim();
            return !string.IsNullOrWhiteSpace(imagePath);
        }

        return false;
    }

    public string? TryReadAuthenticodeStatus(string filePath)
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(filePath))
        {
            return null;
        }

        try
        {
            ProcessStartInfo psi = new()
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -Command \"(Get-AuthenticodeSignature -LiteralPath '{filePath.Replace("'", "''")}').Status.ToString()\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using Process p = Process.Start(psi)!;
            string output = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit(8000);
            return string.IsNullOrWhiteSpace(output) ? null : output;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
