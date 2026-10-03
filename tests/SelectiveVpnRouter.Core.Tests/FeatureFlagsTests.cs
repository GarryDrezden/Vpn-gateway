using SelectiveVpnRouter.Core;
using SelectiveVpnRouter.Network;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public class FeatureFlagsTests : IDisposable
{
    private readonly string? _previous;

    public FeatureFlagsTests()
    {
        _previous = Environment.GetEnvironmentVariable(FeatureFlags.WorkVpnEnvironmentVariable);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(FeatureFlags.WorkVpnEnvironmentVariable, _previous);
    }

    private static void SetWorkVpnEnv(string? value)
        => Environment.SetEnvironmentVariable(FeatureFlags.WorkVpnEnvironmentVariable, value);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("false")]
    [InlineData("no")]
    public void Work_vpn_disabled_by_default_and_falsy_env(string? raw)
    {
        SetWorkVpnEnv(raw);
        Assert.False(FeatureFlags.WorkVpn);
        Assert.False(WorkVpnUiProjection.IsVisible);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("TRUE")]
    [InlineData("yes")]
    [InlineData("on")]
    public void Work_vpn_env_override_enables(string raw)
    {
        SetWorkVpnEnv(raw);
        Assert.True(FeatureFlags.WorkVpn);
        Assert.True(WorkVpnUiProjection.IsVisible);
    }

    [Fact]
    public void Connect_work_vpn_throws_feature_disabled_without_side_effects()
    {
        SetWorkVpnEnv(null);
        WorkVpnFeatureDisabledException ex = Assert.Throws<WorkVpnFeatureDisabledException>(() => WorkVpnFeatureGate.ThrowIfDisabled());
        Assert.Equal(WorkVpnFeatureGate.DisabledErrorCode, ex.Message);
    }

    [Fact]
    public void Disabled_work_snapshot_is_inert()
    {
        WorkVpnLiveStatus snap = WorkVpnLiveStatusFactory.Disabled();
        Assert.False(snap.FeatureEnabled);
        Assert.False(snap.Configured);
        Assert.False(snap.WorkVpnReady);
        Assert.Equal(WorkVpnSessionState.Disconnected, snap.State);
        Assert.Null(snap.ProcessId);
        Assert.Null(snap.LastError);
    }

    [Fact]
    public void Selective_connect_ipc_timeout_unchanged_when_work_vpn_flag_off()
    {
        SetWorkVpnEnv(null);
        Assert.False(FeatureFlags.WorkVpn);
        Assert.Equal(IpcTimeouts.ConnectVpnMs, IpcTimeouts.OperationTimeoutMs(IpcMethods.ConnectVpn));
        Assert.Equal(IpcTimeouts.ShortOperationMs, IpcTimeouts.OperationTimeoutMs(IpcMethods.ConnectWorkVpn));
    }

    [Fact]
    public void Work_openvpn_controller_still_testable_when_flag_off()
    {
        SetWorkVpnEnv(null);
        Assert.False(FeatureFlags.WorkVpn);
        var controller = new WorkOpenVpnController();
        controller.ApplyLogLineForTests("AUTH_PENDING");
        Assert.Equal(WorkVpnSessionState.WaitingForMfa, controller.Phase);
    }
}