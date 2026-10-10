using SelectiveVpnRouter.Core.Portable;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

[Collection("PortableBootstrapSerial")]
public class PortableIpcProbeTests
{
    [Fact]
    public void A_ipc_succeeds_on_fourth_attempt()
    {
        int calls = 0;
        PortableIpcProbe.GetStatusAttemptOverrideForTests = _ =>
        {
            calls++;
            return calls < 4
                ? new PortableIpcProbe.GetStatusAttemptResult(false, false, "Unable to connect", false)
                : new PortableIpcProbe.GetStatusAttemptResult(true, true, null, false);
        };
        try
        {
            PortableIpcSmokeResult result = PortableIpcProbe.WaitForGetStatusReady(
                totalTimeoutMs: 5_000,
                pollIntervalMs: 10);
            Assert.True(result.Ready);
            Assert.Equal(4, result.Attempts);
        }
        finally
        {
            PortableIpcProbe.GetStatusAttemptOverrideForTests = null;
        }
    }

    [Fact]
    public void B_ipc_timeout_returns_not_ready()
    {
        PortableIpcProbe.GetStatusAttemptOverrideForTests = _ =>
            new PortableIpcProbe.GetStatusAttemptResult(false, false, "Unable to connect", false);
        try
        {
            PortableIpcSmokeResult result = PortableIpcProbe.WaitForGetStatusReady(
                totalTimeoutMs: 200,
                pollIntervalMs: 20);
            Assert.False(result.Ready);
            Assert.True(result.Attempts >= 1);
        }
        finally
        {
            PortableIpcProbe.GetStatusAttemptOverrideForTests = null;
        }
    }

    [Fact]
    public void C_get_status_ok_with_service_alive_only_is_ready()
    {
        PortableIpcProbe.GetStatusAttemptOverrideForTests = _ =>
            new PortableIpcProbe.GetStatusAttemptResult(true, true, null, false);
        try
        {
            Assert.True(PortableIpcProbe.WaitForGetStatusReady(totalTimeoutMs: 500, pollIntervalMs: 10).Ready);
        }
        finally
        {
            PortableIpcProbe.GetStatusAttemptOverrideForTests = null;
        }
    }

    [Fact]
    public void D_non_retryable_protocol_error_fails_fast()
    {
        int calls = 0;
        PortableIpcProbe.GetStatusAttemptOverrideForTests = _ =>
        {
            calls++;
            return new PortableIpcProbe.GetStatusAttemptResult(false, false, "GetStatus failed: protocol mismatch", true);
        };
        try
        {
            PortableIpcSmokeResult result = PortableIpcProbe.WaitForGetStatusReady(totalTimeoutMs: 5_000, pollIntervalMs: 10);
            Assert.False(result.Ready);
            Assert.Equal(1, result.Attempts);
            Assert.True(result.NonRetryable);
        }
        finally
        {
            PortableIpcProbe.GetStatusAttemptOverrideForTests = null;
        }
    }
}