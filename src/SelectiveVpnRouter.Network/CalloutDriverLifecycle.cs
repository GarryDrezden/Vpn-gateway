using System.Diagnostics;
using System.ServiceProcess;
using SelectiveVpnRouter.Core.Portable;

namespace SelectiveVpnRouter.Network;

/// <summary>Ensures the kernel callout driver service is running and the device can be opened.</summary>
public static class CalloutDriverLifecycle
{
    private const int ServiceRunningState = 4;

    public sealed record EnsureResult(bool ServiceRunning, bool DeviceOpenable, string? Error);

    /// <summary>Idempotent: starts <see cref="PortableLayout.DriverServiceName"/> when installed; does not install .sys.</summary>
    public static EnsureResult EnsureServiceRunning()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new EnsureResult(false, false, "Callout driver is supported on Windows only.");
        }

        int state = TryQueryServiceState(PortableLayout.DriverServiceName);
        if (state == ServiceRunningState)
        {
            return new EnsureResult(true, DriverEnvironment.DeviceOpenable(), null);
        }

        if (state < 0 && DriverEnvironment.FindSys() is null)
        {
            return new EnsureResult(false, false, "SelectiveVpnCallout.sys not found; driver is not installed.");
        }

        (int exitCode, int? win32, string raw) = RunSc("start " + PortableLayout.DriverServiceName);
        if (exitCode == 0 || win32 == 1056 || TryQueryServiceState(PortableLayout.DriverServiceName) == ServiceRunningState)
        {
            return new EnsureResult(true, DriverEnvironment.DeviceOpenable(), null);
        }

        string message = DescribeScFailure("start", PortableLayout.DriverServiceName, exitCode, win32, raw);
        return new EnsureResult(false, false, message);
    }

    internal static int TryQueryServiceState(string serviceName)
    {
        (int exitCode, _, string raw) = RunSc("query " + serviceName);
        if (exitCode != 0)
        {
            return -1;
        }

        foreach (string line in raw.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("STATE", StringComparison.OrdinalIgnoreCase))
            {
                int colon = trimmed.IndexOf(':');
                if (colon >= 0)
                {
                    string afterColon = trimmed[(colon + 1)..].Trim();
                    int space = afterColon.IndexOf(' ');
                    string token = space >= 0 ? afterColon[..space] : afterColon;
                    if (int.TryParse(token, out int code))
                    {
                        return code;
                    }
                }
            }
        }

        return -1;
    }

    internal static (int ExitCode, int? Win32, string Raw) RunSc(string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "sc.exe",
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        ProcessOutputEncoding.UseConsoleEncoding(psi);
        using Process? process = Process.Start(psi);
        if (process is null)
        {
            return (1, null, string.Empty);
        }

        string raw = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit(8000);
        return (process.ExitCode, TryParseScWin32(raw), raw);
    }

    private static int? TryParseScWin32(string raw)
    {
        const string prefix = "WIN32:";
        int idx = raw.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            return null;
        }

        int start = idx + prefix.Length;
        int end = start;
        while (end < raw.Length && char.IsDigit(raw[end]))
        {
            end++;
        }

        return int.TryParse(raw[start..end], out int code) ? code : null;
    }

    private static string DescribeScFailure(string action, string serviceName, int exitCode, int? win32, string raw)
    {
        string win32Part = win32 is null ? "" : " WIN32=" + win32.Value;
        string detail = string.IsNullOrWhiteSpace(raw) ? "" : " " + raw.Trim();
        return "sc.exe " + action + " " + serviceName + " failed (exit " + exitCode + ")" + win32Part + "." + detail;
    }
}
