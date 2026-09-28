using System.Security.AccessControl;
using System.Security.Principal;

namespace SelectiveVpnRouter.Core;

public static class ProgramDataStorage
{
    public static void EnsureConfigured()
    {
        Directory.CreateDirectory(AppPaths.ProgramData);
        Directory.CreateDirectory(AppPaths.RuntimeDirectory);
        Directory.CreateDirectory(AppPaths.LogDirectory);

        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            ApplyServiceDirectoryAcl(AppPaths.ProgramData);
        }
        catch (Exception)
        {
        }
    }

    public static bool IsDirectoryWritable(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            string probe = Path.Combine(directory, ".write-probe-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static void LogStartupDiagnostics(Action<string> log)
    {
        string identity = WindowsIdentity.GetCurrent().Name;
        bool writable = IsDirectoryWritable(AppPaths.ProgramData);
        log($"service identity={identity}");
        log($"config path={AppPaths.ConfigFile}");
        log($"config directory writable={writable}");
        if (!writable)
        {
            log("Config directory is not writable for the service identity. "
                + "Re-run scripts/install-service.ps1 as Administrator to repair ACL on "
                + AppPaths.ProgramData + ".");
        }
    }

    private static void ApplyServiceDirectoryAcl(string directory)
    {
        var info = new DirectoryInfo(directory);
        DirectorySecurity security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        AddFullControl(security, WellKnownSidType.LocalSystemSid);
        AddFullControl(security, WellKnownSidType.BuiltinAdministratorsSid);
        AddReadExecute(security, WellKnownSidType.BuiltinUsersSid);

        info.SetAccessControl(security);
    }

    private static void AddFullControl(DirectorySecurity security, WellKnownSidType sidType)
    {
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(sidType, null),
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
    }

    private static void AddReadExecute(DirectorySecurity security, WellKnownSidType sidType)
    {
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(sidType, null),
            FileSystemRights.Read | FileSystemRights.ReadAndExecute,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
    }
}