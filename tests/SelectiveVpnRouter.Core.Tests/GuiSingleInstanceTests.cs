using SelectiveVpnRouter.Core.Startup;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public sealed class GuiSingleInstanceTests
{
    private const string SampleSid = "S-1-5-21-1234567890-123456789-123456789-1001";

    [Fact]
    public void Mutex_and_event_names_use_sid_when_available()
    {
        string segment = GuiSingleInstanceIdentity.ResolveUserSegment(SampleSid, "alice");
        Assert.Equal(SampleSid, segment);
        Assert.StartsWith(@"Local\VPNRoute.Gui.Instance.S-1-5-21-", GuiSingleInstanceIdentity.MutexNameForUserSegment(segment), StringComparison.Ordinal);
        Assert.StartsWith(@"Local\VPNRoute.Gui.Activate.S-1-5-21-", GuiSingleInstanceIdentity.ActivateEventNameForUserSegment(segment), StringComparison.Ordinal);
    }

    [Fact]
    public void Mutex_and_event_names_differ_by_sid()
    {
        string mutexA = GuiSingleInstanceIdentity.MutexNameForUserSegment(
            GuiSingleInstanceIdentity.ResolveUserSegment(SampleSid, "alice"));
        string mutexB = GuiSingleInstanceIdentity.MutexNameForUserSegment(
            GuiSingleInstanceIdentity.ResolveUserSegment("S-1-5-21-9999999999-999999999-999999999-1002", "alice"));
        Assert.NotEqual(mutexA, mutexB);
    }

    [Fact]
    public void ResolveUserSegment_falls_back_to_sanitized_username_without_sid()
    {
        string segment = GuiSingleInstanceIdentity.ResolveUserSegment(null, "alice.test");
        Assert.Equal("alice_test", segment);
    }

    [Theory]
    [InlineData(true, true, true, false)]
    [InlineData(true, false, false, true)]
    [InlineData(false, true, true, false)]
    [InlineData(false, false, true, false)]
    public void GuiSingleInstancePolicy_primary_and_signal_rules(
        bool singleInstanceEnabled,
        bool acquiredPrimaryMutex,
        bool expectedPrimary,
        bool expectedSignal)
    {
        GuiSingleInstanceAttempt attempt = GuiSingleInstancePolicy.Evaluate(singleInstanceEnabled, acquiredPrimaryMutex);
        Assert.Equal(expectedPrimary, attempt.IsPrimary);
        Assert.Equal(expectedSignal, attempt.ShouldSignalExistingInstance);
    }

    [Fact]
    public void App_checks_single_instance_before_main_window()
    {
        string app = ReadRepoFile(Path.Combine("src", "SelectiveVpnRouter.App", "App.xaml.cs"));
        int singleInstance = app.IndexOf("GuiSingleInstanceCoordinator", StringComparison.Ordinal);
        int mainWindow = app.IndexOf("new MainWindow()", StringComparison.Ordinal);
        Assert.True(singleInstance >= 0);
        Assert.True(mainWindow > singleInstance);
        Assert.Contains("!LayoutDebugOptions.Enabled", app, StringComparison.Ordinal);
    }

    [Fact]
    public void Coordinator_uses_sid_based_identity_resolution()
    {
        string coordinator = ReadRepoFile(Path.Combine("src", "SelectiveVpnRouter.App", "Startup", "GuiSingleInstanceCoordinator.cs"));
        Assert.Contains("NamesForCurrentInteractiveUser", coordinator, StringComparison.Ordinal);
    }

    [Fact]
    public void Second_instance_activates_existing_window_path()
    {
        string main = ReadRepoFile(Path.Combine("src", "SelectiveVpnRouter.App", "MainWindow.xaml.cs"));
        Assert.Contains("ActivateFromSecondInstance", main, StringComparison.Ordinal);
        Assert.Contains("ShowMainWindowFromTray", main, StringComparison.Ordinal);
        string app = ReadRepoFile(Path.Combine("src", "SelectiveVpnRouter.App", "App.xaml.cs"));
        Assert.Contains("ActivateFromSecondInstance", app, StringComparison.Ordinal);
        Assert.Contains("Shutdown(0)", app, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath)
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            string candidate = Path.Combine(dir, relativePath);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException(relativePath + " not found");
    }
}
