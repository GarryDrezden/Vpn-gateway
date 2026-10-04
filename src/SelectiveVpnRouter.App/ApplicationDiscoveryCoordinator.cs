using System.Collections.ObjectModel;
using SelectiveVpnRouter.Core;
using SelectiveVpnRouter.Core.ApplicationDiscovery;

namespace SelectiveVpnRouter.App;

public sealed class ApplicationDiscoveryCoordinator
{
    private readonly ApplicationDiscoveryService _service = new();
    private readonly ObservableCollection<DiscoveredApplicationRow> _installedRows = [];
    private readonly ObservableCollection<DiscoveredApplicationRow> _runningRows = [];
    private IReadOnlyList<DiscoveredApplication>? _installedCache;
    private IReadOnlyList<DiscoveredApplication>? _runningCache;
    private CancellationTokenSource? _installedCts;
    private CancellationTokenSource? _runningCts;

    public ObservableCollection<DiscoveredApplicationRow> InstalledRows => _installedRows;
    public ObservableCollection<DiscoveredApplicationRow> RunningRows => _runningRows;

    public bool IsInstalledLoading { get; private set; }
    public bool IsRunningLoading { get; private set; }

    public bool ShowSystemAndServiceEntries { get; set; }
    public bool ShowBackgroundProcesses { get; set; }

    public event Action? StateChanged;
    public event Action? SelectionCountsChanged;

    public async Task RefreshInstalledAsync(IEnumerable<RoutingRule> rules, string? searchTerm, bool forceRescan = false)
    {
        CancelInstalled();
        _installedCts = new CancellationTokenSource();
        CancellationToken token = _installedCts.Token;
        IsInstalledLoading = true;
        NotifyStateChanged();

        try
        {
            if (forceRescan || _installedCache is null)
            {
                _installedCache = await _service.DiscoverInstalledAsync(
                    rules,
                    searchTerm: null,
                    token,
                    CreateViewOptions());
            }

            IReadOnlyList<DiscoveredApplication> filtered = ApplicationDiscoverySearch.Filter(_installedCache, searchTerm);
            ReplaceRows(_installedRows, filtered, OnSelectionChanged);
        }
        finally
        {
            IsInstalledLoading = false;
            NotifyStateChanged();
        }
    }

    public async Task RefreshRunningAsync(IEnumerable<RoutingRule> rules, string? searchTerm)
    {
        CancelRunning();
        _runningCts = new CancellationTokenSource();
        CancellationToken token = _runningCts.Token;
        IsRunningLoading = true;
        NotifyStateChanged();

        try
        {
            _runningCache = await _service.DiscoverRunningAsync(
                rules,
                searchTerm: null,
                token,
                CreateViewOptions());
            IReadOnlyList<DiscoveredApplication> filtered = ApplicationDiscoverySearch.Filter(_runningCache, searchTerm);
            ReplaceRows(_runningRows, filtered, OnSelectionChanged);
        }
        finally
        {
            IsRunningLoading = false;
            NotifyStateChanged();
        }
    }

    public IReadOnlyList<DiscoveredApplicationRow> GetSelectedInstalled() =>
        _installedRows.Where(r => r.IsSelected).ToList();

    public IReadOnlyList<DiscoveredApplicationRow> GetSelectedRunning() =>
        _runningRows.Where(r => r.IsSelected).ToList();

    public void ClearInstalledSelection()
    {
        foreach (DiscoveredApplicationRow row in _installedRows)
        {
            row.IsSelected = false;
        }
    }

    public void ClearRunningSelection()
    {
        foreach (DiscoveredApplicationRow row in _runningRows)
        {
            row.IsSelected = false;
        }
    }

    private ApplicationDiscoveryViewOptions CreateViewOptions() => new()
    {
        ShowSystemAndServiceEntries = ShowSystemAndServiceEntries,
        ShowBackgroundProcesses = ShowBackgroundProcesses,
    };

    private void OnSelectionChanged() => SelectionCountsChanged?.Invoke();

    private void CancelInstalled()
    {
        _installedCts?.Cancel();
        _installedCts?.Dispose();
        _installedCts = null;
    }

    private void CancelRunning()
    {
        _runningCts?.Cancel();
        _runningCts?.Dispose();
        _runningCts = null;
    }

    private void NotifyStateChanged() => StateChanged?.Invoke();

    private static void ReplaceRows(
        ObservableCollection<DiscoveredApplicationRow> target,
        IReadOnlyList<DiscoveredApplication> applications,
        Action onSelectionChanged)
    {
        target.Clear();
        foreach (DiscoveredApplication app in applications)
        {
            var row = new DiscoveredApplicationRow(app);
            row.SelectedChanged += onSelectionChanged;
            target.Add(row);
        }
    }
}