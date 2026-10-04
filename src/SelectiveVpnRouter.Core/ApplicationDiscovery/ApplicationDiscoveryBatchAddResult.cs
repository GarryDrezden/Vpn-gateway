namespace SelectiveVpnRouter.Core.ApplicationDiscovery;

public sealed record ApplicationDiscoveryBatchAddResult
{
    public required AppConfiguration Config { get; init; }
    public int AddedCount { get; init; }
    public int SkippedDuplicateCount { get; init; }
    public int SkippedAlreadyConfiguredCount { get; init; }
    public IReadOnlyList<RoutingRule> AddedRules { get; init; } = [];
}