using System.ServiceProcess;

namespace SelectiveVpnRouter.Core.Portable;

public static class WindowsServiceScmSync
{
    public const int ErrorServiceMarkedForDelete = 1072;

    internal static Func<string, bool>? IsServiceRegisteredOverrideForTests;
    internal static Func<string, ServiceControllerStatus?>? TryQueryStatusOverrideForTests;
    internal static Action<TimeSpan>? SleepOverrideForTests;

    public sealed record WaitOutcome(bool Success, string? Error);

    public static bool IsServiceRegistered(string serviceName)
    {
        if (IsServiceRegisteredOverrideForTests is not null)
        {
            return IsServiceRegisteredOverrideForTests(serviceName);
        }

        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            using ServiceController controller = new(serviceName);
            _ = controller.Status;
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public static bool TryQueryStatus(string serviceName, out ServiceControllerStatus status)
    {
        if (TryQueryStatusOverrideForTests is not null)
        {
            ServiceControllerStatus? overridden = TryQueryStatusOverrideForTests(serviceName);
            if (overridden is null)
            {
                status = default;
                return false;
            }

            status = overridden.Value;
            return true;
        }

        status = default;
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            using ServiceController controller = new(serviceName);
            controller.Refresh();
            status = controller.Status;
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public static WaitOutcome WaitUntilServiceAbsent(
        string serviceName,
        TimeSpan timeout,
        TimeSpan pollInterval)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (!IsServiceRegistered(serviceName))
            {
                return new WaitOutcome(true, null);
            }

            PollSleep(pollInterval);
        }

        return new WaitOutcome(
            false,
            $"Service '{serviceName}' is still registered in SCM after {timeout.TotalSeconds:0}s (may be marked for delete).");
    }

    public static WaitOutcome WaitUntilServiceStatus(
        string serviceName,
        ServiceControllerStatus desired,
        TimeSpan timeout,
        TimeSpan pollInterval)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        ServiceControllerStatus last = default;
        bool sawStatus = false;

        while (DateTime.UtcNow < deadline)
        {
            if (!TryQueryStatus(serviceName, out ServiceControllerStatus status))
            {
                if (desired == ServiceControllerStatus.Stopped)
                {
                    return new WaitOutcome(true, null);
                }

                PollSleep(pollInterval);
                continue;
            }

            sawStatus = true;
            last = status;
            if (status == desired)
            {
                return new WaitOutcome(true, null);
            }

            if (status is ServiceControllerStatus.StartPending or ServiceControllerStatus.StopPending)
            {
                PollSleep(pollInterval);
                continue;
            }

            if (desired == ServiceControllerStatus.Running && status == ServiceControllerStatus.Stopped)
            {
                PollSleep(pollInterval);
                continue;
            }

            break;
        }

        if (!sawStatus && desired == ServiceControllerStatus.Stopped)
        {
            return new WaitOutcome(true, null);
        }

        return new WaitOutcome(
            false,
            $"Service '{serviceName}' did not reach {desired} (last SCM state={(int)last}).");
    }

    public static bool IsMarkedForDeleteScFailure(int exitCode, string? output = null)
    {
        if (exitCode == ErrorServiceMarkedForDelete)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(output))
        {
            return false;
        }

        return output.Contains("1072", StringComparison.Ordinal)
            || output.Contains("MARKED_FOR_DELETE", StringComparison.OrdinalIgnoreCase);
    }

    internal static void PollSleep(TimeSpan interval)
    {
        if (SleepOverrideForTests is not null)
        {
            SleepOverrideForTests(interval);
            return;
        }

        if (interval > TimeSpan.Zero)
        {
            Thread.Sleep(interval);
        }
    }
}
