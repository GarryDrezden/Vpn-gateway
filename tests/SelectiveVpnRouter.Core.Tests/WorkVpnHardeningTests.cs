using SelectiveVpnRouter.Core;
using SelectiveVpnRouter.Network;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public class WorkVpnHardeningTests
{
    [Fact]
    public void ConnectWorkVpn_ipc_uses_short_timeout()
    {
        Assert.Equal(IpcTimeouts.ShortOperationMs, IpcTimeouts.OperationTimeoutMs(IpcMethods.ConnectWorkVpn));
    }

    [Fact]
    public void Auth_file_removed_on_mfa_pending_line()
    {
        var controller = NewControllerWithAuthFile(out string path);
        controller.ApplyLogLineForTests("AUTH_PENDING");
        Assert.False(File.Exists(path));
        Assert.False(controller.AuthFileExistsForTests);
        Assert.Equal(WorkVpnSessionState.WaitingForMfa, controller.Phase);
    }

    [Fact]
    public void Auth_file_removed_on_connected_line()
    {
        var controller = NewControllerWithAuthFile(out string path);
        controller.ApplyLogLineForTests("Initialization Sequence Completed");
        Assert.False(File.Exists(path));
        Assert.True(controller.Connected);
    }

    [Fact]
    public void Auth_file_removed_on_auth_failed()
    {
        var controller = NewControllerWithAuthFile(out string path);
        controller.ApplyLogLineForTests("AUTH_FAILED");
        Assert.False(File.Exists(path));
        Assert.Equal(WorkVpnSessionState.Failed, controller.Phase);
    }

    [Fact]
    public void Auth_file_exists_until_auth_event()
    {
        var controller = NewControllerWithAuthFile(out string path);
        Assert.True(File.Exists(path));
        Assert.True(controller.AuthFileExistsForTests);
        controller.ApplyLogLineForTests("PUSH_REPLY");
        Assert.True(File.Exists(path));
    }

    
    [Fact]
    public async Task Auth_file_removed_on_stop()
    {
        WorkOpenVpnController controller = NewControllerWithAuthFile(out string path);
        await controller.StopAsync();
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Long_mfa_budget_remains_for_background_wait_not_ipc()
    {
        Assert.True(WorkVpnConnectBudget.TotalOperationMs >= WorkVpnConnectBudget.MfaWaitMs);
        Assert.Equal(IpcTimeouts.ShortOperationMs, IpcTimeouts.OperationTimeoutMs(IpcMethods.ConnectWorkVpn));
    }    private static WorkOpenVpnController NewControllerWithAuthFile(out string path)
    {
        path = Path.Combine(Path.GetTempPath(), "work-auth-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(path, "user\npass");
        var controller = new WorkOpenVpnController();
        controller.AttachAuthFileForTests(path);
        return controller;
    }
}