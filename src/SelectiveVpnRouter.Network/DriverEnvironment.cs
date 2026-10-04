using System.Diagnostics;
using System.Net;
using SelectiveVpnRouter.Core;

namespace SelectiveVpnRouter.Network;

public sealed record DriverEnvironmentReport
{
    public bool WdkReady { get; init; }
    public string WdkMessage { get; init; } = "";
    public string? SysPath { get; init; }
    public string SignatureStatus { get; init; } = "Unknown";
    public string SecureBoot { get; init; } = "unknown";
    public string TestSigning { get; init; } = "unknown";
    public string Hvci { get; init; } = "unknown";
    public string ServiceStatus { get; init; } = "not installed";
    public bool DeviceOpenable { get; init; }
}

public static class DriverEnvironment
{
    public static string? FindSys()
    {
        var candidates = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "SelectiveVpnCallout.sys"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "drivers", "SelectiveVpnCallout.sys"),
        };

        string? repoRelease = RepoPathResolver.ResolveRepoRelativePath("artifacts", "driver", "staging", "Release", "SelectiveVpnCallout.sys")
            ?? RepoPathResolver.ResolveRepoRelativePath("artifacts", "driver", "Release", "SelectiveVpnCallout.sys");
        if (repoRelease is not null)
        {
            candidates.Add(repoRelease);
        }

        string? repoDebug = RepoPathResolver.ResolveRepoRelativePath("artifacts", "driver", "Debug", "SelectiveVpnCallout.sys");
        if (repoDebug is not null)
        {
            candidates.Add(repoDebug);
        }

        return candidates.FirstOrDefault(File.Exists);
    }

    public static DriverEnvironmentReport Capture()
    {
        string? sys = FindSys();
        string sig = sys is null
            ? "missing"
            : "present (Authenticode: run scripts\\check-driver.ps1 / Get-AuthenticodeSignature)";

        string wdk = DetectWdk();
        return new DriverEnvironmentReport
        {
            WdkReady = wdk.StartsWith("WDK ready", StringComparison.Ordinal),
            WdkMessage = wdk,
            SysPath = sys,
            SignatureStatus = sig,
            SecureBoot = ReadSecureBoot(),
            TestSigning = ReadTestSigning(),
            Hvci = ReadHvci(),
            ServiceStatus = ReadService(),
            DeviceOpenable = DeviceOpenable(),
        };
    }

    public static bool DeviceOpenable()
    {
        using CalloutDriverClient c = CalloutDriverClient.TryOpen();
        return c.IsLoaded;
    }

    private static string DetectWdk()
    {
        string vs = FindVs();
        bool toolset = vs.Length > 0 && Directory.Exists(Path.Combine(vs, @"MSBuild\Microsoft\VC\v170\Platforms\x64\PlatformToolsets\WindowsKernelModeDriver10.0"));
        string kits = @"C:\Program Files (x86)\Windows Kits\10\Include";
        bool km = Directory.Exists(kits) && Directory.GetFiles(kits, "fwpsk.h", SearchOption.AllDirectories).Any();
        bool wdf = Directory.Exists(kits) && Directory.GetFiles(Path.Combine(@"C:\Program Files (x86)\Windows Kits\10\Include", "wdf"), "wdf.h", SearchOption.AllDirectories).Length > 0;
        if (toolset && km && wdf)
        {
            return "WDK ready";
        }

        var missing = new List<string>();
        if (!toolset) missing.Add("platform toolset WindowsKernelModeDriver10.0");
        if (!km) missing.Add("km\\fwpsk.h");
        if (!wdf) missing.Add("wdf.h");
        return "WDK not ready: " + string.Join(", ", missing) + ". Install WDK from https://learn.microsoft.com/en-us/windows-hardware/drivers/download-the-wdk — scripts never enable TESTSIGNING.";
    }

    private static string FindVs()
    {
        string vswhere = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft Visual Studio\Installer\vswhere.exe");
        if (!File.Exists(vswhere))
        {
            return "";
        }

        try
        {
            var p = Process.Start(new ProcessStartInfo
            {
                FileName = vswhere,
                ArgumentList = { "-latest", "-products", "*", "-requires", "Microsoft.VisualStudio.Component.VC.Tools.x86.x64", "-property", "installationPath" },
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (p is null)
            {
                return "";
            }

            string path = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit(5000);
            return path;
        }
        catch (Exception)
        {
            return "";
        }
    }

    private static string ReadSecureBoot()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
            object? v = key?.GetValue("UEFISecureBootEnabled");
            return v is int i ? (i == 1 ? "On" : "Off") : "unknown";
        }
        catch (Exception ex)
        {
            return "unknown (" + ex.Message + ")";
        }
    }

    private static string ReadTestSigning()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "bcdedit.exe",
                Arguments = "/enum {current}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            ProcessOutputEncoding.UseConsoleEncoding(psi);
            using var p = Process.Start(psi);
            if (p is null)
            {
                return "unknown";
            }

            string o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit(4000);
            if (o.Contains("testsigning", StringComparison.OrdinalIgnoreCase) && o.Contains("Yes", StringComparison.OrdinalIgnoreCase))
            {
                return "On";
            }

            return p.ExitCode == 0 ? "Off" : "unknown";
        }
        catch (Exception ex)
        {
            return "unknown (" + ex.Message + ")";
        }
    }

    private static string ReadHvci()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity");
            object? v = key?.GetValue("Enabled");
            return v is int i && i == 1 ? "On" : "Off";
        }
        catch (Exception)
        {
            return "unknown";
        }
    }

    private static string ReadService()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\SelectiveVpnCallout");
            if (key is null)
            {
                return "not installed";
            }

            return "installed start=" + key.GetValue("Start");
        }
        catch (Exception ex)
        {
            return "unknown (" + ex.Message + ")";
        }
    }
}

public static class PreferredRoutes
{
    public static DefaultRouteSnapshot? PreferredDirectDefault()
    {
        IReadOnlyList<RouteRow> defaults = RouteTable.DefaultRoutes().OrderBy(r => r.Metric).ToList();
        if (defaults.Count == 0)
        {
            return null;
        }

        RouteRow top = defaults[0];
        AdapterView? nic = AdapterCatalog.All().FirstOrDefault(a => a.Ipv4Index == top.InterfaceIndex);
        return new DefaultRouteSnapshot
        {
            InterfaceIndex = top.InterfaceIndex,
            Metric = top.Metric,
            NextHop = top.NextHop.ToString(),
            AdapterName = nic?.Name ?? "",
            IsVpnAdapter = nic is not null && (AdapterCatalog.LooksVpn(nic.Name) || AdapterCatalog.LooksVpn(nic.Description)),
        };
    }

    public static bool PreferredIsStolen(bool isVpnAdapter, uint metric)
        => isVpnAdapter && metric < 5000;

    public static bool IsPreferredStolen(DefaultRouteSnapshot preferred)
        => PreferredIsStolen(preferred.IsVpnAdapter, preferred.Metric);

    public static DiagnosticResult Evaluate(int? vpnIfIndex, uint ownedMetric = 9000)
    {
        DefaultRouteSnapshot? preferred = PreferredDirectDefault();
        if (preferred is null)
        {
            return new DiagnosticResult { Name = "preferred-default", Outcome = DiagnosticOutcomes.Fail, Message = "No IPv4 default route." };
        }

        string text = $"DIRECT preferred default: if={preferred.InterfaceIndex} ({preferred.AdapterName}) via {preferred.NextHop} metric {preferred.Metric}.";
        if (vpnIfIndex is int vpn)
        {
            RouteRow? vpnDefault = RouteTable.DefaultRoutes().FirstOrDefault(r => r.InterfaceIndex == vpn);
            if (vpnDefault is not null)
            {
                text += $" SelectiveVpn owned fallback candidate: VPN if={vpn} via {vpnDefault.NextHop} metric {vpnDefault.Metric} (expected owned metric {ownedMetric}).";
            }
            else
            {
                text += " No 0.0.0.0/0 on the VPN NIC (proxy may get WSAENETUNREACH until the owned high-metric default is added).";
            }
        }

        bool stolen = IsPreferredStolen(preferred);
        if (stolen)
        {
            return new DiagnosticResult { Name = "preferred-default", Outcome = DiagnosticOutcomes.Fail, Message = text + " FAIL: preferred default is the VPN NIC with a low metric." };
        }

        return new DiagnosticResult { Name = "preferred-default", Outcome = DiagnosticOutcomes.Pass, Message = text };
    }
}
