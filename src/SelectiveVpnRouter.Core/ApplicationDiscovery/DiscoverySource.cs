namespace SelectiveVpnRouter.Core.ApplicationDiscovery;

[Flags]
public enum DiscoverySource
{
    None = 0,
    StartMenu = 1 << 0,
    AppPaths = 1 << 1,
    Uninstall = 1 << 2,
    RunningProcess = 1 << 3,
    PackagedApp = 1 << 4,
    Manual = 1 << 5,
    InteractiveWindow = 1 << 6,
}