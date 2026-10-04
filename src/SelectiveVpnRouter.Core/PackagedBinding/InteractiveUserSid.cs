using System.Security.Principal;

namespace SelectiveVpnRouter.Core;

public static class InteractiveUserSid
{
    public static string? TryGetCurrent()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            return WindowsIdentity.GetCurrent().User?.Value;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
