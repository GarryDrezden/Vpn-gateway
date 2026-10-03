using SelectiveVpnRouter.Core;
using SelectiveVpnRouter.Network;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public class WorkVpnAuthClassifierTests
{
    [Fact]
    public void Plain_auth_failed_is_wrong_credentials()
    {
        const string line = "2026-10-03 09:31:28 AUTH: Received control message: AUTH_FAILED";
        Assert.Equal(WorkVpnAuthResponseKind.WrongCredentials, WorkVpnAuthLineClassifier.Classify(line));
    }

    [Fact]
    public void Auth_failed_with_crv1_is_challenge()
    {
        const string line = "AUTH: Received control message: AUTH_FAILED,CRV1:E:Enter OTP";
        Assert.Equal(WorkVpnAuthResponseKind.ChallengeRequired, WorkVpnAuthLineClassifier.Classify(line));
    }

    [Fact]
    public void Auth_pending_is_mfa_pending()
    {
        Assert.Equal(WorkVpnAuthResponseKind.MfaPending, WorkVpnAuthLineClassifier.Classify("AUTH_PENDING"));
    }

    [Fact]
    public void Plain_auth_failed_terminal_and_releases_auth_file()
    {
        WorkOpenVpnController controller = NewControllerWithAuthFile(out string path);
        controller.ApplyLogLineForTests("AUTH: Received control message: AUTH_FAILED");
        Assert.Equal(WorkVpnSessionState.Failed, controller.Phase);
        Assert.False(File.Exists(path));
        Assert.Equal(WorkVpnAuthResponseKind.WrongCredentials, controller.LastAuthClassification);
    }

    [Fact]
    public void Challenge_does_not_delete_auth_file()
    {
        WorkOpenVpnController controller = NewControllerWithAuthFile(out string path);
        controller.ApplyLogLineForTests("AUTH: Received control message: AUTH_FAILED,CRV1:E:OTP");
        Assert.Equal(WorkVpnSessionState.WaitingForMfa, controller.Phase);
        Assert.True(File.Exists(path));
        Assert.True(controller.AuthFileExistsForTests);
    }

    [Fact]
    public async Task Cancel_after_failure_cleans_auth_file()
    {
        WorkOpenVpnController controller = NewControllerWithAuthFile(out string path);
        controller.ApplyLogLineForTests("AUTH: Received control message: AUTH_FAILED");
        await controller.StopAsync();
        Assert.False(File.Exists(path));
        Assert.Equal(WorkVpnSessionState.Disconnected, controller.Phase);
    }

    [Fact]
    public async Task Connect_gate_released_after_background_task_finally()
    {
        var gate = new object();
        Task? connectTask = null;
        connectTask = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(20).ConfigureAwait(false);
                throw new InvalidOperationException("Authentication failed.");
            }
            catch (InvalidOperationException)
            {
            }
            finally
            {
                lock (gate)
                {
                    connectTask = null;
                }
            }
        });

        await connectTask.ConfigureAwait(true);
        bool inProgress;
        lock (gate)
        {
            inProgress = connectTask is { IsCompleted: false };
        }

        Assert.False(inProgress);
    }

    private static WorkOpenVpnController NewControllerWithAuthFile(out string path)
    {
        path = Path.Combine(Path.GetTempPath(), "work-auth-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(path, "user\npass");
        var controller = new WorkOpenVpnController();
        controller.AttachAuthFileForTests(path);
        return controller;
    }
}