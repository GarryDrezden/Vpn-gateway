namespace SelectiveVpnRouter.Core.ApplicationDiscovery.Windows;

internal static class ShortcutTargetResolver
{
    public static string? TryResolveExecutable(string shortcutPath)
    {
        if (string.IsNullOrWhiteSpace(shortcutPath) || !File.Exists(shortcutPath))
        {
            return null;
        }

        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
            {
                return null;
            }

            object? shell = Activator.CreateInstance(shellType);
            if (shell is null)
            {
                return null;
            }

            dynamic shortcut = shell!.GetType().InvokeMember(
                "CreateShortcut",
                System.Reflection.BindingFlags.InvokeMethod,
                null,
                shell,
                [shortcutPath])!;
            string target = shortcut.TargetPath;
            if (string.IsNullOrWhiteSpace(target) || !target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return File.Exists(target) ? target : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}