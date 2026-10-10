using System.ComponentModel;
using System.ServiceProcess;
using SelectiveVpnRouter.Core.Portable;

namespace SelectiveVpnRouter.Network;

/// <summary>Ensures the kernel callout driver service is running and the device can be opened.</summary>
public static class CalloutDriverLifecycle
{
    internal const int ErrorServiceAlreadyRunning = 1056;
    internal const int ErrorServiceDoesNotExist = 1060;
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

        ServiceController controller;
        try
        {
            controller = new ServiceController(PortableLayout.DriverServiceName);
        }
        catch (Exception ex) when (IsServiceMissingException(ex))
        {
            return MissingDriverServiceResult();
        }

        using (controller)
        {
            if (!TryGetServiceStatus(controller, out ServiceControllerStatus status, out EnsureResult? missing))
            {
                return missing!;
            }

            if (IsRunningStatus(status))
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

    internal static EnsureResult MissingDriverServiceResult()
    {
        if (DriverEnvironment.FindSys() is null)
        {
            return new EnsureResult(false, false, "SelectiveVpnCallout.sys not found; driver is not installed.");
        }

        return new EnsureResult(false, false, "driver-service-not-registered");
    }

    internal static bool IsServiceMissingException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is Win32Exception win32 && win32.NativeErrorCode == ErrorServiceDoesNotExist)
            {
                return true;
            }

            if (current is InvalidOperationException invalid
                && invalid.Message.Contains("was not found", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool TryGetServiceStatus(
        ServiceController controller,
        out ServiceControllerStatus status,
        out EnsureResult? failure)
    {
        failure = null;
        try
        {
            controller.Refresh();
            status = controller.Status;
            return true;
        }
        catch (Exception ex) when (IsServiceMissingException(ex))
        {
            status = default;
            failure = MissingDriverServiceResult();
            return false;
        }
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

    private static bool TryStartService(ServiceController controller, out string? error)
    {
        error = null;
        if (!TryGetServiceStatus(controller, out ServiceControllerStatus status, out EnsureResult? missing))
        {
            error = missing!.Error;
            return false;
        }

        if (IsRunningStatus(status))
        {
            return true;
        }

        try
        {
            if (status is ServiceControllerStatus.StopPending)
            {
                controller.WaitForStatus(ServiceControllerStatus.Stopped, StartTimeout);
            }

            controller.Start();
            return true;
        }
        catch (Exception ex) when (IsServiceMissingException(ex))
        {
            error = MissingDriverServiceResult().Error;
            return false;
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
            if (!TryGetServiceStatus(controller, out ServiceControllerStatus status, out EnsureResult? missing))
            {
                error = missing!.Error;
                return false;
            }

            if (IsRunningStatus(status))
            {
                return true;
            }

            if (status is ServiceControllerStatus.StartPending or ServiceControllerStatus.StopPending)
            {
                Thread.Sleep(PollInterval);
                continue;
            }

            if (status == ServiceControllerStatus.Stopped)
            {
                Thread.Sleep(PollInterval);
                continue;
            }

            break;
        }

        if (!TryGetServiceStatus(controller, out ServiceControllerStatus finalStatus, out EnsureResult? finalMissing))
        {
            error = finalMissing!.Error;
            return false;
        }

        if (IsRunningStatus(finalStatus))
        {
            return true;
        }

        error = "SelectiveVpnCallout did not reach Running (SCM state=" + (int)finalStatus + ").";
        return false;
    }
}
