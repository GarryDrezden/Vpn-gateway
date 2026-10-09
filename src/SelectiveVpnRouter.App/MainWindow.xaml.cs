using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using SelectiveVpnRouter.Core;
using SelectiveVpnRouter.Core.ApplicationDiscovery;
using SelectiveVpnRouter.Core.BrowserRouting;
using SelectiveVpnRouter.Core.Portable;
using SelectiveVpnRouter.Core.RoutingTrace;
using Forms = System.Windows.Forms;
using MessageBox = System.Windows.MessageBox;
using Window = System.Windows.Window;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using RadioButton = System.Windows.Controls.RadioButton;
using Color = System.Windows.Media.Color;

namespace SelectiveVpnRouter.App;

public partial class MainWindow : Window
{
    private readonly ServiceClient _client = new();
    private readonly ObservableCollection<ApplicationRuleRow> _appRules = [];
    private readonly ObservableCollection<ApplicationRuleRow> _filteredAppRules = [];
    private readonly ObservableCollection<RuleRow> _advancedRules = [];
    private readonly List<ConnectionFlowRow> _allFlowRows = [];
    private readonly ObservableCollection<ConnectionGroupViewModel> _connectionGroups = [];
    private readonly ObservableCollection<ConnectionGroupViewModel> _connectionRecentGroups = [];
    private readonly HashSet<string> _expandedConnectionGroups = new(StringComparer.OrdinalIgnoreCase);
    private string? _selectedConnectionGroupKey;
    private string? _selectedConnectionEndpointKey;
    private ServiceSnapshot? _lastSnapshot;
    private readonly ObservableCollection<RealAppFlowHistoryRow> _realAppFlows = [];
    private readonly List<string> _resolvedTargetAddresses = [];
    private int _targetPort = 443;
    private IReadOnlyList<TempAppVpnFlowDto> _lastRealAppFlowDtos = [];
    private readonly Forms.NotifyIcon _tray = new();
    private readonly PeriodicTimer _timer = new(TimeSpan.FromSeconds(2));
    private CancellationTokenSource _cts = new();
    private bool _exit;
    private bool _connectUiActive;
    private bool _workConnectPollActive;
    private bool _bootstrapGateActive;
    private bool _refreshLoopStarted;
    private AppConfiguration _config = new();
    private readonly ApplicationDiscoveryCoordinator _appDiscovery = new();
    private bool _installedDiscoveryLoaded;
    private string _lastDiagRaw = string.Empty;
    private enum RoutingTraceUiPhase { Idle, Starting, Active, Stopping, Completed }

    private RoutingTraceUiPhase _routingTracePhase = RoutingTraceUiPhase.Idle;
    private Guid? _routingTracePollSessionId;
    private long _routingTraceAfterSequence;
    private string _routingTraceLastReport = string.Empty;
    private readonly ObservableCollection<RoutingTraceEventRow> _routingTraceRows = [];
    private readonly Dictionary<Guid, RoutingTraceEventRow> _routingTraceRowIndex = new();

    public MainWindow()
    {
        InitializeComponent();
        ApplyWorkVpnFeatureVisibility();
        if (LayoutDebugOptions.Enabled)
        {
            LayoutDebugHelper.Attach(this, RootDock);
        }

        AppsList.ItemsSource = _filteredAppRules;
        InstalledAppsList.ItemsSource = _appDiscovery.InstalledRows;
        RunningAppsList.ItemsSource = _appDiscovery.RunningRows;
        _appDiscovery.StateChanged += () => Dispatcher.Invoke(UpdateDiscoveryLoadingUi);
        _appDiscovery.SelectionCountsChanged += () => Dispatcher.Invoke(UpdateDiscoverySelectionBars);
        RulesGrid.ItemsSource = _advancedRules;
        RealAppFlowGrid.ItemsSource = _realAppFlows;
        RoutingTraceGrid.ItemsSource = _routingTraceRows;
        ConnectionGroupsList.ItemsSource = _connectionGroups;
        ConnectionRecentGroupsList.ItemsSource = _connectionRecentGroups;
        FlowSearchBox.TextChanged += (_, _) =>
        {
            UpdateFlowSearchPlaceholder();
            RefreshConnectionsBoard();
        };
        UpdateFlowSearchPlaceholder();
        UpdateInstalledAppsSearchPlaceholder();
        UpdateRunningAppsSearchPlaceholder();
        FlowFilterAll.Click += OnFlowFilterChanged;
        FlowFilterVpn.Click += OnFlowFilterChanged;
        FlowFilterDirect.Click += OnFlowFilterChanged;
        FlowFilterMixed.Click += OnFlowFilterChanged;
        FlowFilterErrors.Click += OnFlowFilterChanged;
        AppSearchBox.TextChanged += (_, _) => RefreshAppsFilter();
        _tray.Text = AppBranding.ProductName;
        _tray.Visible = true;
        Icon = AppIconHelper.WpfIcon;
        _tray.Icon = AppIconHelper.CloneTrayIcon();
        _tray.DoubleClick += (_, _) => { Show(); WindowState = WindowState.Normal; Activate(); };
        _tray.ContextMenuStrip = BuildTray();
        Loaded += async (_, _) => await StartAsync();
    }

    private Forms.ContextMenuStrip BuildTray()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Открыть", null, (_, _) => { Show(); Activate(); });
        menu.Items.Add("Подключить VPN", null, async (_, _) => await ConnectVpnAsync());
        menu.Items.Add("Отключить VPN", null, async (_, _) => await Call(IpcMethods.DisconnectVpn));
        menu.Items.Add("Приостановить маршрутизацию", null, async (_, _) => await Call(IpcMethods.PauseRouting));
        menu.Items.Add("Аварийное восстановление", null, async (_, _) => await Call(IpcMethods.EmergencyRestore));
        menu.Items.Add("Выход", null, (_, _) => { _exit = true; _tray.Visible = false; System.Windows.Application.Current.Shutdown(); });
        return menu;
    }

    private async Task StartAsync()
    {
        if (!await EnsurePortableBootstrapReadyAsync())
        {
            return;
        }

        await ContinueStartupAsync();
    }

    private async Task ContinueStartupAsync()
    {
        _config = await LoadConfigFromServiceOrDiskAsync();
        ApplyConfigToUi(_config);
        if (!_refreshLoopStarted)
        {
            _refreshLoopStarted = true;
            _ = RefreshLoop();
        }

        await RefreshAsync(force: true);
    }

    private async Task<bool> EnsurePortableBootstrapReadyAsync()
    {
        PortableBootstrapStatus status = PortableBootstrapUi.ReadStatus();
        if (status.BootstrapState == PortableBootstrapState.Ready)
        {
            BootstrapOverlay.Visibility = Visibility.Collapsed;
            _bootstrapGateActive = false;
            return true;
        }

        _bootstrapGateActive = true;
        BootstrapOverlay.Visibility = Visibility.Visible;
        BootstrapOverlayMessage.Text = status.BootstrapState switch
        {
            PortableBootstrapState.NeedsRepair or PortableBootstrapState.VersionMismatch
                => "Системные компоненты VPN Route требуют обновления для этой копии приложения.",
            PortableBootstrapState.Broken
                => status.Message,
            _
                => "Для работы требуется подготовить системные компоненты (служба и драйвер продукта).",
        };
        BootstrapActionButton.Content = status.BootstrapState is PortableBootstrapState.NeedsRepair
            or PortableBootstrapState.VersionMismatch
            ? "Обновить"
            : "Подготовить";
        BootstrapActionButton.IsEnabled = status.BootstrapState != PortableBootstrapState.Broken
            || !status.DriverSigningBlocked;
        BootstrapOverlayDetail.Text = status.DriverSigningBlocked
            ? status.Message
            : status.Message + Environment.NewLine + "Потребуется одноразовое подтверждение UAC.";
        await Task.CompletedTask;
        return false;
    }

    private async void OnBootstrapAction(object sender, RoutedEventArgs e)
    {
        string root = PortableBootstrapUi.ResolvePortableRoot();
        try
        {
            BootstrapActionButton.IsEnabled = false;
            PortableBootstrapLaunchResult launch = await Task.Run(() => PortableBootstrapUi.LaunchElevatedRepair(root));
            if (launch.Outcome != PortableBootstrapLaunchOutcome.Ready)
            {
                BootstrapOverlayDetail.Text = launch.UserMessage;
                BootstrapActionButton.IsEnabled = launch.Outcome != PortableBootstrapLaunchOutcome.DriverSigningBlocked;
                return;
            }

            if (await EnsurePortableBootstrapReadyAsync())
            {
                await ContinueStartupAsync();
            }
        }
        catch (Exception ex)
        {
            BootstrapOverlayDetail.Text = ex.Message;
            BootstrapActionButton.IsEnabled = true;
        }
    }

    private async Task<AppConfiguration> LoadConfigFromServiceOrDiskAsync()
    {
        try
        {
            if (await _client.TryPingAsync(_cts.Token))
            {
                return await _client.SendOkAsync<AppConfiguration>(IpcMethods.GetConfig, null, _cts.Token)
                    ?? new AppConfiguration();
            }
        }
        catch (Exception)
        {
        }

        return ConfigSerializer.LoadOrDefault(AppPaths.ConfigFile);
    }

    private async Task RefreshLoop()
    {
        try
        {
            while (await _timer.WaitForNextTickAsync(_cts.Token))
            {
                await RefreshAsync();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RefreshAsync(bool force = false)
    {
        if (_bootstrapGateActive && !force)
        {
            return;
        }

        if (_connectUiActive && !force && !_workConnectPollActive)
        {
            return;
        }

        try
        {
            ServiceSnapshot? snap = await _client.SendOkAsync<ServiceSnapshot>(IpcMethods.GetStatus, null, _cts.Token);
            if (snap is null)
            {
                SetServiceUnavailable(true);
                return;
            }

            SetServiceUnavailable(false);
            _lastSnapshot = snap;
            bool vpnConnected = snap.VpnRoutingReady;
            UpdateHomeDashboard(snap, vpnConnected);
            UpdateAppsVpnState(snap);

            _allFlowRows.Clear();
            _allFlowRows.AddRange(
                FlowPresentationHelper.SelectUserFlows(snap.Flows).Select(f => new ConnectionFlowRow(f)));
            RefreshConnectionsBoard(snap);
            LogBox.Text = string.Join(Environment.NewLine, snap.Vpn.RecentLog);
            _tray.Text = vpnConnected ? AppBranding.ProductName + " — подключён" : AppBranding.ProductName + " — отключён";
            await RefreshTempRealAppStatusAsync();
            await ReconcileRoutingTraceAsync(pollEvents: _routingTracePhase == RoutingTraceUiPhase.Active);
        }
        catch (Exception)
        {
            SetServiceUnavailable(true);
        }
    }

    private void ApplyWorkVpnFeatureVisibility()
    {
        if (WorkVpnUiProjection.IsVisible)
        {
            return;
        }

        HomeWorkVpnCard.Visibility = Visibility.Collapsed;
        WorkVpnSettingsCard.Visibility = Visibility.Collapsed;
        SetupVpnCard.SetValue(Grid.ColumnSpanProperty, 3);
    }

    private void SetServiceUnavailable(bool unavailable)
    {
        ServiceBanner.Visibility = unavailable ? Visibility.Visible : Visibility.Collapsed;
        if (!unavailable) { return; }
        ServiceBannerText.Text = "Служба VPN Route недоступна.";
        HomeVpnStatusText.Text = "Служба недоступна";
        HomeVpnSubtitle.Text = "Запустите службу SelectiveVpnRouter и нажмите «Повторить».";
        HomeVpnStatusDot.Fill = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
    }

    private void UpdateBrowserIntegrationUi(ServiceSnapshot snap)
    {
        BrowserIntegrationUiPresentation.ViewModel vm =
            BrowserIntegrationUiPresentation.Map(snap.BrowserIntegration, DateTimeOffset.UtcNow);
        HomeBrowserExtensionText.Text = vm.ExtensionStatus;
        HomeBrowserApiText.Text = vm.BrowserApi;
        HomeBrowserProxyText.Text = vm.BrowserProxyStatus;
        HomeBrowserSocksText.Text = vm.SocksEndpoint;
        HomeBrowserVpnEgressText.Text = vm.VpnEgressStatus;
        HomeBrowserVpnInterfaceText.Text = vm.VpnInterface;
        HomeBrowserRuleCountText.Text = vm.RuleCount;
        HomeBrowserLastContactText.Text = vm.LastContact;
        if (string.IsNullOrWhiteSpace(vm.DetailLine))
        {
            HomeBrowserDetailText.Visibility = Visibility.Collapsed;
            HomeBrowserDetailText.Text = "";
        }
        else
        {
            HomeBrowserDetailText.Text = vm.DetailLine;
            HomeBrowserDetailText.Visibility = Visibility.Visible;
        }
    }

    private void UpdateHomePage(ServiceSnapshot snap, bool vpnConnected)
    {
        UpdateHomeWorkVpn(snap);
        bool connecting = !vpnConnected && snap.Vpn.Running;
        HomeVpnStatusText.Text = vpnConnected
            ? "VPN Route работает"
            : connecting ? "Подключение…" : "VPN Route выключен";
        HomeVpnStatusDot.Fill = new SolidColorBrush(vpnConnected
            ? Color.FromRgb(0x05, 0x96, 0x69)
            : connecting ? Color.FromRgb(0xF5, 0x9E, 0x0B) : Color.FromRgb(0x9C, 0xA3, 0xAF));
        HomeVpnSubtitle.Text = vpnConnected
            ? $"Маршрутизация активна · {snap.VpnAdapter?.Name ?? "VPN"} · proxy {(snap.ProxyPort?.ToString() ?? "—")}"
            : connecting
                ? "OpenVPN запускается, ожидаем готовность маршрутизации…"
                : "Приложения используют обычное подключение";
        HomeProfileText.Text = string.IsNullOrWhiteSpace(ProfileBox.Text) ? "—" : System.IO.Path.GetFileNameWithoutExtension(ProfileBox.Text);
        HomeDirectAdapterText.Text = snap.DirectAdapter?.Name ?? "—";
        HomeVpnAdapterText.Text = vpnConnected ? snap.VpnAdapter?.Name ?? "—" : "—";
        HomeAppsCountText.Text = ApplicationRulesHelper.CountVpnRoutedApplications(_appRules.Select(r => r.ToRule())).ToString();
        HomeDriverDot.Fill = new SolidColorBrush(snap.DriverLoaded ? Color.FromRgb(0x05, 0x96, 0x69) : Color.FromRgb(0x9C, 0xA3, 0xAF));
        HomeDriverText.Text = snap.DriverLoaded ? "Загружен" : "Не загружен";
        ConnectVpnButton.Visibility = vpnConnected ? Visibility.Collapsed : Visibility.Visible;
        DisconnectVpnButton.Visibility = vpnConnected ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateHomeWorkVpn(ServiceSnapshot snap)
    {
        if (!WorkVpnUiProjection.IsVisible)
        {
            return;
        }

        WorkVpnLiveStatus work = snap.WorkVpn;
        bool ready = work.WorkVpnReady;
        bool connecting = work.State is WorkVpnSessionState.Connecting
            or WorkVpnSessionState.WaitingForMfa
            or WorkVpnSessionState.WaitingForCredentials;
        HomeWorkVpnStatusText.Text = ready
            ? "Подключён"
            : work.State == WorkVpnSessionState.WaitingForMfa || work.WaitingForMfa
                ? "Ожидание MFA…"
                : work.State == WorkVpnSessionState.WaitingForCredentials
                    ? "Ожидание учётных данных…"
                : connecting ? "Подключение…" : work.State == WorkVpnSessionState.Failed ? "Ошибка подключения" : "Отключён";
        HomeWorkVpnStatusDot.Fill = new SolidColorBrush(ready
            ? Color.FromRgb(0x05, 0x96, 0x69)
            : work.WaitingForMfa || connecting
                ? Color.FromRgb(0xF5, 0x9E, 0x0B)
                : work.State == WorkVpnSessionState.Failed
                    ? Color.FromRgb(0xDC, 0x26, 0x26)
                    : Color.FromRgb(0x9C, 0xA3, 0xAF));
        HomeWorkVpnSubtitle.Text = work.WaitingForMfa
            ? "Ожидание подтверждения второго фактора…"
            : work.State == WorkVpnSessionState.Failed && !string.IsNullOrWhiteSpace(work.LastError)
                ? work.LastError
            : connecting
                ? "OpenVPN запускается, ожидаем ответ сервера…"
                : "Корпоративный split-tunnel (server push routes)";
        string profile = work.ProfilePath ?? WorkProfileBox.Text;
        HomeWorkProfileText.Text = string.IsNullOrWhiteSpace(profile) ? "—" : System.IO.Path.GetFileNameWithoutExtension(profile);
        HomeWorkAddressText.Text = work.Address ?? "—";
        bool inFlight = connecting || work.State is WorkVpnSessionState.WaitingForMfa or WorkVpnSessionState.WaitingForCredentials;
        ConnectWorkVpnButton.Visibility = ready || inFlight ? Visibility.Collapsed : Visibility.Visible;
        DisconnectWorkVpnButton.Visibility = ready || inFlight ? Visibility.Visible : Visibility.Collapsed;
        DisconnectWorkVpnButton.Content = inFlight && !ready ? "Отменить" : "Отключить";
    }

    private void UpdateHomeDashboard(ServiceSnapshot snap, bool vpnConnected)
    {
        UpdateHomePage(snap, vpnConnected);
        UpdateBrowserIntegrationUi(snap);
        HomeVpnAddressText.Text = snap.VpnAdapter?.Ipv4.FirstOrDefault() ?? "—";
        DateTimeOffset activityCutoff = DateTimeOffset.UtcNow.AddMinutes(-5);
        var recentUserFlows = FlowPresentationHelper.SelectUserFlows(snap.Flows, 100)
            .Where(f => f.UpdatedAt >= activityCutoff)
            .ToList();
        HomeRoutingActiveCountText.Text = _appRules.Count(row =>
            recentUserFlows.Any(flow => ApplicationRulesHelper.PathsEqual(row.ExePath, flow.ProcessPath))).ToString();
        HomeRoutingDirectCountText.Text = _appRules.Count(r => r.Mode == RouteMode.Direct).ToString();
        HomeRoutingStatusText.Text = vpnConnected
            ? (snap.TransparentRedirectActive ? "Маршрутизация активна" : "Подключено (ожидание перенаправления)")
            : "Не активна";
        HomeActiveAppsList.ItemsSource = _appRules.Take(8).ToList();
        HomeRecentFlowsGrid.ItemsSource = _allFlowRows.Take(10).ToList();
    }

    private void UpdateAppsVpnState(ServiceSnapshot snap)
    {
        bool vpnConnected = snap.VpnRoutingReady;
        IEnumerable<FlowEvent> activeFlows = FlowPresentationHelper.SelectUserFlows(snap.Flows)
            .Where(f => !FlowStatusHelper.IsTerminal(f.Status)
                        || f.Status is FlowLifecycle.Connected or FlowLifecycle.Relaying);
        foreach (ApplicationRuleRow row in _appRules)
        {
            row.VpnConnected = vpnConnected;
            row.UpdateRuntimeTraffic(activeFlows);
        }
    }

    private void RefreshAppsFilter()
    {
        RouteMode? modeFilter = null;
        if (AppFilterVpn.IsChecked == true)
        {
            modeFilter = RouteMode.Vpn;
        }
        else if (AppFilterDirect.IsChecked == true)
        {
            modeFilter = RouteMode.Direct;
        }

        HashSet<Guid> filteredIds = ApplicationRulesHelper.FilterApplicationRules(
                _appRules.Select(r => r.ToRule()),
                AppSearchBox.Text,
                modeFilter)
            .Select(r => r.Id)
            .ToHashSet();

        _filteredAppRules.Clear();
        foreach (ApplicationRuleRow row in _appRules.Where(r => filteredIds.Contains(r.Id)))
        {
            _filteredAppRules.Add(row);
        }

        AppsEmptyPanel.Visibility = _appRules.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnAppFilterChanged(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton clicked && clicked.IsChecked == true)
        {
            if (clicked != AppFilterAll)
            {
                AppFilterAll.IsChecked = false;
            }

            if (clicked != AppFilterVpn)
            {
                AppFilterVpn.IsChecked = false;
            }

            if (clicked != AppFilterDirect)
            {
                AppFilterDirect.IsChecked = false;
            }
        }
        else if (AppFilterAll.IsChecked != true && AppFilterVpn.IsChecked != true && AppFilterDirect.IsChecked != true)
        {
            AppFilterAll.IsChecked = true;
        }

        RefreshAppsFilter();
    }

    private void RefreshConnectionsBoard(ServiceSnapshot? snap = null)
    {
        snap ??= _lastSnapshot;
        if (snap is null)
        {
            return;
        }

        PackagedRoutingTargetIndex? packagedIndex = OperatingSystem.IsWindows()
            ? PackagedRoutingTargetIndex.Build(_config.Rules, new WindowsPackagedApplicationPathResolver())
            : null;
        ConnectionsBoardProjection board = ConnectionUxProjection.Build(
            snap.Flows,
            snap.VpnRoutingReady,
            GetConnectionRouteFilterKind(),
            FlowSearchBox.Text,
            packagedRoutingIndex: packagedIndex);

        ConnectionsRoutingStatusText.Text = board.RoutingStatusLine;
        ConnectionsSummaryText.Text = board.Summary.ApplicationsLine;
        ConnectionsDisconnectedPanel.Visibility = board.ShowDisconnectedEmptyState ? Visibility.Visible : Visibility.Collapsed;
        ConnectionsNoTrafficPanel.Visibility = board.ShowNoTrafficEmptyState ? Visibility.Visible : Visibility.Collapsed;
        ConnectionsListScroll.Visibility = board.VpnRoutingReady ? Visibility.Visible : Visibility.Collapsed;

        double scrollOffset = ConnectionsListScroll.VerticalOffset;

        _connectionGroups.Clear();
        _connectionRecentGroups.Clear();
        if (!board.VpnRoutingReady)
        {
            _selectedConnectionGroupKey = null;
            _selectedConnectionEndpointKey = null;
            SetConnectionDetailsPanelVisible(false);
            return;
        }

        foreach (ConnectionAppGroupProjection group in board.ActiveGroups)
        {
            ConnectionGroupViewModel vm = CreateConnectionGroupVm(group);
            _connectionGroups.Add(vm);
        }

        foreach (ConnectionAppGroupProjection group in board.RecentOnlyGroups)
        {
            ConnectionGroupViewModel vm = CreateConnectionGroupVm(group);
            _connectionRecentGroups.Add(vm);
        }

        ConnectionsListScroll.ScrollToVerticalOffset(scrollOffset);
        RestoreConnectionSelection();
    }

    private void SyncDiscoveryViewOptions()
    {
        _appDiscovery.ShowSystemAndServiceEntries = InstalledShowSystemCheckBox.IsChecked == true;
        _appDiscovery.ShowBackgroundProcesses = RunningShowBackgroundCheckBox.IsChecked == true;
    }

    private void UpdateInstalledAppsSearchPlaceholder()
    {
        InstalledAppsSearchPlaceholder.Visibility = string.IsNullOrWhiteSpace(InstalledAppsSearchBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void UpdateRunningAppsSearchPlaceholder()
    {
        RunningAppsSearchPlaceholder.Visibility = string.IsNullOrWhiteSpace(RunningAppsSearchBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void UpdateFlowSearchPlaceholder()
    {
        FlowSearchPlaceholder.Visibility = string.IsNullOrWhiteSpace(FlowSearchBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void SetConnectionDetailsPanelVisible(bool visible)
    {
        ConnectionDetailsPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        ConnectionsDetailGapColumn.Width = visible ? new GridLength(12) : new GridLength(0);
        ConnectionsDetailColumn.Width = visible ? new GridLength(0.33, GridUnitType.Star) : new GridLength(0);
    }

    private void RestoreConnectionSelection()
    {
        if (string.IsNullOrWhiteSpace(_selectedConnectionGroupKey))
        {
            return;
        }

        ConnectionGroupViewModel? groupVm = _connectionGroups.FirstOrDefault(g =>
                string.Equals(g.Projection.GroupKey, _selectedConnectionGroupKey, StringComparison.OrdinalIgnoreCase))
            ?? _connectionRecentGroups.FirstOrDefault(g =>
                string.Equals(g.Projection.GroupKey, _selectedConnectionGroupKey, StringComparison.OrdinalIgnoreCase));

        if (groupVm is null)
        {
            _selectedConnectionGroupKey = null;
            _selectedConnectionEndpointKey = null;
            SetConnectionDetailsPanelVisible(false);
            return;
        }

        if (ConnectionGroupsList.Items.Contains(groupVm))
        {
            ConnectionGroupsList.SelectedItem = groupVm;
        }
        else if (ConnectionRecentGroupsList.Items.Contains(groupVm))
        {
            ConnectionRecentGroupsList.SelectedItem = groupVm;
        }

        if (!string.IsNullOrWhiteSpace(_selectedConnectionEndpointKey))
        {
            ConnectionEndpointAggregateProjection? aggregate = groupVm.Projection.ActiveEndpointAggregates
                .FirstOrDefault(a => string.Equals(a.AggregateKey, _selectedConnectionEndpointKey, StringComparison.Ordinal));
            if (aggregate is not null)
            {
                ShowConnectionDetails(groupVm.Projection, aggregate);
            }
            else
            {
                ShowConnectionDetails(groupVm.Projection, null);
            }
        }
        else
        {
            ShowConnectionDetails(groupVm.Projection, null);
        }
    }

    private ConnectionGroupViewModel CreateConnectionGroupVm(ConnectionAppGroupProjection group)
    {
        bool expanded = _expandedConnectionGroups.Contains(group.GroupKey);
        ConnectionGroupViewModel vm = new(group, expanded);
        vm.PropertyChanged += OnConnectionGroupExpandedChanged;
        return vm;
    }

    private void OnConnectionGroupExpandedChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ConnectionGroupViewModel.IsExpanded) || sender is not ConnectionGroupViewModel vm)
        {
            return;
        }

        if (vm.IsExpanded)
        {
            _expandedConnectionGroups.Add(vm.Projection.GroupKey);
        }
        else
        {
            _expandedConnectionGroups.Remove(vm.Projection.GroupKey);
        }
    }

    private ConnectionRouteFilterKind GetConnectionRouteFilterKind()
    {
        if (FlowFilterErrors.IsChecked == true)
        {
            return ConnectionRouteFilterKind.Errors;
        }

        if (FlowFilterVpn.IsChecked == true)
        {
            return ConnectionRouteFilterKind.Vpn;
        }

        if (FlowFilterDirect.IsChecked == true)
        {
            return ConnectionRouteFilterKind.Direct;
        }

        if (FlowFilterMixed.IsChecked == true)
        {
            return ConnectionRouteFilterKind.Mixed;
        }

        return ConnectionRouteFilterKind.All;
    }

    private void OnFlowFilterChanged(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton clicked && clicked.IsChecked == true)
        {
            if (clicked == FlowFilterAll || clicked == FlowFilterVpn || clicked == FlowFilterDirect || clicked == FlowFilterMixed)
            {
                if (clicked != FlowFilterAll) { FlowFilterAll.IsChecked = false; }
                if (clicked != FlowFilterVpn) { FlowFilterVpn.IsChecked = false; }
                if (clicked != FlowFilterDirect) { FlowFilterDirect.IsChecked = false; }
                if (clicked != FlowFilterMixed) { FlowFilterMixed.IsChecked = false; }
                FlowFilterErrors.IsChecked = false;
            }
            else if (clicked == FlowFilterErrors)
            {
                FlowFilterAll.IsChecked = false;
                FlowFilterVpn.IsChecked = false;
                FlowFilterDirect.IsChecked = false;
                FlowFilterMixed.IsChecked = false;
            }
        }
        else if (FlowFilterAll.IsChecked != true
                 && FlowFilterVpn.IsChecked != true
                 && FlowFilterDirect.IsChecked != true
                 && FlowFilterMixed.IsChecked != true
                 && FlowFilterErrors.IsChecked != true)
        {
            FlowFilterAll.IsChecked = true;
        }

        RefreshConnectionsBoard();
    }

    private void OnConnectionGroupSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not System.Windows.Controls.ListView list)
        {
            return;
        }

        if (list.SelectedItem is not ConnectionGroupViewModel group)
        {
            if (ReferenceEquals(sender, ConnectionGroupsList) || ReferenceEquals(sender, ConnectionRecentGroupsList))
            {
                _selectedConnectionGroupKey = null;
                _selectedConnectionEndpointKey = null;
                SetConnectionDetailsPanelVisible(false);
            }

            return;
        }

        if (ReferenceEquals(sender, ConnectionGroupsList) && ConnectionRecentGroupsList.SelectedItem is not null)
        {
            ConnectionRecentGroupsList.SelectedItem = null;
        }
        else if (ReferenceEquals(sender, ConnectionRecentGroupsList) && ConnectionGroupsList.SelectedItem is not null)
        {
            ConnectionGroupsList.SelectedItem = null;
        }

        _selectedConnectionGroupKey = group.Projection.GroupKey;
        _selectedConnectionEndpointKey = null;
        SetConnectionDetailsPanelVisible(true);
        ShowConnectionDetails(group.Projection, null);
    }

    private void OnConnectionEndpointSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not System.Windows.Controls.ListView list
            || list.DataContext is not ConnectionGroupViewModel groupVm
            || list.SelectedItem is not ConnectionEndpointAggregateProjection aggregate)
        {
            return;
        }

        _selectedConnectionGroupKey = groupVm.Projection.GroupKey;
        _selectedConnectionEndpointKey = aggregate.AggregateKey;
        if (ConnectionGroupsList.SelectedItem != groupVm)
        {
            ConnectionGroupsList.SelectedItem = groupVm;
        }

        SetConnectionDetailsPanelVisible(true);
        ShowConnectionDetails(groupVm.Projection, aggregate);
    }

    private void ShowConnectionDetails(ConnectionAppGroupProjection p, ConnectionEndpointAggregateProjection? aggregate)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Приложение: " + p.ApplicationName);
        sb.AppendLine("Путь: " + p.ProcessPath);
        sb.AppendLine("Маршрут: " + p.RouteSummaryLabel + (string.IsNullOrWhiteSpace(p.MixedBreakdown) ? "" : " (" + p.MixedBreakdown + ")"));
        sb.AppendLine("Активных соединений: " + p.ActiveConnectionCount);
        sb.AppendLine("Состояние: " + p.StateSummary);
        if (p.FirstActivityUtc is not null)
        {
            sb.AppendLine("Первое событие: " + p.FirstActivityUtc.Value.ToLocalTime());
        }

        if (p.LastActivityUtc is not null)
        {
            sb.AppendLine("Последнее событие: " + p.LastActivityUtc.Value.ToLocalTime());
        }

        ConnectionFlowProjection? sample = aggregate?.SampleFlow
            ?? p.ActiveFlows.FirstOrDefault()
            ?? p.RecentFlows.FirstOrDefault();

        if (aggregate is not null)
        {
            sb.AppendLine();
            sb.AppendLine("Агрегированная строка");
            sb.AppendLine("Удалённый адрес: " + aggregate.RemoteEndpoint);
            sb.AppendLine("Количество: " + aggregate.Count);
            sb.AppendLine("Маршрут: " + aggregate.RouteLabel);
            sb.AppendLine("Состояние: " + aggregate.DisplayStateLabel);
        }

        if (sample is not null)
        {
            sb.AppendLine();
            sb.AppendLine(aggregate is null ? "Пример соединения" : "Пример из группы");
            sb.AppendLine("Удалённый: " + sample.RemoteEndpoint);
            sb.AppendLine("PID: " + sample.Pid);
            sb.AppendLine("Маршрут: " + sample.RouteLabel);
            sb.AppendLine("Состояние: " + sample.DisplayStateLabel + " (" + sample.RawStatus + ")");
            sb.AppendLine("Proxy: redirect=" + sample.WfpRedirect + " accepted=" + sample.ProxyAccepted);
            if (!string.IsNullOrWhiteSpace(sample.LocalEndpoint))
            {
                sb.AppendLine("Локальный: " + sample.LocalEndpoint);
            }
        }

        ConnectionDetailsText.Text = sb.ToString().TrimEnd();
    }

    private void OnNavigateApps(object sender, RoutedEventArgs e) => MainTabs.SelectedIndex = 1;

    private void OnNavigateConnections(object sender, RoutedEventArgs e) => MainTabs.SelectedIndex = 2;

    private void AppendDiagResult(string line)
    {
        _lastDiagRaw = line;
        string display = DiagnosticDisplayFormatter.FormatForUi(line);
        DiagResults.Items.Insert(0, new DiagJournalEntry(line, display));
        DiagInnerTabs.SelectedIndex = 0;
        DiagResultDetailBox.Text = display;
        DiagResultDetailBox.TextWrapping = TextWrapping.Wrap;
        DiagResultDetailBox.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        DiagResultDetailBox.ScrollToHome();

        bool pass = line.StartsWith("PASS", StringComparison.OrdinalIgnoreCase);
        bool fail = line.StartsWith("FAIL", StringComparison.OrdinalIgnoreCase);
        bool warn = line.StartsWith("WARNING", StringComparison.OrdinalIgnoreCase);
        DiagResultHeadline.Text = pass ? "✓ PASS" : fail ? "✗ FAIL" : warn ? "⚠ WARNING" : "Результат последней проверки";
        DiagResultPanel.BorderBrush = pass
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x15, 0x80, 0x3D))
            : fail
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xB9, 0x1C, 0x1C))
                : warn
                    ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xB4, 0x53, 0x09))
                    : System.Windows.Media.Brushes.LightGray;
        DiagResultPanel.BorderThickness = pass || fail || warn ? new Thickness(2) : new Thickness(1);
    }

    private static bool IsDiagnosticPass(string? outcome) =>
        string.Equals(outcome, DiagnosticOutcomes.Pass, StringComparison.OrdinalIgnoreCase);

    private async void OnRunBasicDiagnostics(object sender, RoutedEventArgs e)
    {
        string[] tests = BasicDiagnosticsOrchestration.SafeSteps;

        int success = 0;
        foreach (string name in tests)
        {
            try
            {
                DiagnosticResult? result = await _client.SendOkAsync<DiagnosticResult>(
                    IpcMethods.RunDiagnostic,
                    new { name, confirm = false },
                    _cts.Token);
                if (result is null)
                {
                    AppendDiagResult($"FAIL  {name}: no response");
                    continue;
                }

                AppendDiagResult($"{result.Outcome}  {result.Name}: {result.Message}");
                if (IsDiagnosticPass(result.Outcome))
                {
                    success++;
                }
            }
            catch (Exception ex)
            {
                AppendDiagResult($"FAIL  {name}: {ex.Message}");
            }
        }

        int failures = tests.Length - success;
        BasicDiagSummaryText.Text = failures == 0
            ? $"✓ {success}/{tests.Length} проверок успешно"
            : $"⚠ {success}/{tests.Length} — {failures} проблем(а)";
        if (failures > 0)
        {
            DiagInnerTabs.SelectedIndex = 0;
        }
    }

    private async void OnRetryService(object sender, RoutedEventArgs e)
    {
        _config = await LoadConfigFromServiceOrDiskAsync();
        ApplyConfigToUi(_config);
        await RefreshAsync(force: true);
    }

    private async Task AddApplicationRuleAsync(string exePath)
    {
        ApplicationRuleAddResult result = ApplicationRulesHelper.TryAddApplicationRule(_config, exePath);
        if (result.IsDuplicate) { MessageBox.Show("Это приложение уже добавлено.", AppBranding.ProductName); return; }
        if (!result.Ok || result.Config is null || result.Rule is null) { MessageBox.Show(result.Error ?? "Error", AppBranding.ProductName); return; }
        await SaveConfigAsync(result.Config);
        _config = result.Config;
        _appRules.Add(new ApplicationRuleRow(result.Rule, await IsVpnConnectedAsync()));
        RefreshAppsFilter();
        await RefreshDiscoveryAfterRulesChangedAsync();
    }

    private IEnumerable<RoutingRule> GetPermanentApplicationRulesForDiscovery() =>
        ApplicationRulesHelper.GetPermanentApplicationRules(_config);

    private async void OnAppsInnerTabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, AppsInnerTabControl))
        {
            return;
        }

        if (AppsInnerTabControl.SelectedIndex == 1 && !_installedDiscoveryLoaded)
        {
            _installedDiscoveryLoaded = true;
            await RefreshInstalledAppsAsync(forceRescan: true);
        }
        else if (AppsInnerTabControl.SelectedIndex == 2)
        {
            await RefreshRunningAppsAsync();
        }
    }

    private async void OnRefreshInstalledApps(object sender, RoutedEventArgs e) =>
        await RefreshInstalledAppsAsync(forceRescan: true);

    private async void OnRefreshRunningApps(object sender, RoutedEventArgs e) =>
        await RefreshRunningAppsAsync();

    private async void OnInstalledAppsSearchChanged(object sender, TextChangedEventArgs e)
    {
        UpdateInstalledAppsSearchPlaceholder();
        await RefreshInstalledAppsAsync(forceRescan: false);
    }

    private async void OnRunningAppsSearchChanged(object sender, TextChangedEventArgs e)
    {
        UpdateRunningAppsSearchPlaceholder();
        await RefreshRunningAppsAsync();
    }

    private async void OnInstalledDiscoveryOptionsChanged(object sender, RoutedEventArgs e)
    {
        SyncDiscoveryViewOptions();
        await RefreshInstalledAppsAsync(forceRescan: true);
    }

    private async void OnRunningDiscoveryOptionsChanged(object sender, RoutedEventArgs e)
    {
        SyncDiscoveryViewOptions();
        await RefreshRunningAppsAsync();
    }

    private void OnInstalledAppsSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateDiscoverySelectionBars();

    private void OnRunningAppsSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateDiscoverySelectionBars();

    private async void OnAddInstalledSelectionVpn(object sender, RoutedEventArgs e) =>
        await AddDiscoveredSelectionAsync(_appDiscovery.GetSelectedInstalled(), RouteMode.Vpn);

    private async void OnAddInstalledSelectionDirect(object sender, RoutedEventArgs e) =>
        await AddDiscoveredSelectionAsync(_appDiscovery.GetSelectedInstalled(), RouteMode.Direct);

    private async void OnAddRunningSelectionVpn(object sender, RoutedEventArgs e) =>
        await AddDiscoveredSelectionAsync(_appDiscovery.GetSelectedRunning(), RouteMode.Vpn);

    private async void OnAddRunningSelectionDirect(object sender, RoutedEventArgs e) =>
        await AddDiscoveredSelectionAsync(_appDiscovery.GetSelectedRunning(), RouteMode.Direct);

    private async Task RefreshInstalledAppsAsync(bool forceRescan)
    {
        try
        {
            SyncDiscoveryViewOptions();
            await _appDiscovery.RefreshInstalledAsync(
                GetPermanentApplicationRulesForDiscovery(),
                InstalledAppsSearchBox.Text,
                forceRescan);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, AppBranding.ProductName);
        }

        UpdateDiscoverySelectionBars();
    }

    private async Task RefreshRunningAppsAsync()
    {
        try
        {
            SyncDiscoveryViewOptions();
            await _appDiscovery.RefreshRunningAsync(
                GetPermanentApplicationRulesForDiscovery(),
                RunningAppsSearchBox.Text);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, AppBranding.ProductName);
        }

        UpdateDiscoverySelectionBars();
    }

    private async Task AddDiscoveredSelectionAsync(IReadOnlyList<DiscoveredApplicationRow> selected, RouteMode mode)
    {
        if (selected.Count == 0)
        {
            return;
        }

        ApplicationDiscoveryBatchAddResult batch = ApplicationDiscoveryCatalog.AddSelectedRules(
            _config,
            selected.Select(r => r.Application),
            mode);
        if (batch.AddedCount == 0)
        {
            MessageBox.Show("Выбранные приложения уже настроены или не удалось добавить.", AppBranding.ProductName);
            return;
        }

        await SaveConfigAsync(batch.Config);
        _config = batch.Config;
        bool vpnConnected = await IsVpnConnectedAsync();
        foreach (RoutingRule rule in batch.AddedRules)
        {
            _appRules.Add(new ApplicationRuleRow(rule, vpnConnected));
        }

        RefreshAppsFilter();
        _appDiscovery.ClearInstalledSelection();
        _appDiscovery.ClearRunningSelection();
        await RefreshDiscoveryAfterRulesChangedAsync();
        UpdateDiscoverySelectionBars();
    }

    private async Task RefreshDiscoveryAfterRulesChangedAsync()
    {
        if (_installedDiscoveryLoaded)
        {
            await RefreshInstalledAppsAsync(forceRescan: false);
        }

        if (AppsInnerTabControl.SelectedIndex == 2)
        {
            await RefreshRunningAppsAsync();
        }
    }

    private void UpdateDiscoveryLoadingUi()
    {
        InstalledAppsLoadingText.Visibility = _appDiscovery.IsInstalledLoading ? Visibility.Visible : Visibility.Collapsed;
        RunningAppsLoadingText.Visibility = _appDiscovery.IsRunningLoading ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateDiscoverySelectionBars()
    {
        int installedCount = _appDiscovery.GetSelectedInstalled().Count;
        int runningCount = _appDiscovery.GetSelectedRunning().Count;
        InstalledAppsSelectionText.Text = $"Выбрано: {installedCount}";
        RunningAppsSelectionText.Text = $"Выбрано: {runningCount}";
        InstalledAppsActionBar.Visibility = installedCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        RunningAppsActionBar.Visibility = runningCount > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void OnAppRouteToggle(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton toggle || toggle.DataContext is not ApplicationRuleRow row) { return; }
        RouteMode newMode = string.Equals(toggle.Tag as string, "Direct", StringComparison.OrdinalIgnoreCase) ? RouteMode.Direct : RouteMode.Vpn;
        if (row.Mode == newMode) { return; }
        AppConfiguration? updated = ApplicationRulesHelper.TrySetApplicationRouteMode(_config, row.Id, newMode);
        if (updated is null) { return; }
        await SaveConfigAsync(updated);
        row.Mode = newMode;
    }

    private async void OnRemoveAppRule(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Guid id }) { return; }
        ApplicationRuleRow? row = _appRules.FirstOrDefault(r => r.Id == id);
        if (row is null) { return; }
        AppConfiguration? updated = ApplicationRulesHelper.TryRemoveApplicationRule(_config, id);
        if (updated is null) { return; }
        await SaveConfigAsync(updated);
        _appRules.Remove(row);
        RefreshAppsFilter();
        _ = RefreshDiscoveryAfterRulesChangedAsync();
    }

    private void ApplyConfigToUi(AppConfiguration cfg)
    {
        ExeBox.Text = cfg.Vpn.OpenVpnPath;
        ProfileBox.Text = cfg.Vpn.ProfilePath;
        if (WorkVpnUiProjection.IsVisible)
        {
            WorkVpnSettings work = WorkVpnProfileDefaults.WithMigrationDefaults(cfg.WorkVpn);
            WorkVpnEnabledBox.IsChecked = work.Enabled;
            WorkProfileBox.Text = work.ProfilePath;
            WorkUserBox.Text = work.Username ?? "";
            WorkRememberUserBox.IsChecked = work.RememberUsername;
        }
        DcoBox.IsChecked = cfg.Vpn.CompatibilityDisableDco;
        QuicBox.IsChecked = cfg.Vpn.BlockQuicForVpnApps;
        Ipv6Box.SelectedIndex = cfg.Vpn.Ipv6Policy switch
        {
            Ipv6Policy.Auto => 0,
            Ipv6Policy.VpnIfAvailable => 1,
            Ipv6Policy.AllowDirect => 3,
            _ => 2,
        };
        _appRules.Clear();
        foreach (RoutingRule rule in ApplicationRulesHelper.GetPermanentApplicationRules(cfg)) { _appRules.Add(new ApplicationRuleRow(rule, false)); }
        _advancedRules.Clear();
        foreach (RoutingRule r in cfg.Rules.Where(r => r.Type != RuleType.Application)) { _advancedRules.Add(RuleRow.From(r)); }
        RefreshAppsFilter();
        RoutingTraceRuleCombo.ItemsSource = ApplicationRulesHelper.GetPermanentApplicationRules(cfg).ToList();
    }

    private AppConfiguration ReadConfigFromUi()
    {
        var vpn = new VpnProfileSettings
        {
            OpenVpnPath = ExeBox.Text.Trim(),
            ProfilePath = ProfileBox.Text.Trim(),
            CompatibilityDisableDco = DcoBox.IsChecked == true,
            BlockQuicForVpnApps = QuicBox.IsChecked == true,
            Ipv6Policy = Ipv6Box.SelectedIndex switch
            {
                0 => Ipv6Policy.Auto,
                1 => Ipv6Policy.VpnIfAvailable,
                3 => Ipv6Policy.AllowDirect,
                _ => Ipv6Policy.BlockForVpnRoutedApps,
            },
            PublicIpEndpoint = _config.Vpn.PublicIpEndpoint,
        };
        List<RoutingRule> diagnosticRules = _config.Rules
            .Where(ApplicationRulesHelper.IsDiagnosticApplicationRule)
            .ToList();
        WorkVpnSettings work = WorkVpnUiProjection.IsVisible
            ? new WorkVpnSettings
            {
                Enabled = WorkVpnEnabledBox.IsChecked == true,
                ProfilePath = WorkProfileBox.Text.Trim(),
                Username = string.IsNullOrWhiteSpace(WorkUserBox.Text) ? null : WorkUserBox.Text.Trim(),
                RememberUsername = WorkRememberUserBox.IsChecked == true,
            }
            : _config.WorkVpn;
        return new AppConfiguration
        {
            Vpn = vpn,
            WorkVpn = work,
            Rules = _appRules.Select(r => r.ToRule())
                .Concat(_advancedRules.Select(r => r.ToRule()))
                .Concat(diagnosticRules)
                .ToList(),
            Ui = _config.Ui,
        };
    }

    private async void OnStart(object sender, RoutedEventArgs e) => await ConnectVpnAsync();

    private async void OnStartWorkVpn(object sender, RoutedEventArgs e) => await ConnectWorkVpnAsync();

    private async void OnStopWorkVpn(object sender, RoutedEventArgs e) => await Call(IpcMethods.DisconnectWorkVpn);

    private async void OnStop(object sender, RoutedEventArgs e) => await Call(IpcMethods.DisconnectVpn);
    private async void OnPause(object sender, RoutedEventArgs e) => await Call(IpcMethods.PauseRouting);
    private async void OnEmergency(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Удалить маршруты, WFP-фильтры и управляемый OpenVPN этого приложения? Другие VPN не затрагиваются.",
                "Аварийное восстановление", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
        {
            return;
        }

        await Call(IpcMethods.EmergencyRestore);
    }

    private async void OnAddApp(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Applications (*.exe)|*.exe" };
        if (dlg.ShowDialog() != true) { return; }
        try { await AddApplicationRuleAsync(dlg.FileName); } catch (Exception ex) { MessageBox.Show(ex.Message, AppBranding.ProductName); }
    }

    private void OnAddDomain(object sender, RoutedEventArgs e)
    {
        string? host = Prompt("Домен (example.com или *.example.com). Действует для всех процессов.", "youtube.com");
        if (string.IsNullOrWhiteSpace(host))
        {
            return;
        }

        _advancedRules.Add(RuleRow.From(RoutingRule.Create(RuleType.Domain, host, host, RouteMode.Vpn)));
    }

    private void OnAddCidr(object sender, RoutedEventArgs e)
    {
        string? cidr = Prompt("IP или CIDR. Действует для всех процессов.", "1.2.3.0/24");
        if (string.IsNullOrWhiteSpace(cidr))
        {
            return;
        }

        _advancedRules.Add(RuleRow.From(RoutingRule.Create(RuleType.Cidr, cidr, cidr, RouteMode.Vpn)));
    }

    private void OnRemoveRule(object sender, RoutedEventArgs e)
    {
        if (RulesGrid.SelectedItem is RuleRow row)
        {
            _advancedRules.Remove(row);
        }
    }

    private async void OnSaveRules(object sender, RoutedEventArgs e) => await SaveConfigAsync();

    private async void OnSaveSetup(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(FirstAppBox.Text) && File.Exists(FirstAppBox.Text))
        {
            ApplicationRuleAddResult add = ApplicationRulesHelper.TryAddApplicationRule(_config, FirstAppBox.Text);
            if (add.Ok && add.Config is not null && add.Rule is not null)
            {
                _config = add.Config;
                _appRules.Add(new ApplicationRuleRow(add.Rule, await IsVpnConnectedAsync()));
                RefreshAppsFilter();
            }
        }

        await SaveConfigAsync();
        WizardHint.Text = "Настройки сохранены. Подключите VPN на главной вкладке.";
    }

    private async void OnAddGitDirect(object sender, RoutedEventArgs e)
    {
        if (_appRules.Any(r => r.ExePath.Equals("git.exe", StringComparison.OrdinalIgnoreCase))
            || _advancedRules.Any(r => r.Target.Equals("git.exe", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        RoutingRule rule = RoutingRule.Create(RuleType.Application, "git", "git.exe", RouteMode.Direct);
        AppConfiguration updated = _config with { Rules = _config.Rules.Concat([rule]).ToList() };
        await SaveConfigAsync(updated);
        _advancedRules.Add(RuleRow.From(rule));
    }

    private void OnBrowseExe(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "openvpn.exe|openvpn.exe", FileName = "openvpn.exe" };
        if (dlg.ShowDialog() == true)
        {
            ExeBox.Text = dlg.FileName;
        }
    }

    private void OnBrowseProfile(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "OpenVPN (*.ovpn)|*.ovpn" };
        if (dlg.ShowDialog() == true)
        {
            ProfileBox.Text = dlg.FileName;
        }
    }

    private void OnBrowseFirstApp(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Программы (*.exe)|*.exe" };
        if (dlg.ShowDialog() == true)
        {
            FirstAppBox.Text = dlg.FileName;
        }
    }

    private void OnBrowseRealApp(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Программы (*.exe)|*.exe" };
        if (dlg.ShowDialog() == true)
        {
            RealAppExeBox.Text = dlg.FileName;
        }
    }

    private async void OnApplyTempRealApp(object sender, RoutedEventArgs e)
    {
        string exe = RealAppExeBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(exe))
        {
            MessageBox.Show("Выберите EXE через «Обзор EXE».", "Selective VPN Router");
            return;
        }

        if (!File.Exists(exe))
        {
            MessageBox.Show("Файл не найден: " + exe, "Selective VPN Router");
            return;
        }

        if (!await IsVpnConnectedAsync())
        {
            MessageBox.Show("Сначала подключите VPN.", "Selective VPN Router");
            return;
        }

        try
        {
            TempAppVpnStatus status = await _client.SendOkAsync<TempAppVpnStatus>(
                IpcMethods.ApplyTempAppVpnRoute,
                new TempAppVpnRequest { ExePath = exe },
                _cts.Token) ?? new TempAppVpnStatus();
            ApplyTempRealAppStatus(status);
            if (!string.IsNullOrWhiteSpace(status.Error))
            {
                MessageBox.Show(status.Error, "Selective VPN Router");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Selective VPN Router");
        }
    }

    private async void OnRemoveTempRealApp(object sender, RoutedEventArgs e)
    {
        try
        {
            TempAppVpnStatus status = await _client.SendOkAsync<TempAppVpnStatus>(
                IpcMethods.RemoveTempAppVpnRoute,
                null,
                _cts.Token) ?? new TempAppVpnStatus();
            ApplyTempRealAppStatus(status);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Selective VPN Router");
        }
    }

    private async void OnRefreshTempRealApp(object sender, RoutedEventArgs e) => await RefreshTempRealAppStatusAsync(force: true);

    private void OnResolveRealAppTarget(object sender, RoutedEventArgs e) => ResolveRealAppTargetAddresses();

    private void OnRealAppTargetFilterChanged(object sender, RoutedEventArgs e) => ApplyRealAppFlowHistoryDisplay();

    private void ResolveRealAppTargetAddresses()
    {
        _resolvedTargetAddresses.Clear();
        if (!TargetEndpointParser.TryParse(RealAppTargetBox.Text.Trim(), out string host, out int port))
        {
            RealAppResolvedTargetsLine.Text = "Resolved: invalid target (use host:port)";
            return;
        }

        _targetPort = port;
        try
        {
            IPAddress[] addresses = Dns.GetHostAddresses(host);
            foreach (IPAddress address in addresses.Where(a => a.AddressFamily == AddressFamily.InterNetwork))
            {
                _resolvedTargetAddresses.Add(address.ToString());
            }

            RealAppResolvedTargetsLine.Text = _resolvedTargetAddresses.Count == 0
                ? $"Resolved: no IPv4 A-records for {host}"
                : "Resolved: " + string.Join(", ", _resolvedTargetAddresses);
        }
        catch (Exception ex)
        {
            RealAppResolvedTargetsLine.Text = "Resolved: DNS failed — " + ex.Message;
        }

        ApplyRealAppFlowHistoryDisplay();
        ApplyRealAppTargetStatus(_lastRealAppFlowDtos, DateTimeOffset.UtcNow);
    }

    private async Task RefreshTempRealAppStatusAsync(bool force = false)
    {
        if (_connectUiActive && !force && !_workConnectPollActive)
        {
            return;
        }

        try
        {
            string exe = RealAppExeBox.Text.Trim();
            Task<TempAppVpnStatus?> statusTask = _client.SendOkAsync<TempAppVpnStatus>(
                IpcMethods.GetTempAppVpnStatus,
                null,
                _cts.Token);
            Task<TempAppVpnFlowsResponse?> flowsTask = string.IsNullOrWhiteSpace(exe)
                ? Task.FromResult<TempAppVpnFlowsResponse?>(null)
                : _client.SendOkAsync<TempAppVpnFlowsResponse>(
                    IpcMethods.GetTempAppVpnFlows,
                    new TempAppVpnFlowsRequest { ExePath = exe, MaxCount = TempAppVpnFlowQuery.DefaultMaxCount },
                    _cts.Token);

            await Task.WhenAll(statusTask, flowsTask);
            TempAppVpnStatus? status = await statusTask;
            TempAppVpnFlowsResponse? flows = await flowsTask;
            if (status is not null)
            {
                if (string.IsNullOrWhiteSpace(exe) && !string.IsNullOrWhiteSpace(status.ExePath))
                {
                    flows = await _client.SendOkAsync<TempAppVpnFlowsResponse>(
                        IpcMethods.GetTempAppVpnFlows,
                        new TempAppVpnFlowsRequest { ExePath = status.ExePath, MaxCount = TempAppVpnFlowQuery.DefaultMaxCount },
                        _cts.Token);
                }

                ApplyTempRealAppStatus(status, flows);
            }
        }
        catch (Exception)
        {
            RealAppStatusLine.Text = "Статус: служба недоступна";
            RealAppStatusLine.Foreground = System.Windows.Media.Brushes.Gray;
            RealAppDetailLine.Text = string.Empty;
            RealAppAdvancedLine.Text = string.Empty;
            RealAppTargetStatusLine.Text = "Цель: —";
            RealAppQueriedAtLine.Text = string.Empty;
            _realAppFlows.Clear();
        }
    }

    private void ApplyTempRealAppStatus(TempAppVpnStatus status, TempAppVpnFlowsResponse? flows = null)
    {
        if (!string.IsNullOrWhiteSpace(status.ExePath))
        {
            RealAppExeBox.Text = status.ExePath;
        }

        if (!status.Active)
        {
            RealAppStatusLine.Text = "Статус: правило не активно";
            RealAppStatusLine.Foreground = System.Windows.Media.Brushes.Gray;
            RealAppDetailLine.Text = "Выберите EXE и включите временный VPN-маршрут.";
            RealAppAdvancedLine.Text = string.Empty;
            RealAppTargetStatusLine.Text = "Цель: —";
            RealAppQueriedAtLine.Text = string.Empty;
            _lastRealAppFlowDtos = [];
            _realAppFlows.Clear();
            return;
        }

        string policyLine =
            $"fileExists={status.FileExists} appIdResolved={status.AppIdResolved} " +
            $"filterInstalled={status.FilterInstalled} filterId={status.FilterId}";
        RealAppAdvancedLine.Text = policyLine + (status.Error is null ? "" : Environment.NewLine + status.Error);

        RealAppDetailLine.Text = status.Summary;
        switch (status.ObservationState)
        {
            case RealAppRoutingObservation.EgressVerified:
                RealAppStatusLine.Text = "Latest flow — TCP closed successfully (см. целевой статус ниже)";
                RealAppStatusLine.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x15, 0x80, 0x3D));
                break;
            case RealAppRoutingObservation.RoutingObserved:
                RealAppStatusLine.Text = "ROUTING OBSERVED — цепочка WFP→proxy→VPN создана, egress ещё не подтверждён";
                RealAppStatusLine.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x1D, 0x4E, 0xD8));
                break;
            case RealAppRoutingObservation.Warning:
                RealAppStatusLine.Text = "WARNING — VPN routing observed, но flow завершился с ошибкой";
                RealAppStatusLine.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xB4, 0x53, 0x09));
                break;
            case RealAppRoutingObservation.FlowError:
                RealAppStatusLine.Text = "FLOW ERROR — ошибка proxy/outbound без полной routing-цепочки";
                RealAppStatusLine.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xB9, 0x1C, 0x1C));
                break;
            case RealAppRoutingObservation.Partial:
                RealAppStatusLine.Text = "Частично — WFP redirect без полной цепочки proxy/VPN";
                RealAppStatusLine.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xB4, 0x53, 0x09));
                break;
            case RealAppRoutingObservation.Waiting:
                RealAppStatusLine.Text = "WAITING — правило активно, трафик приложения ещё не наблюдался";
                RealAppStatusLine.Foreground = System.Windows.Media.Brushes.Gray;
                break;
            default:
                if (!status.FilterInstalled || status.FilterId == 0)
                {
                    RealAppStatusLine.Text = "FAIL — WFP filter не установлен";
                    RealAppStatusLine.Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0xB9, 0x1C, 0x1C));
                }
                else
                {
                    RealAppStatusLine.Text = "Правило активно — ожидание трафика";
                    RealAppStatusLine.Foreground = System.Windows.Media.Brushes.Gray;
                }

                break;
        }

        if (!string.IsNullOrWhiteSpace(status.WfpLayerAudit))
        {
            RealAppAdvancedLine.Text = policyLine + Environment.NewLine + status.WfpLayerAudit +
                (status.Error is null ? "" : Environment.NewLine + status.Error);
        }

        RealAppFlowObservation? flow = status.SelectedFlow;
        if (flow is not null)
        {
            string errorLine = flow.ErrorDetails is null
                ? flow.Status
                : FlowStatusHelper.FormatErrorStatus(flow.ErrorDetails);
            RealAppDetailLine.Text =
                $"flow={flow.FlowId:N}  updated={flow.UpdatedAt:HH:mm:ss.fff}  queried={status.QueriedAt:HH:mm:ss.fff}{Environment.NewLine}" +
                $"Process PID={flow.Pid}  dest={flow.Destination}:{flow.Port}{Environment.NewLine}" +
                $"WFP redirected={flow.WfpRedirect}  proxy accepted={flow.ProxyAccepted}  redirect context={flow.RedirectRecordsApplied}{Environment.NewLine}" +
                $"vpnOutboundCreated={flow.VpnOutboundCreated} bound={flow.VpnOutboundBound} connected={flow.VpnOutboundConnected}{Environment.NewLine}" +
                $"Route={flow.Route}  local={flow.LocalInterface ?? "—"}  outboundLocal={flow.OutboundLocalEndpoint ?? "—"}{Environment.NewLine}" +
                $"routingObserved={flow.RoutingObserved}  tcpConnectSuccess={flow.TcpConnectSuccess}  egressVerified={flow.EgressVerified}{Environment.NewLine}" +
                errorLine +
                (string.IsNullOrWhiteSpace(status.RouteDiagnostic) ? "" : Environment.NewLine + status.RouteDiagnostic);
        }

        DateTimeOffset queriedAt = flows?.QueriedAt ?? status.QueriedAt;
        _lastRealAppFlowDtos = flows?.Flows ?? [];
        if (_resolvedTargetAddresses.Count == 0)
        {
            ResolveRealAppTargetAddresses();
        }
        else
        {
            ApplyRealAppFlowHistoryDisplay();
            ApplyRealAppTargetStatus(_lastRealAppFlowDtos, queriedAt);
        }
    }

    private void ApplyRealAppFlowHistoryDisplay()
    {
        _realAppFlows.Clear();
        IEnumerable<TempAppVpnFlowDto> source = _lastRealAppFlowDtos;
        if (RealAppTargetFilterOnly.IsChecked == true && _resolvedTargetAddresses.Count > 0)
        {
            source = source.Where(f => TargetFlowMatcher.MatchesTarget(f, _resolvedTargetAddresses, _targetPort));
        }

        IEnumerable<TempAppVpnFlowDto> ordered = source
            .OrderByDescending(f => _resolvedTargetAddresses.Count > 0
                && TargetFlowMatcher.MatchesTarget(f, _resolvedTargetAddresses, _targetPort))
            .ThenByDescending(f => f.UpdatedAt)
            .ThenByDescending(f => f.SequenceId);

        foreach (TempAppVpnFlowDto dto in ordered)
        {
            _realAppFlows.Add(RealAppFlowHistoryRow.FromDto(dto, _resolvedTargetAddresses, _targetPort));
        }
    }

    private void ApplyRealAppTargetStatus(IReadOnlyList<TempAppVpnFlowDto> flows, DateTimeOffset queriedAt)
    {
        RealAppQueriedAtLine.Text = $"QueriedAt: {queriedAt.ToLocalTime():HH:mm:ss.fff}";
        if (_resolvedTargetAddresses.Count == 0)
        {
            RealAppTargetStatusLine.Text = "Цель: resolve target addresses first";
            RealAppTargetStatusLine.Foreground = System.Windows.Media.Brushes.Gray;
            return;
        }

        TempAppVpnFlowDto? targetFlow = TargetFlowMatcher.SelectLatestTargetFlow(
            flows,
            _resolvedTargetAddresses,
            _targetPort);
        string state = TargetFlowAcceptance.ComputeState(targetFlow);
        RealAppTargetStatusLine.Text = TargetFlowAcceptance.FormatTargetStatusLine(state, targetFlow);
        RealAppTargetStatusLine.Foreground = state switch
        {
            TargetAcceptanceState.ClosedPass => new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x15, 0x80, 0x3D)),
            TargetAcceptanceState.Connected or TargetAcceptanceState.RoutingObserved => new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x1D, 0x4E, 0xD8)),
            TargetAcceptanceState.Error => new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xB9, 0x1C, 0x1C)),
            TargetAcceptanceState.Connecting => new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xB4, 0x53, 0x09)),
            _ => System.Windows.Media.Brushes.Gray,
        };
    }

    private void OnTestCenterScrollPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.OriginalSource is DependencyObject src && IsDescendantOf(src, RealAppFlowGrid))
        {
            return;
        }

        if (sender is ScrollViewer scrollViewer)
        {
            scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta);
            e.Handled = true;
        }
    }

    private static bool IsDescendantOf(DependencyObject? child, DependencyObject ancestor)
    {
        while (child != null)
        {
            if (child == ancestor)
            {
                return true;
            }

            child = VisualTreeHelper.GetParent(child);
        }

        return false;
    }

    private void OnClearDiagLog(object sender, RoutedEventArgs e) => DiagResults.Items.Clear();

    private void OnClearTestCenterResult(object sender, RoutedEventArgs e)
    {
        DiagResultDetailBox.Clear();
        DiagResultHeadline.Text = "Результат последней проверки";
        DiagResultPanel.BorderBrush = System.Windows.Media.Brushes.LightGray;
        DiagResultPanel.BorderThickness = new Thickness(1);
    }

    private void OnCopyTestCenterResult(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(DiagResultDetailBox.Text))
        {
            return;
        }

        System.Windows.Clipboard.SetText(string.IsNullOrEmpty(_lastDiagRaw) ? DiagResultDetailBox.Text : _lastDiagRaw);
    }

    private void OnExpandTestCenterResult(object sender, RoutedEventArgs e)
    {
        var wrapToggle = new System.Windows.Controls.CheckBox
        {
            Content = "Перенос строк",
            IsChecked = true,
            Margin = new Thickness(0, 0, 0, 8),
        };
        var viewer = new TextBox
        {
            Text = DiagResultDetailBox.Text,
            IsReadOnly = true,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            FontSize = 12,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        wrapToggle.Checked += (_, _) => ApplyDiagViewerWrap(viewer, true);
        wrapToggle.Unchecked += (_, _) => ApplyDiagViewerWrap(viewer, false);

        var root = new DockPanel { Margin = new Thickness(12) };
        DockPanel.SetDock(wrapToggle, Dock.Top);
        root.Children.Add(wrapToggle);
        root.Children.Add(viewer);

        var dlg = new Window
        {
            Title = "Результат диагностики",
            Width = 960,
            Height = 720,
            Owner = this,
            Content = root,
        };
        dlg.ShowDialog();
    }

    private static void ApplyDiagViewerWrap(TextBox viewer, bool wrap)
    {
        viewer.TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
        viewer.HorizontalScrollBarVisibility = wrap ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
    }

    private void OnCopyDiagLog(object sender, RoutedEventArgs e)
    {
        if (DiagResults.Items.Count == 0)
        {
            return;
        }

        string text = string.Join(
            Environment.NewLine + Environment.NewLine,
            DiagResults.Items.Cast<object>().Select(i => i switch
            {
                DiagJournalEntry entry => entry.Display,
                _ => i.ToString() ?? string.Empty,
            }));
        System.Windows.Clipboard.SetText(text);
    }

    private void OnCopyVpnLog(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(LogBox.Text))
        {
            return;
        }

        System.Windows.Clipboard.SetText(LogBox.Text);
    }

    private static readonly HashSet<string> ConfirmDiags =
    [
        "driver-install", "driver-start", "driver-stop", "driver-uninstall", "kill-service", "cleanup",
    ];

    private async void OnDiag(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string name })
        {
            return;
        }

        bool confirm = false;
        if (ConfirmDiags.Contains(name))
        {
            string extra = name == "kill-service"
                ? "Это завершит службу с правами администратора. Интернет «Напрямую» должен продолжить работать. После этого перезапустите службу. Параметры загрузки не меняются."
                : "Изменяется только состояние драйвера/службы. TESTSIGNING, Secure Boot, HVCI и BitLocker НЕ меняются.";
            if (MessageBox.Show(extra + "\n\nПродолжить: " + name + "?", "Подтверждение", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
            {
                AppendDiagResult("WARNING  " + name + ": отменено");
                return;
            }

            confirm = true;
        }

        try
        {
            DiagnosticResult? r = await _client.SendOkAsync<DiagnosticResult>(IpcMethods.RunDiagnostic, new { name, confirm }, _cts.Token);
            if (r is not null)
            {
                AppendDiagResult($"{r.Outcome}  {r.Name}: {r.Message}");
            }
        }
        catch (Exception ex)
        {
            AppendDiagResult("FAIL  " + name + ": " + ex.Message);
        }
    }

    private async Task SaveConfigAsync(AppConfiguration? config = null)
    {
        _config = config ?? ReadConfigFromUi();
        await _client.SendOkAsync<AppConfiguration>(IpcMethods.SetConfig, _config, _cts.Token);
        HomeAppsCountText.Text = ApplicationRulesHelper.CountVpnRoutedApplications(_appRules.Select(r => r.ToRule())).ToString();
    }

    private ConnectVpnRequest BuildConnectVpnRequestFromUi() =>
        new()
        {
            OpenVpnPath = ExeBox.Text.Trim(),
            ProfilePath = ProfileBox.Text.Trim(),
            DisableDco = DcoBox.IsChecked == true,
        };

    private ConnectWorkVpnRequest? BuildConnectWorkVpnRequestFromUi(string password)
    {
        if (WorkVpnEnabledBox.IsChecked != true)
        {
            return null;
        }

        string? user = string.IsNullOrWhiteSpace(WorkUserBox.Text) ? null : WorkUserBox.Text.Trim();
        if (user is null)
        {
            return null;
        }

        return new ConnectWorkVpnRequest
        {
            OpenVpnPath = ExeBox.Text.Trim(),
            ProfilePath = WorkProfileBox.Text.Trim(),
            Username = user,
            Password = password,
            DisableDco = DcoBox.IsChecked == true,
        };
    }

    private async Task ConnectWorkVpnAsync()
    {
        if (!WorkVpnUiProjection.IsVisible)
        {
            return;
        }

        if (WorkVpnEnabledBox.IsChecked != true)
        {
            MessageBox.Show("Включите рабочий VPN в настройках.", AppBranding.ProductName);
            return;
        }

        await SaveConfigAsync();
        if (!PromptWorkCredentials(out string password))
        {
            return;
        }

        ConnectWorkVpnRequest? request = BuildConnectWorkVpnRequestFromUi(password);
        if (request is null)
        {
            MessageBox.Show("Укажите имя пользователя для рабочего VPN.", AppBranding.ProductName);
            return;
        }

        try
        {
            await _client.SendOkAsync<ServiceSnapshot>(IpcMethods.ConnectWorkVpn, request, _cts.Token);
            _workConnectPollActive = true;
            await PollWorkVpnConnectAsync();
        }
        catch (Exception ex)
        {
            _workConnectPollActive = false;
            await RefreshAsync(force: true);
            MessageBox.Show(ex.Message, AppBranding.ProductName);
        }
    }

    private async Task PollWorkVpnConnectAsync()
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(WorkVpnConnectBudget.TotalOperationMs + 30_000);
        try
        {
            while (DateTime.UtcNow < deadline)
            {
                await RefreshAsync(force: true);
                WorkVpnLiveStatus? work = _lastSnapshot?.WorkVpn;
                if (work is null)
                {
                    break;
                }

                if (work.WorkVpnReady)
                {
                    return;
                }

                if (work.State == WorkVpnSessionState.Failed)
                {
                    if (!string.IsNullOrWhiteSpace(work.LastError))
                    {
                        MessageBox.Show(work.LastError, AppBranding.ProductName);
                    }

                    return;
                }

                if (work.State is WorkVpnSessionState.Disconnected && !work.Connected && work.LastError is not null)
                {
                    MessageBox.Show(work.LastError, AppBranding.ProductName);
                    return;
                }

                await Task.Delay(TimeSpan.FromSeconds(2), _cts.Token);
            }

            await RefreshAsync(force: true);
            if (_lastSnapshot?.WorkVpn.WorkVpnReady != true)
            {
                MessageBox.Show(
                    "Подключение рабочего VPN всё ещё выполняется или прервано. Проверьте статус на главной вкладке.",
                    AppBranding.ProductName);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _workConnectPollActive = false;
        }
    }

    private void OnBrowseWorkProfile(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "OpenVPN profile (*.ovpn)|*.ovpn" };
        if (dlg.ShowDialog() == true)
        {
            WorkProfileBox.Text = dlg.FileName;
        }
    }

    private bool PromptWorkCredentials(out string password)
    {
        password = "";
        var w = new Window
        {
            Title = "Рабочий VPN",
            Width = 420,
            Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
        };
        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(new TextBlock { Text = "Пароль (не сохраняется):", Margin = new Thickness(0, 0, 0, 4) });
        var pwd = new PasswordBox { Margin = new Thickness(0, 0, 0, 12) };
        panel.Children.Add(pwd);
        var buttons = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
        };
        var ok = new Button { Content = "Подключить", MinWidth = 100, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Отмена", MinWidth = 80, IsCancel = true };
        bool accepted = false;
        ok.Click += (_, _) => { accepted = true; w.DialogResult = true; };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        w.Content = panel;
        if (w.ShowDialog() != true || !accepted)
        {
            return false;
        }

        password = pwd.Password;
        return !string.IsNullOrEmpty(password);
    }

    private static void LogConnectStage(string stage)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.LocalAppData);
            string line = DateTimeOffset.Now.ToString("o") + " " + stage + Environment.NewLine;
            File.AppendAllText(Path.Combine(AppPaths.LocalAppData, "connect.log"), line);
        }
        catch (Exception)
        {
        }
    }

    private async Task ConnectVpnAsync()
    {
        if (_connectUiActive)
        {
            return;
        }

        SetConnectUiBusy(true);
        LogConnectStage("connect-click");
        ConnectVpnRequest request = BuildConnectVpnRequestFromUi();
        try
        {
            LogConnectStage("connect-request-send exe=" + request.OpenVpnPath + " profile=" + request.ProfilePath);
            await _client.SendOkAsync<ServiceSnapshot>(IpcMethods.ConnectVpn, request, _cts.Token);
            LogConnectStage("connect-response ok");
            await RefreshAsync(force: true);
        }
        catch (IpcTimeoutException)
        {
            LogConnectStage("connect-response timeout");
            await Task.Delay(TimeSpan.FromSeconds(3), _cts.Token);
            await RefreshAsync(force: true);
            if (await IsVpnConnectedAsync())
            {
                return;
            }

            MessageBox.Show(IpcTimeoutException.ConnectVpnUserMessage, "Selective VPN Router");
        }
        catch (InvalidOperationException ex) when (VpnTunnelNotReadyException.IsReadinessFailureMessage(ex.Message))
        {
            LogConnectStage("connect-response readiness-timeout");
            await RefreshAsync(force: true);
            MessageBox.Show(ex.Message, "VPN adapter not ready");
        }
        catch (Exception ex)
        {
            LogConnectStage("connect-response error=" + ex.Message);
            await RefreshAsync(force: true);
            MessageBox.Show(ex.Message, "Selective VPN Router");
        }
        finally
        {
            SetConnectUiBusy(false);
        }
    }

    private async Task Call(string method, object? payload = null)
    {
        try
        {
            await _client.SendOkAsync<ServiceSnapshot>(method, payload, _cts.Token);
            await RefreshAsync(force: true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Selective VPN Router");
        }
    }

    private async Task<bool> IsVpnConnectedAsync()
    {
        try
        {
            ServiceSnapshot? snap = await _client.SendOkAsync<ServiceSnapshot>(IpcMethods.GetStatus, null, _cts.Token);
            return snap?.VpnRoutingReady == true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void SetConnectUiBusy(bool busy)
    {
        _connectUiActive = busy;
        ConnectVpnButton.IsEnabled = !busy;
        DisconnectVpnButton.IsEnabled = !busy;
        if (busy)
        {
        }
    }

    private void OnClosing(object sender, CancelEventArgs e)
    {
        if (_exit)
        {
            _cts.Cancel();
            _tray.Visible = false;
            _tray.Dispose();
            return;
        }

        e.Cancel = true;
        Hide();
    }

    private void OnRoutingTraceRuleSelected(object sender, SelectionChangedEventArgs e)
    {
        if (RoutingTraceRuleCombo.SelectedItem is RoutingRule rule)
        {
            RoutingTraceTargetBox.Text = rule.Target;
        }
    }

    private void OnBrowseRoutingTraceTarget(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Executable|*.exe", Title = "Выберите приложение для мониторинга" };
        if (dlg.ShowDialog() == true)
        {
            RoutingTraceTargetBox.Text = dlg.FileName;
            RoutingTraceRuleCombo.SelectedItem = null;
        }
    }

    private async void OnStartRoutingTrace(object sender, RoutedEventArgs e)
    {
        try
        {
            Guid? ruleId = RoutingTraceRuleCombo.SelectedItem is RoutingRule r ? r.Id : null;
            string? exe = string.IsNullOrWhiteSpace(RoutingTraceTargetBox.Text) ? null : RoutingTraceTargetBox.Text.Trim();
            if (ruleId is null && string.IsNullOrWhiteSpace(exe))
            {
                MessageBox.Show(this, "???????? ??????? ??? EXE.", AppBranding.ProductName, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _routingTracePhase = RoutingTraceUiPhase.Starting;
            ApplyRoutingTraceControls(null);

            bool udpIpv6 = RoutingTraceIncludeUdpIpv6.IsChecked == true;
            var options = new RoutingTraceOptions
            {
                FollowChildProcesses = RoutingTraceFollowChildren.IsChecked == true,
                IncludeUdp = udpIpv6,
                IncludeIpv6 = udpIpv6,
            };
            RoutingTraceSession? session = await _client.SendOkAsync<RoutingTraceSession>(
                IpcMethods.StartRoutingTrace,
                new StartRoutingTraceRequest { RuleId = ruleId, ExecutablePath = exe, Options = options },
                _cts.Token);
            if (session is null)
            {
                _routingTracePhase = RoutingTraceUiPhase.Idle;
                ApplyRoutingTraceControls(null);
                return;
            }

            _routingTracePollSessionId = session.SessionId;
            _routingTraceAfterSequence = 0;
            _routingTraceRows.Clear();
            _routingTraceRowIndex.Clear();
            _routingTraceLastReport = string.Empty;
            _routingTracePhase = RoutingTraceUiPhase.Active;
            await ReconcileRoutingTraceAsync(pollEvents: true);
        }
        catch (Exception ex)
        {
            _routingTracePhase = RoutingTraceUiPhase.Idle;
            ApplyRoutingTraceControls(null);
            if (!IsBenignRoutingTraceError(ex))
            {
                MessageBox.Show(this, ex.Message, AppBranding.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private async void OnStopRoutingTrace(object sender, RoutedEventArgs e)
    {
        try
        {
            _routingTracePhase = RoutingTraceUiPhase.Stopping;
            ApplyRoutingTraceControls(null);
            RoutingTraceSession? session = await _client.SendOkAsync<RoutingTraceSession>(IpcMethods.StopRoutingTrace, null, _cts.Token);
            _routingTracePhase = RoutingTraceUiPhase.Completed;
            _routingTracePollSessionId = session?.SessionId;
            if (session is not null)
            {
                await RefreshRoutingTraceSnapshotAsync(session);
                try
                {
                    _routingTraceLastReport = await FetchRoutingTraceReportAsync("text");
                }
                catch
                {
                    _routingTraceLastReport = string.Empty;
                }
            }

            await ReconcileRoutingTraceAsync(pollEvents: false);
        }
        catch (Exception ex)
        {
            _routingTracePhase = RoutingTraceUiPhase.Idle;
            await ReconcileRoutingTraceAsync(pollEvents: false);
            if (!IsBenignRoutingTraceError(ex))
            {
                MessageBox.Show(this, ex.Message, AppBranding.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private async Task ReconcileRoutingTraceAsync(bool pollEvents)
    {
        RoutingTraceStatus? status = await _client.SendOkAsync<RoutingTraceStatus>(
            IpcMethods.GetRoutingTraceStatus,
            null,
            _cts.Token);
        if (status is null)
        {
            return;
        }

        if (status.Active)
        {
            _routingTracePhase = RoutingTraceUiPhase.Active;
            _routingTracePollSessionId = status.ActiveSessionId;
        }
        else if (_routingTracePhase is RoutingTraceUiPhase.Starting or RoutingTraceUiPhase.Stopping)
        {
            // keep transitional phase until caller settles
        }
        else if (status.HasCompletedSession)
        {
            _routingTracePhase = RoutingTraceUiPhase.Completed;
            _routingTracePollSessionId = status.CompletedSessionId;
        }
        else if (_routingTracePhase == RoutingTraceUiPhase.Active)
        {
            _routingTracePhase = RoutingTraceUiPhase.Idle;
            RoutingTraceStatusLine.Text = RoutingTraceUiText.MonitoringEndedNoActiveSession;
        }
        else if (_routingTracePhase != RoutingTraceUiPhase.Stopping)
        {
            _routingTracePhase = RoutingTraceUiPhase.Idle;
        }

        ApplyRoutingTraceControls(status);
        UpdateRoutingTraceStatusLine(status);

        if (pollEvents && status.Active && status.ActiveSessionId is Guid activeId)
        {
            await PollRoutingTraceEventsAsync(activeId);
        }
        else if (pollEvents && !status.Active && status.HasCompletedSession && status.CompletedSessionId is Guid completedId)
        {
            await PollRoutingTraceEventsAsync(completedId);
        }

        await RefreshRoutingTraceSnapshotAsync();
    }

    private void ApplyRoutingTraceControls(RoutingTraceStatus? status)
    {
        bool active = status?.Active == true || _routingTracePhase == RoutingTraceUiPhase.Active;
        bool completed = status?.HasCompletedSession == true || _routingTracePhase == RoutingTraceUiPhase.Completed;
        bool busy = _routingTracePhase is RoutingTraceUiPhase.Starting or RoutingTraceUiPhase.Stopping;

        RoutingTraceStartBtn.IsEnabled = !active && !busy;
        RoutingTraceStopBtn.IsEnabled = active && !busy;
        RoutingTraceBrowseBtn.IsEnabled = !active && !busy;
        RoutingTraceRuleCombo.IsEnabled = !active && !busy;
        RoutingTraceCopyBtn.IsEnabled = completed && !active;
        RoutingTraceExportBtn.IsEnabled = completed && !active;
    }

    private void UpdateRoutingTraceStatusLine(RoutingTraceStatus status)
    {
        if (status.Active && status.ActiveStartedAt is DateTimeOffset started)
        {
            TimeSpan elapsed = DateTimeOffset.UtcNow - started;
            RoutingTraceStatusLine.Text = RoutingTraceUiText.FormatActiveMonitoring(elapsed, status.ActiveTargetName);
            return;
        }

        if (status.HasCompletedSession)
        {
            DateTimeOffset completedStart = status.CompletedStartedAt ?? status.CompletedStoppedAt ?? DateTimeOffset.UtcNow;
            DateTimeOffset completedStop = status.CompletedStoppedAt ?? DateTimeOffset.UtcNow;
            TimeSpan duration = completedStop - completedStart;
            RoutingTraceStatusLine.Text = RoutingTraceUiText.FormatCompletedReport(duration, status.CompletedTargetName);
        }
        else if (_routingTracePhase == RoutingTraceUiPhase.Idle)
        {
            RoutingTraceStatusLine.Text = RoutingTraceUiText.MonitoringNotStarted;
        }
    }

    private async Task RefreshRoutingTraceSnapshotAsync(RoutingTraceSession? sessionOverride = null)
    {
        RoutingTraceSnapshot? snap = await _client.SendOkAsync<RoutingTraceSnapshot>(IpcMethods.GetRoutingTraceSnapshot, null, _cts.Token);
        RoutingTraceSession? session = sessionOverride ?? snap?.Session;
        if (session is null)
        {
            return;
        }

        RoutingTraceLiveCounters c = session.LiveCounters;
        RoutingTraceCountersLine.Text =
            $"TCP/IPv4 VPN {c.TcpIpv4Vpn}  Historical {c.TcpIpv4Historical}  NoProxy {c.TcpIpv4MissingProxy}  UNCOVERED UDP/4 {c.UdpIpv4Uncovered}  TCP/6 {c.TcpIpv6Uncovered}  UDP/6 {c.UdpIpv6Uncovered}  Leaks {c.ConfirmedLeaks}";
    }

    private async Task PollRoutingTraceEventsAsync(Guid sessionId)
    {
        try
        {
            RoutingTraceEventsPage? page = await _client.SendOkAsync<RoutingTraceEventsPage>(
                IpcMethods.GetRoutingTraceEvents,
                new GetRoutingTraceEventsRequest { SessionId = sessionId, AfterSequence = _routingTraceAfterSequence, Limit = 200 },
                _cts.Token);
            if (page is null)
            {
                return;
            }

            if (!page.SessionFound)
            {
                await ReconcileRoutingTraceAsync(pollEvents: false);
                return;
            }

            if (page.Events.Count > 0)
            {
                MergeRoutingTraceEvents(page.Events);
                _routingTraceAfterSequence = page.LastSequenceId;
            }
        }
        catch (Exception ex) when (IsBenignRoutingTraceError(ex))
        {
            await ReconcileRoutingTraceAsync(pollEvents: false);
        }
    }

    private void MergeRoutingTraceEvents(IReadOnlyList<RoutingTraceEvent> events)
    {
        foreach (RoutingTraceEvent ev in events)
        {
            if (ev.FlowId != Guid.Empty && _routingTraceRowIndex.TryGetValue(ev.FlowId, out RoutingTraceEventRow? existing))
            {
                int idx = _routingTraceRows.IndexOf(existing);
                var refreshed = new RoutingTraceEventRow(ev);
                if (idx >= 0)
                {
                    _routingTraceRows[idx] = refreshed;
                }
                _routingTraceRowIndex[ev.FlowId] = refreshed;
                continue;
            }

            var row = new RoutingTraceEventRow(ev);
            _routingTraceRows.Add(row);
            if (ev.FlowId != Guid.Empty)
            {
                _routingTraceRowIndex[ev.FlowId] = row;
            }
        }
    }

    private static bool IsBenignRoutingTraceError(Exception ex)
    {
        string message = ex.Message ?? string.Empty;
        return message.Contains("routing_trace_unavailable", StringComparison.OrdinalIgnoreCase)
            || message.Contains("No routing trace session", StringComparison.OrdinalIgnoreCase);
    }

    private void OnRoutingTraceRowSelected(object sender, SelectionChangedEventArgs e)
    {
        if (RoutingTraceGrid.SelectedItem is RoutingTraceEventRow row)
        {
            RoutingTraceDetailsLine.Text = row.Details;
        }
    }

    private async void OnCopyRoutingTraceReport(object sender, RoutedEventArgs e)
    {
        try
        {
            string report = string.IsNullOrWhiteSpace(_routingTraceLastReport)
                ? await FetchRoutingTraceReportAsync("text")
                : _routingTraceLastReport;
            System.Windows.Clipboard.SetText(report);
        }
        catch (Exception ex) when (!IsBenignRoutingTraceError(ex))
        {
            MessageBox.Show(this, ex.Message, AppBranding.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void OnExportRoutingTraceJson(object sender, RoutedEventArgs e)
    {
        try
        {
            string json = await FetchRoutingTraceReportAsync("json");
            var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "JSON|*.json", FileName = "routing-trace.json" };
            if (dlg.ShowDialog() == true)
            {
                File.WriteAllText(dlg.FileName, json);
            }
        }
        catch (Exception ex) when (!IsBenignRoutingTraceError(ex))
        {
            MessageBox.Show(this, ex.Message, AppBranding.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task<string> FetchRoutingTraceReportAsync(string format)
    {
        var payload = await _client.SendOkAsync<Dictionary<string, string>>(
            IpcMethods.ExportRoutingTrace,
            new { format },
            _cts.Token);
        return payload is not null && payload.TryGetValue("report", out string? report) ? report : string.Empty;
    }

    private static string? Prompt(string title, string placeholder)
    {
        var w = new Window
        {
            Title = title,
            Width = 480,
            Height = 140,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var box = new TextBox { Text = placeholder, Margin = new Thickness(12) };
        var ok = new Button { Content = "OK", Width = 80, Margin = new Thickness(12), IsDefault = true };
        string? result = null;
        ok.Click += (_, _) => { result = box.Text; w.DialogResult = true; };
        w.Content = new DockPanel { Children = { ok, box } };
        DockPanel.SetDock(ok, Dock.Bottom);
        return w.ShowDialog() == true ? result : null;
    }
}

internal sealed class DiagJournalEntry(string raw, string display)
{
    public string Raw { get; } = raw;
    public string Display { get; } = display;
    public override string ToString() => Display;
}

public sealed class RuleRow : INotifyPropertyChanged
{
    private bool _enabled = true;
    private string _name = "";
    private string _target = "";
    public Guid Id { get; set; }
    public RuleType Type { get; set; }
    public RouteMode Mode { get; set; }
    public string ModeDisplay => Mode == RouteMode.Direct ? "Напрямую" : "VPN";
    public string TypeDisplay => Type switch
    {
        RuleType.Application => "Приложение",
        RuleType.Domain => "Домен",
        RuleType.Cidr => "IP/CIDR",
        _ => Type.ToString(),
    };
    public bool Enabled { get => _enabled; set { _enabled = value; PropertyChanged?.Invoke(this, new(nameof(Enabled))); } }
    public string Name { get => _name; set { _name = value; PropertyChanged?.Invoke(this, new(nameof(Name))); } }
    public string Target { get => _target; set { _target = value; PropertyChanged?.Invoke(this, new(nameof(Target))); } }
    public event PropertyChangedEventHandler? PropertyChanged;

    public static RuleRow From(RoutingRule r) => new()
    {
        Id = r.Id,
        Enabled = r.Enabled,
        Type = r.Type,
        Name = r.Name,
        Target = r.Target,
        Mode = r.Mode,
    };

    public RoutingRule ToRule() => new()
    {
        Id = Id == Guid.Empty ? Guid.NewGuid() : Id,
        Enabled = Enabled,
        Type = Type,
        Name = Name,
        Target = Target,
        Mode = Mode,
    };
}
