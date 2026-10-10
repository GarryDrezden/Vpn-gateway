using System.ServiceProcess;

namespace SelectiveVpnRouter.Core.Portable;

public static class PortableBootstrapPostRepairProbe
{
    public const int DefaultProductServiceWaitMs = 30_000;
    public const int DefaultIpcReadinessWaitMs = 45_000;

    internal static Func<TimeSpan, WindowsServiceScmSync.WaitOutcome>? WaitForProductServiceRunningOverrideForTests;
    internal static int? IpcReadinessWaitMsOverrideForTests;

    public static WindowsServiceScmSync.WaitOutcome WaitForProductServiceRunning(
        TimeSpan timeout,
        TimeSpan pollInterval)
    {
        if (WaitForProductServiceRunningOverrideForTests is not null)
        {
            return WaitForProductServiceRunningOverrideForTests(timeout);
        }

        return WindowsServiceScmSync.WaitUntilServiceStatus(
            PortableLayout.ProductServiceName,
            ServiceControllerStatus.Running,
            timeout,
            pollInterval);
    }
}
