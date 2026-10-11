using Microsoft.Win32;
using SelectiveVpnRouter.Core.Startup;

namespace SelectiveVpnRouter.App.Startup;

internal sealed class WindowsStartupRunKey : IWindowsStartupRunKey
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public string? ReadCommand(string valueName)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(valueName) as string;
    }

    public void WriteCommand(string valueName, string command)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Unable to open HKCU Run key.");
        key.SetValue(valueName, command, RegistryValueKind.String);
    }

    public void DeleteValue(string valueName)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }
}
