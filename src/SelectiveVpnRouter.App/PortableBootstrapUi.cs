using System.IO;
using SelectiveVpnRouter.Core.Portable;

namespace SelectiveVpnRouter.App;

internal static class PortableBootstrapUi
{
    private static readonly PortableBootstrapProcessLauncher Launcher = new();

    internal static string ResolvePortableRoot()
        => Path.GetFullPath(AppContext.BaseDirectory);

    internal static PortableBootstrapStatus ReadStatus(string? portableRoot = null)
    {
        string root = portableRoot ?? ResolvePortableRoot();
        return new PortableBootstrapEngine().GetStatus(root);
    }

    internal static bool NeedsUserBootstrapAction(PortableBootstrapState state)
        => state is PortableBootstrapState.NeedsServiceRegistration
            or PortableBootstrapState.NeedsDriverRegistration
            or PortableBootstrapState.NeedsRepair
            or PortableBootstrapState.VersionMismatch;

    internal static PortableBootstrapLaunchResult LaunchElevatedRepair(string portableRoot)
        => Launcher.LaunchElevatedRepair(portableRoot);

    internal static PortableBootstrapLaunchResult LaunchElevatedRemove(string portableRoot)
        => Launcher.LaunchElevatedRemove(portableRoot);
}