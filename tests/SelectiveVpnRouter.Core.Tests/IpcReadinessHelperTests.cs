using SelectiveVpnRouter.Core;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public class IpcReadinessHelperTests
{
    [Theory]
    [InlineData("IPC response header EOF")]
    [InlineData("Pipe not found")]
    [InlineData("Unable to connect to pipe")]
    [InlineData("The pipe has been ended.")]
    public void Transient_ipc_errors_are_detected(string message)
    {
        Assert.True(IpcReadinessHelper.IsTransientIpcError(message));
    }

    [Theory]
    [InlineData("GetStatus failed: protocol mismatch")]
    [InlineData("Invalid IPC response length: -1")]
    [InlineData("RunDiagnostic failed")]
    public void Non_transient_ipc_errors_are_not_retried(string message)
    {
        Assert.False(IpcReadinessHelper.IsTransientIpcError(message));
    }
}