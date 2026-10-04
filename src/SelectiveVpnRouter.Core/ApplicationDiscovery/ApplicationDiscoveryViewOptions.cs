namespace SelectiveVpnRouter.Core.ApplicationDiscovery;

public sealed class ApplicationDiscoveryViewOptions
{
    public static ApplicationDiscoveryViewOptions Default { get; } = new();

    public bool ShowSystemAndServiceEntries { get; init; }

    public bool ShowBackgroundProcesses { get; init; }
}