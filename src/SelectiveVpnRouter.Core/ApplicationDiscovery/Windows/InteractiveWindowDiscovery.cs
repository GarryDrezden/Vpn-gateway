using SelectiveVpnRouter.Core.ApplicationDiscovery;

namespace SelectiveVpnRouter.Core.ApplicationDiscovery.Windows;

public sealed class InteractiveWindowDiscovery
{
    private readonly IInteractiveWindowEnumerator _enumerator;
    private readonly IProcessPathResolver _processPathResolver;

    public InteractiveWindowDiscovery(
        IInteractiveWindowEnumerator? enumerator = null,
        IProcessPathResolver? processPathResolver = null)
    {
        _enumerator = enumerator ?? new User32InteractiveWindowEnumerator();
        _processPathResolver = processPathResolver ?? new ProcessPathResolver();
    }

    public IEnumerable<DiscoveredApplicationCandidate> Collect(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            yield break;
        }

        HashSet<string> seenExecutableKeys = new(StringComparer.OrdinalIgnoreCase);
        foreach (InteractiveWindowSnapshot window in _enumerator.EnumerateVisibleTopLevelWindows())
        {
            cancellationToken.ThrowIfCancellationRequested();

            string? exePath = _processPathResolver.TryGetExecutablePath(window.ProcessId);
            if (string.IsNullOrWhiteSpace(exePath))
            {
                continue;
            }

            if (!ApplicationDiscoveryFilter.IsUsableExecutablePath(exePath, requireExistingFile: true))
            {
                continue;
            }

            if (ApplicationDiscoveryFilter.IsUnderWindowsDirectory(exePath))
            {
                continue;
            }

            if (!ApplicationDiscoveryMerger.TryNormalizeIdentity(exePath, out string key))
            {
                continue;
            }

            if (!seenExecutableKeys.Add(key))
            {
                continue;
            }

            string displayName = string.IsNullOrWhiteSpace(window.WindowTitle)
                ? Path.GetFileNameWithoutExtension(exePath)
                : window.WindowTitle.Trim();

            yield return new DiscoveredApplicationCandidate
            {
                ExecutablePath = exePath,
                DisplayName = displayName,
                Source = DiscoverySource.InteractiveWindow,
                IsRunning = true,
                LaunchConfidence = ApplicationDiscoveryLaunchConfidence.High,
            };
        }
    }
}