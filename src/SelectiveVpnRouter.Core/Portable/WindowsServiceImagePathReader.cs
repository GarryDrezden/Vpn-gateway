using Microsoft.Win32;

namespace SelectiveVpnRouter.Core.Portable;

internal static class WindowsServiceImagePathReader
{
    internal static string? TryReadImagePath(string serviceName)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(serviceName))
        {
            return null;
        }

        string? fromRegistry = TryReadFromRegistry(serviceName);
        if (!string.IsNullOrWhiteSpace(fromRegistry))
        {
            return fromRegistry.Trim();
        }

        return WindowsSystemBootstrapProbe.TryParseScQcImagePath(serviceName);
    }

    private static string? TryReadFromRegistry(string serviceName)
    {
        try
        {
            string keyPath = @"SYSTEM\CurrentControlSet\Services\" + serviceName;
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(keyPath, writable: false);
            if (key is null)
            {
                return null;
            }

            return key.GetValue("ImagePath") as string;
        }
        catch (Exception)
        {
            return null;
        }
    }
}