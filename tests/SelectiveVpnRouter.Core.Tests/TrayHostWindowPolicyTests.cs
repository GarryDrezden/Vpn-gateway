using SelectiveVpnRouter.Core.Tray;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public class TrayHostWindowPolicyTests
{
    [Fact]
    public void Minimized_while_running_hides_to_tray()
    {
        Assert.True(TrayHostWindowPolicy.ShouldHideToTrayWhenMinimized(isMinimized: true, isShuttingDown: false));
    }

    [Fact]
    public void Minimized_during_shutdown_does_not_hide_to_tray()
    {
        Assert.False(TrayHostWindowPolicy.ShouldHideToTrayWhenMinimized(isMinimized: true, isShuttingDown: true));
    }

    [Fact]
    public void Restore_needed_when_hidden_or_minimized()
    {
        Assert.True(TrayHostWindowPolicy.ShouldRestoreToNormal(isHidden: true, isMinimized: false));
        Assert.True(TrayHostWindowPolicy.ShouldRestoreToNormal(isHidden: false, isMinimized: true));
    }

    [Fact]
    public void Restore_not_needed_when_already_visible_and_normal()
    {
        Assert.False(TrayHostWindowPolicy.ShouldRestoreToNormal(isHidden: false, isMinimized: false));
    }
}
