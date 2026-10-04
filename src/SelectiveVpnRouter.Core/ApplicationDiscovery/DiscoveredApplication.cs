namespace SelectiveVpnRouter.Core.ApplicationDiscovery;

public sealed record DiscoveredApplicationCandidate
{
    public required string ExecutablePath { get; init; }
    public string? DisplayName { get; init; }
    public string? Publisher { get; init; }
    public string? IconPath { get; init; }
    public DiscoverySource Source { get; init; }
    public bool IsRunning { get; init; }
    public ApplicationDiscoveryLaunchConfidence LaunchConfidence { get; init; } = ApplicationDiscoveryLaunchConfidence.Medium;
    public PackagedApplicationIdentity? PackageIdentity { get; init; }
    public string? InstallLocation { get; init; }
    public bool IsSystemComponent { get; init; }
}

public sealed record DiscoveredApplication
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string ExecutablePath { get; init; }
    public string? Publisher { get; init; }
    public string? IconPath { get; init; }
    public DiscoverySource Sources { get; init; }
    public bool IsRunning { get; init; }
    public RouteMode? ExistingRouteMode { get; init; }
    public ApplicationDiscoveryLaunchConfidence LaunchConfidence { get; init; }
    public PackagedApplicationIdentity? PackageIdentity { get; init; }

    public bool IsAlreadyConfigured => ExistingRouteMode is not null;
    public string ExecutableFileName => Path.GetFileName(ExecutablePath);
}