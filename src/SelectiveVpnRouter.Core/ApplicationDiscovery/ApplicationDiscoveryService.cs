using SelectiveVpnRouter.Core.ApplicationDiscovery.Windows;

namespace SelectiveVpnRouter.Core.ApplicationDiscovery;

public sealed class ApplicationDiscoveryService
{
    private readonly InteractiveWindowDiscovery _interactiveWindowDiscovery;

    public ApplicationDiscoveryService(InteractiveWindowDiscovery? interactiveWindowDiscovery = null)
    {
        _interactiveWindowDiscovery = interactiveWindowDiscovery ?? new InteractiveWindowDiscovery();
    }

    public Task<IReadOnlyList<DiscoveredApplication>> DiscoverInstalledAsync(
        IEnumerable<RoutingRule> existingRules,
        string? searchTerm = null,
        CancellationToken cancellationToken = default,
        ApplicationDiscoveryViewOptions? options = null)
        => Task.Run(
            () => ApplicationDiscoveryCatalog.BuildInstalledCatalog(
                CollectInstalledCandidates(cancellationToken),
                existingRules,
                searchTerm,
                options),
            cancellationToken);

    public Task<IReadOnlyList<DiscoveredApplication>> DiscoverRunningAsync(
        IEnumerable<RoutingRule> existingRules,
        string? searchTerm = null,
        CancellationToken cancellationToken = default,
        ApplicationDiscoveryViewOptions? options = null)
        => Task.Run(
            () => ApplicationDiscoveryCatalog.BuildRunningCatalog(
                CollectRunningCandidates(cancellationToken, options),
                existingRules,
                searchTerm,
                options),
            cancellationToken);

    private static IEnumerable<DiscoveredApplicationCandidate> CollectInstalledCandidates(CancellationToken cancellationToken)
    {
        foreach (DiscoveredApplicationCandidate candidate in StartMenuApplicationDiscovery.Collect(cancellationToken))
        {
            yield return candidate;
        }

        foreach (DiscoveredApplicationCandidate candidate in AppPathsRegistryDiscovery.Collect(cancellationToken))
        {
            yield return candidate;
        }

        foreach (DiscoveredApplicationCandidate candidate in UninstallRegistryDiscovery.Collect(cancellationToken))
        {
            yield return candidate;
        }

        foreach (DiscoveredApplicationCandidate candidate in PackagedAppDiscovery.Collect(cancellationToken))
        {
            yield return candidate;
        }
    }

    private IEnumerable<DiscoveredApplicationCandidate> CollectRunningCandidates(
        CancellationToken cancellationToken,
        ApplicationDiscoveryViewOptions? options)
    {
        options ??= ApplicationDiscoveryViewOptions.Default;
        foreach (DiscoveredApplicationCandidate candidate in _interactiveWindowDiscovery.Collect(cancellationToken))
        {
            yield return candidate;
        }

        if (options.ShowBackgroundProcesses)
        {
            foreach (DiscoveredApplicationCandidate candidate in RunningProcessDiscovery.Collect(cancellationToken))
            {
                yield return candidate;
            }
        }
    }
}