using SelectiveVpnRouter.Core.Portable;

namespace SelectiveVpnRouter.Core.Startup;

public static class StartupWindowPolicy
{
    /// <summary>
    /// Hidden tray startup is allowed only when bootstrap does not require blocking user action.
    /// </summary>
    public static bool ShouldStartHidden(bool startMinimized, bool bootstrapRequiresUserAction)
        => startMinimized && !bootstrapRequiresUserAction;

    public static bool BootstrapRequiresUserAction(PortableBootstrapState state)
        => state is not PortableBootstrapState.Ready;
}
