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
using System.Windows.Media;
using Microsoft.Win32;
using SelectiveVpnRouter.Core;
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
    private AppConfiguration _config = new();

    public MainWindow()
    {
        InitializeComponent();
        if (LayoutDebugOptions.Enabled)
        {
            LayoutDebugHelper.Attach(this, RootDock);
        }

        AppsList.ItemsSource = _filteredAppRules;
        RulesGrid.ItemsSource = _advancedRules;
        RealAppFlowGrid.ItemsSource = _realAppFlows;
        FlowSearchBox.TextChanged += (_, _) => RefreshFlowFilters();
        FlowFilterAppCombo.SelectionChanged += (_, _) => RefreshFlowFilters();
        FlowFilterAll.Click += OnFlowFilterChanged;
        FlowFilterVpn.Click += OnFlowFilterChanged;
        FlowFilterDirect.Click += OnFlowFilterChanged;
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
        _config = await LoadConfigFromServiceOrDiskAsync();
        ApplyConfigToUi(_config);
        _ = RefreshLoop();
        await RefreshAsync();
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
        if (_connectUiActive && !force)
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
            bool vpnConnected = snap.Vpn.Connected;
            UpdateHomeDashboard(snap, vpnConnected);
            UpdateAppsVpnState(vpnConnected);

            _allFlowRows.Clear();
            _allFlowRows.AddRange(
                FlowPresentationHelper.SelectUserFlows(snap.Flows).Select(f => new ConnectionFlowRow(f)));
            UpdateFlowFilterAppCombo();
            RefreshFlowFilters();
            LogBox.Text = string.Join(Environment.NewLine, snap.Vpn.RecentLog);
            _tray.Text = vpnConnected ? AppBranding.ProductName + " — подключён" : AppBranding.ProductName + " — отключён";
            await RefreshTempRealAppStatusAsync();
        }
        catch (Exception)
        {
            SetServiceUnavailable(true);
        }
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

    private void UpdateHomePage(ServiceSnapshot snap, bool vpnConnected)
    {
        HomeVpnStatusText.Text = vpnConnected ? "Подключён" : "Отключён";
        HomeVpnStatusDot.Fill = new SolidColorBrush(vpnConnected ? Color.FromRgb(0x05, 0x96, 0x69) : Color.FromRgb(0x9C, 0xA3, 0xAF));
        HomeVpnSubtitle.Text = vpnConnected ? (snap.TransparentRedirectActive ? "Маршрутизация приложений активна" : "Маршрутизация приложений выключена") : "Приложения используют обычное подключение";
        HomeProfileText.Text = string.IsNullOrWhiteSpace(ProfileBox.Text) ? "—" : System.IO.Path.GetFileNameWithoutExtension(ProfileBox.Text);
        HomeDirectAdapterText.Text = snap.DirectAdapter?.Name ?? "—";
        HomeVpnAdapterText.Text = vpnConnected ? snap.VpnAdapter?.Name ?? "—" : "—";
        HomeAppsCountText.Text = ApplicationRulesHelper.CountVpnRoutedApplications(_appRules.Select(r => r.ToRule())).ToString();
        HomeDriverDot.Fill = new SolidColorBrush(snap.DriverLoaded ? Color.FromRgb(0x05, 0x96, 0x69) : Color.FromRgb(0x9C, 0xA3, 0xAF));
        HomeDriverText.Text = snap.DriverLoaded ? "Загружен" : "Не загружен";
        ConnectVpnButton.Visibility = vpnConnected ? Visibility.Collapsed : Visibility.Visible;
        DisconnectVpnButton.Visibility = vpnConnected ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateHomeDashboard(ServiceSnapshot snap, bool vpnConnected)
    {
        UpdateHomePage(snap, vpnConnected);
        HomeVpnAddressText.Text = snap.VpnAdapter?.Ipv4.FirstOrDefault() ?? "—";
        DateTimeOffset activityCutoff = DateTimeOffset.UtcNow.AddMinutes(-5);
        var recentUserFlows = FlowPresentationHelper.SelectUserFlows(snap.Flows, 100)
            .Where(f => f.UpdatedAt >= activityCutoff)
            .ToList();
        HomeRoutingActiveCountText.Text = _appRules.Count(row =>
            recentUserFlows.Any(flow => ApplicationRulesHelper.PathsEqual(row.ExePath, flow.ProcessPath))).ToString();
        HomeRoutingDirectCountText.Text = _appRules.Count(r => r.Mode == RouteMode.Direct).ToString();
        HomeRoutingStatusText.Text = snap.TransparentRedirectActive
            ? "Активна"
            : vpnConnected ? "Ожидает" : "Не активна";
        HomeActiveAppsList.ItemsSource = _appRules.Take(8).ToList();
        HomeRecentFlowsGrid.ItemsSource = _allFlowRows.Take(10).ToList();
    }

    private void UpdateAppsVpnState(bool vpnConnected)
    {
        foreach (ApplicationRuleRow row in _appRules) { row.VpnConnected = vpnConnected; }
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

    private void UpdateFlowFilterAppCombo()
    {
        string? selected = FlowFilterAppCombo.SelectedItem as string;
        List<string> apps = _allFlowRows
            .Select(r => r.Application)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(a => a, StringComparer.OrdinalIgnoreCase)
            .ToList();

        FlowFilterAppCombo.Items.Clear();
        FlowFilterAppCombo.Items.Add("Все приложения");
        foreach (string app in apps)
        {
            FlowFilterAppCombo.Items.Add(app);
        }

        if (!string.IsNullOrWhiteSpace(selected) && FlowFilterAppCombo.Items.Contains(selected))
        {
            FlowFilterAppCombo.SelectedItem = selected;
        }
        else if (FlowFilterAppCombo.SelectedIndex < 0)
        {
            FlowFilterAppCombo.SelectedIndex = 0;
        }
    }

    private void RefreshFlowFilters()
    {
        IEnumerable<ConnectionFlowRow> query = _allFlowRows;

        if (FlowFilterVpn.IsChecked == true)
        {
            query = query.Where(r => r.Route == "VPN");
        }
        else if (FlowFilterDirect.IsChecked == true)
        {
            query = query.Where(r => r.Route == "Напрямую");
        }

        if (FlowFilterErrors.IsChecked == true)
        {
            query = query.Where(r => FlowStatusHelper.IsError(r.State) || FlowStatusHelper.IsCancelled(r.State));
        }

        string? appFilter = FlowFilterAppCombo.SelectedItem as string;
        if (!string.IsNullOrWhiteSpace(appFilter)
            && !string.Equals(appFilter, "Все приложения", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(r => string.Equals(r.Application, appFilter, StringComparison.OrdinalIgnoreCase));
        }

        string search = FlowSearchBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(r =>
                r.Application.Contains(search, StringComparison.OrdinalIgnoreCase)
                || r.Destination.Contains(search, StringComparison.OrdinalIgnoreCase)
                || r.State.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        FlowsGrid.ItemsSource = query.ToList();
    }

    private void OnFlowFilterChanged(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton clicked && clicked.IsChecked == true)
        {
            if (clicked == FlowFilterAll || clicked == FlowFilterVpn || clicked == FlowFilterDirect)
            {
                if (clicked != FlowFilterAll)
                {
                    FlowFilterAll.IsChecked = false;
                }

                if (clicked != FlowFilterVpn)
                {
                    FlowFilterVpn.IsChecked = false;
                }

                if (clicked != FlowFilterDirect)
                {
                    FlowFilterDirect.IsChecked = false;
                }
            }
        }
        else if (FlowFilterAll.IsChecked != true && FlowFilterVpn.IsChecked != true && FlowFilterDirect.IsChecked != true)
        {
            FlowFilterAll.IsChecked = true;
        }

        RefreshFlowFilters();
    }

    private void OnNavigateApps(object sender, RoutedEventArgs e) => MainTabs.SelectedIndex = 1;

    private void OnNavigateConnections(object sender, RoutedEventArgs e) => MainTabs.SelectedIndex = 2;

    private void AppendDiagResult(string line)
    {
        DiagResults.Items.Insert(0, line);
        if (line.StartsWith("FAIL", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("WARNING", StringComparison.OrdinalIgnoreCase))
        {
            DiagLogExpander.IsExpanded = true;
        }
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
            DiagLogExpander.IsExpanded = true;
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
        _appRules.Add(new ApplicationRuleRow(result.Rule, await IsVpnConnectedAsync()));
        RefreshAppsFilter();
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
    }

    private void ApplyConfigToUi(AppConfiguration cfg)
    {
        ExeBox.Text = cfg.Vpn.OpenVpnPath;
        ProfileBox.Text = cfg.Vpn.ProfilePath;
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
        return new AppConfiguration
        {
            Vpn = vpn,
            Rules = _appRules.Select(r => r.ToRule())
                .Concat(_advancedRules.Select(r => r.ToRule()))
                .Concat(diagnosticRules)
                .ToList(),
            Ui = _config.Ui,
        };
    }

    private async void OnStart(object sender, RoutedEventArgs e) => await ConnectVpnAsync();

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
        if (_connectUiActive && !force)
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

    private void OnClearDiagLog(object sender, RoutedEventArgs e) => DiagResults.Items.Clear();

    private void OnCopyDiagLog(object sender, RoutedEventArgs e)
    {
        if (DiagResults.Items.Count == 0)
        {
            return;
        }

        string text = string.Join(Environment.NewLine, DiagResults.Items.Cast<object>().Select(i => i.ToString() ?? ""));
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
            return snap?.Vpn.Connected == true;
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
