using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using SelectiveVpnRouter.Core;

namespace SelectiveVpnRouter.Network;

public static class SecureCredentialFile
{
    public static WorkVpnAuthFileGuard WriteAuthUserPass(string username, string password)
    {
        Directory.CreateDirectory(AppPaths.RuntimeDirectory);
        string path = Path.Combine(AppPaths.RuntimeDirectory, "work-auth-" + Guid.NewGuid().ToString("N") + ".txt");
        var sb = new StringBuilder();
        sb.AppendLine(username);
        sb.AppendLine(password);
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        TryRestrictToAdministrators(path);
        return new WorkVpnAuthFileGuard(path);
    }

    private static void TryRestrictToAdministrators(string path)
    {
        try
        {
            var info = new FileInfo(path);
            FileSecurity sec = info.GetAccessControl();
            sec.SetAccessRuleProtection(true, false);
            var sys = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            var adm = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            sec.AddAccessRule(new FileSystemAccessRule(sys, FileSystemRights.FullControl, AccessControlType.Allow));
            sec.AddAccessRule(new FileSystemAccessRule(adm, FileSystemRights.FullControl, AccessControlType.Allow));
            info.SetAccessControl(sec);
        }
        catch (Exception)
        {
        }
    }
}