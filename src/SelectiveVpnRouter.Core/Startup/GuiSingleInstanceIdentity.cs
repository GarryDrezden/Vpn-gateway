using System.Security.Principal;

namespace SelectiveVpnRouter.Core.Startup;

public static class GuiSingleInstanceIdentity
{
    public static string ResolveUserSegment(string? userSecurityIdentifier, string? fallbackUserName)
    {
        if (!string.IsNullOrWhiteSpace(userSecurityIdentifier))
        {
            return NormalizeObjectNameSegment(userSecurityIdentifier);
        }

        return SanitizeUserSegment(fallbackUserName);
    }

    public static string MutexNameForUserSegment(string userSegment)
        => @"Local\VPNRoute.Gui.Instance." + userSegment;

    public static string ActivateEventNameForUserSegment(string userSegment)
        => @"Local\VPNRoute.Gui.Activate." + userSegment;

    public static (string MutexName, string ActivateEventName) NamesForCurrentInteractiveUser()
    {
        string? sid = TryGetCurrentUserSid();
        string segment = ResolveUserSegment(sid, Environment.UserName);
        return (MutexNameForUserSegment(segment), ActivateEventNameForUserSegment(segment));
    }

    internal static string? TryGetCurrentUserSid()
    {
        try
        {
            return WindowsIdentity.GetCurrent().User?.Value;
        }
        catch (PlatformNotSupportedException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string NormalizeObjectNameSegment(string segment) => segment.Trim();

    private static string SanitizeUserSegment(string? userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            return "default";
        }

        return string.Concat(userName.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
    }
}
