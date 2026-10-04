namespace SelectiveVpnRouter.Core;

public enum PackagedRoutingTargetKind
{
    Primary = 0,
    AssociatedHelper = 1,
}

public sealed record PackagedRoutingTarget
{
    public required string ApplicationId { get; init; }
    public required string ExecutablePath { get; init; }
    public required string RelativeExecutablePath { get; init; }
    public required PackagedRoutingTargetKind Kind { get; init; }
    public bool Resolved { get; init; } = true;
    public string? UnresolvedReason { get; init; }
}

public sealed record PackagedRoutingTargetsForRule
{
    public required RoutingRule Rule { get; init; }
    public required string PrimaryExecutablePath { get; init; }
    public required IReadOnlyList<PackagedRoutingTarget> Targets { get; init; }
    public required IReadOnlyList<string> VpnWfpExecutablePaths { get; init; }
}
