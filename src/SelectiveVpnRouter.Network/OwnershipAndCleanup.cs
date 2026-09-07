using System.Net;
using System.Security.Principal;
using SelectiveVpnRouter.Core;

namespace SelectiveVpnRouter.Network;

public static class PrivilegeProbe
{
    public static bool IsAdministrator()
    {
        using var id = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(id);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}

public static class GatewayGuess
{
    public static string? FromAdapter(AdapterView nic)
    {
        foreach (string ip in nic.Ipv4)
        {
            if (!IPAddress.TryParse(ip, out IPAddress? addr))
            {
                continue;
            }

            byte[] b = addr.GetAddressBytes();
            if (b.Length == 4 && b[3] != 1)
            {
                b[3] = 1;
                return new IPAddress(b).ToString();
            }
        }

        return nic.Ipv4.FirstOrDefault();
    }
}

public static class RouteOwnership
{
    public static void Apply(RoutePlan plan, List<OwnedRoute> actual, Action<string> log)
    {
        foreach (OwnedRoute doomed in plan.ToRemove)
        {
            if (RouteTable.TryDeleteOwned(doomed, out string err))
            {
                actual.RemoveAll(a => RouteReconciler.Same(a, doomed));
                log("Removed owned route " + doomed.DestinationPrefix + " if=" + doomed.InterfaceIndex);
            }
            else
            {
                log("Failed to remove owned route: " + err);
                actual.RemoveAll(a => RouteReconciler.Same(a, doomed));
            }
        }

        foreach (OwnedRoute add in plan.ToAdd)
        {
            if (RouteTable.TryAddOwned(add, out string err))
            {
                actual.Add(add);
                log("Added owned route " + add.DestinationPrefix + " if=" + add.InterfaceIndex + " metric " + add.Metric);
            }
            else
            {
                log("Failed to add owned route: " + err);
            }
        }
    }

    public static void RemoveAll(List<OwnedRoute> actual, Action<string> log)
    {
        Apply(RouteReconciler.Plan([], actual.ToArray()), actual, log);
    }
}

public static class CrashCleanup
{
    public static void ReconcileStale(Action<string> log)
    {
        CrashState stale = ConfigSerializer.LoadCrashState(AppPaths.CrashStateFile);
        if (stale.OwnedRoutes.Count > 0)
        {
            log("Found stale owned routes from previous run; removing.");
            var actual = stale.OwnedRoutes.ToList();
            RouteOwnership.RemoveAll(actual, log);
        }

        if (stale.OpenVpnPid is int pid)
        {
            try
            {
                using var p = System.Diagnostics.Process.GetProcessById(pid);
                if (p.ProcessName.Contains("openvpn", StringComparison.OrdinalIgnoreCase))
                {
                    p.Kill(entireProcessTree: true);
                    log("Killed stale OpenVPN pid " + pid);
                }
            }
            catch (Exception)
            {
            }
        }

        ConfigSerializer.ClearCrashState(AppPaths.CrashStateFile);
    }
}
