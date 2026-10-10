using System.ComponentModel;
using System.ServiceProcess;
using SelectiveVpnRouter.Core.Portable;

namespace SelectiveVpnRouter.Network;

/// <summary>Ensures the kernel callout driver service is running and the device can be opened.</summary>
public static class CalloutDriverLifecycle
{
    internal const int ErrorServiceAlreadyRunning = 1056;
    internal static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    public sealed record EnsureResult(bool ServiceRunning, bool DeviceOpenable, string? Error);

    /// <summary>Idempotent: starts <see cref="PortableLayout.DriverServiceName"/> when installed; does not install .sys.</summary>
    public static EnsureResult EnsureServiceRunning()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new EnsureResult(false, false, "Callout driver is supported on Windows only.");
        }

        ServiceController? controller = TryOpenServiceController(PortableLayout.DriverServiceName);
        if (controller is null)
        {
            if (DriverEnvironment.FindSys() is null)
            {
                return new EnsureResult(false, false, "SelectiveVpnCallout.sys not found; driver is not installed.");
            }

            return new EnsureResult(false, false, "SelectiveVpnCallout service is not registered.");
        }

        using (controller)
        {
            controller.Refresh();
            if (IsRunningStatus(controller.Status))
            {
                return SuccessFromState();
            }

            if (!TryStartService(controller, out string? startError))
            {
                return new EnsureResult(false, false, startError);
            }

            if (!WaitForRunning(controller, StartTimeout, out string? waitError))
            {
                return new EnsureResult(false, false, waitError);
            }

            return SuccessFromState();
        }

        EnsureResult SuccessFromState() => new(true, DriverEnvironment.DeviceOpenable(), null);
    }

    internal static bool IsRunningStatus(ServiceControllerStatus status) =>
        status == ServiceControllerStatus.Running;

    internal static bool IsDriverServiceRunning(int legacyStateCode) =>
        legacyStateCode == (int)ServiceControllerStatus.Running;

    internal static bool IsAlreadyRunningStartOutcome(int exitCode, int? win32) =>
        exitCode == ErrorServiceAlreadyRunning || win32 == ErrorServiceAlreadyRunning;

    internal static bool IsBenignAlreadyRunningException(Exception exception)
    {
        if (exception is Win32Exception win32 && win32.NativeErrorCode == ErrorServiceAlreadyRunning)
        {
            return true;
        }

        return exception is InvalidOperationException invalid
            && invalid.Message.Contains("already running", StringComparison.OrdinalIgnoreCase);
    }

    private static ServiceController? TryOpenServiceController(string serviceName)
    {
        try
        {
            return new ServiceController(serviceName);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static bool TryStartService(ServiceController controller, out string? error)
    {
        error = null;
        controller.Refresh();
        if (IsRunningStatus(controller.Status))
        {
            return true;
        }

        try
        {
            if (controller.Status is ServiceControllerStatus.StopPending)
            {
                controller.WaitForStatus(ServiceControllerStatus.Stopped, StartTimeout);
            }

            controller.Start();
            return true;
        }
        catch (Exception ex) when (IsBenignAlreadyRunningException(ex))
        {
            return true;
        }
        catch (Exception ex)
        {
            error = "StartService SelectiveVpnCallout failed: " + ex.Message;
            return false;
        }
    }

    private static bool WaitForRunning(ServiceController controller, TimeSpan timeout, out string? error)
    {
        error = null;
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            controller.Refresh();
            if (IsRunningStatus(controller.Status))
            {
                return true;
            }

            if (controller.Status is ServiceControllerStatus.StartPending or ServiceControllerStatus.StopPending)
            {
                Thread.Sleep(PollInterval);
                continue;
            }

            if (controller.Status == ServiceControllerStatus.Stopped)
            {
                Thread.Sleep(PollInterval);
                continue;
            }

            break;
        }

        controller.Refresh();
        if (IsRunningStatus(controller.Status))
        {
            return true;
        }

        error = "SelectiveVpnCallout did not reach Running (SCM state=" + (int)controller.Status + ").";
        return false;
    }
}
