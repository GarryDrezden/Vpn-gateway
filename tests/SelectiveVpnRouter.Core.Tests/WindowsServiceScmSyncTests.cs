using System.ServiceProcess;
using SelectiveVpnRouter.Core.Portable;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

[Collection("PortableBootstrapSerial")]
public class WindowsServiceScmSyncTests
{
    [Fact]
    public void Marked_for_delete_exit_code_is_transitional()
    {
        Assert.True(WindowsServiceScmSync.IsMarkedForDeleteScFailure(1072));
        Assert.True(WindowsServiceScmSync.IsMarkedForDeleteScFailure(1, "ERROR_SERVICE_MARKED_FOR_DELETE"));
        Assert.False(WindowsServiceScmSync.IsMarkedForDeleteScFailure(1060));
    }

    [Fact]
    public void Deletion_pending_waits_until_service_is_gone()
    {
        int polls = 0;
        WindowsServiceScmSync.IsServiceRegisteredOverrideForTests = _ =>
        {
            polls++;
            return polls < 4;
        };
        WindowsServiceScmSync.SleepOverrideForTests = _ => { };
        try
        {
            WindowsServiceScmSync.WaitOutcome outcome = WindowsServiceScmSync.WaitUntilServiceAbsent(
                "SelectiveVpnCallout",
                TimeSpan.FromSeconds(5),
                TimeSpan.FromMilliseconds(1));
            Assert.True(outcome.Success);
            Assert.True(polls >= 4);
        }
        finally
        {
            WindowsServiceScmSync.IsServiceRegisteredOverrideForTests = null;
            WindowsServiceScmSync.SleepOverrideForTests = null;
        }
    }

    [Fact]
    public void Deletion_timeout_returns_clear_error()
    {
        WindowsServiceScmSync.IsServiceRegisteredOverrideForTests = _ => true;
        WindowsServiceScmSync.SleepOverrideForTests = _ => { };
        try
        {
            WindowsServiceScmSync.WaitOutcome outcome = WindowsServiceScmSync.WaitUntilServiceAbsent(
                "SelectiveVpnCallout",
                TimeSpan.FromMilliseconds(5),
                TimeSpan.FromMilliseconds(1));
            Assert.False(outcome.Success);
            Assert.Contains("SelectiveVpnCallout", outcome.Error, StringComparison.Ordinal);
            Assert.Contains("marked for delete", outcome.Error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            WindowsServiceScmSync.IsServiceRegisteredOverrideForTests = null;
            WindowsServiceScmSync.SleepOverrideForTests = null;
        }
    }

    [Fact]
    public void Service_running_wait_succeeds_when_scm_reports_running()
    {
        int polls = 0;
        WindowsServiceScmSync.TryQueryStatusOverrideForTests = _ =>
        {
            polls++;
            return polls < 3 ? ServiceControllerStatus.StartPending : ServiceControllerStatus.Running;
        };
        WindowsServiceScmSync.SleepOverrideForTests = _ => { };
        try
        {
            WindowsServiceScmSync.WaitOutcome outcome = WindowsServiceScmSync.WaitUntilServiceStatus(
                "SelectiveVpnRouter",
                ServiceControllerStatus.Running,
                TimeSpan.FromSeconds(5),
                TimeSpan.FromMilliseconds(1));
            Assert.True(outcome.Success);
            Assert.True(polls >= 3);
        }
        finally
        {
            WindowsServiceScmSync.TryQueryStatusOverrideForTests = null;
            WindowsServiceScmSync.SleepOverrideForTests = null;
        }
    }

    [Fact]
    public void Mutator_waits_for_scm_absence_before_driver_create()
    {
        string? repo = FindRepoRoot();
        Assert.NotNull(repo);
        string body = File.ReadAllText(Path.Combine(repo, "src", "SelectiveVpnRouter.Core", "Portable", "WindowsBootstrapMutator.cs"));
        Assert.Contains("WaitUntilServiceAbsent", body, StringComparison.Ordinal);
        Assert.Contains("CreateDriverServiceWithMarkedForDeleteRetry", body, StringComparison.Ordinal);
    }

    private static string? FindRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && dir is not null; i++)
        {
            if (File.Exists(Path.Combine(dir, "SelectiveVpnRouter.sln")))
            {
                return dir;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        return null;
    }
}
