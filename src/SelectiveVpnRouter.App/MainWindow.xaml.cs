using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SelectiveVpnRouter.Core;
using Forms = System.Windows.Forms;
using MessageBox = System.Windows.MessageBox;
using Window = System.Windows.Window;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;

namespace SelectiveVpnRouter.App;

public partial class MainWindow : Window
{
    private readonly ServiceClient _client = new();
    private readonly ObservableCollection<RuleRow> _rules = [];
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

        RulesGrid.ItemsSource = _rules;
        _tray.Text = "Selective VPN Router";
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
                return;
            }

            string vpnStatus = snap.Vpn.Connected ? "VPN: подключён" : "VPN: отключён";
            string driverStatus = snap.DriverLoaded ? "Драйвер: загружен" : "Драйвер: не загружен";
            if (snap.Vpn.Connected)
            {
                string redirectStatus = snap.TransparentRedirectActive ? "Перенаправление: включено" : "Перенаправление: выкл.";
                StatusLine.Text = $"{vpnStatus}    {driverStatus}    {redirectStatus}";
            }
            else
            {
                StatusLine.Text = $"{vpnStatus}    Служба: работает    {driverStatus}";
            }

            IfaceLine.Text = $"Напрямую: {snap.DirectAdapter?.Name ?? "—"}    VPN: {snap.VpnAdapter?.Name ?? "—"} if={snap.VpnAdapter?.Ipv4Index?.ToString() ?? "—"}  preferred default if={snap.PreferredDefault?.InterfaceIndex} metric={snap.PreferredDefault?.Metric}    owned 0/0 metric={snap.OwnedTransportDefault?.Metric.ToString() ?? "—"}";
            FlowsGrid.ItemsSource = snap.Flows;
            LogBox.Text = string.Join(Environment.NewLine, snap.Vpn.RecentLog);
            _tray.Text = snap.Vpn.Connected ? "Selective VPN Router — подключён" : "Selective VPN Router — отключён";
            await RefreshTempRealAppStatusAsync();
        }
        catch (Exception)
        {
            StatusLine.Text = "Служба Selective VPN Router не запущена.";
            IfaceLine.Text = string.Empty;
        }
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
        _rules.Clear();
        foreach (RoutingRule r in cfg.Rules)
        {
            _rules.Add(RuleRow.From(r));
        }
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
        return new AppConfiguration
        {
            Vpn = vpn,
            Rules = _rules.Select(r => r.ToRule()).ToList(),
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

    private void OnAddApp(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Программы (*.exe)|*.exe" };
        if (dlg.ShowDialog() != true)
        {
            return;
        }

        _rules.Add(RuleRow.From(RoutingRule.Create(RuleType.Application, Path.GetFileNameWithoutExtension(dlg.FileName), dlg.FileName, RouteMode.Vpn)));
    }

    private void OnAddDomain(object sender, RoutedEventArgs e)
    {
        string? host = Prompt("Домен (example.com или *.example.com). Действует для всех процессов.", "youtube.com");
        if (string.IsNullOrWhiteSpace(host))
        {
            return;
        }

        _rules.Add(RuleRow.From(RoutingRule.Create(RuleType.Domain, host, host, RouteMode.Vpn)));
    }

    private void OnAddCidr(object sender, RoutedEventArgs e)
    {
        string? cidr = Prompt("IP или CIDR. Действует для всех процессов.", "1.2.3.0/24");
        if (string.IsNullOrWhiteSpace(cidr))
        {
            return;
        }

        _rules.Add(RuleRow.From(RoutingRule.Create(RuleType.Cidr, cidr, cidr, RouteMode.Vpn)));
    }

    private void OnRemoveRule(object sender, RoutedEventArgs e)
    {
        if (RulesGrid.SelectedItem is RuleRow row)
        {
            _rules.Remove(row);
        }
    }

    private async void OnSaveRules(object sender, RoutedEventArgs e) => await SaveConfigAsync();

    private async void OnSaveSetup(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(FirstAppBox.Text) && File.Exists(FirstAppBox.Text)
            && !_rules.Any(r => r.Type == RuleType.Application && string.Equals(r.Target, FirstAppBox.Text, StringComparison.OrdinalIgnoreCase)))
        {
            _rules.Add(RuleRow.From(RoutingRule.Create(RuleType.Application, Path.GetFileNameWithoutExtension(FirstAppBox.Text), FirstAppBox.Text, RouteMode.Vpn)));
        }

        await SaveConfigAsync();
        WizardHint.Text = "Сохранено. Подключите VPN и откройте «Тестирование». Добавьте git.exe как «Напрямую», если Cursor должен идти через VPN, а git — через рабочую сеть.";
    }

    private void OnAddGitDirect(object sender, RoutedEventArgs e)
    {
        if (_rules.Any(r => r.Type == RuleType.Application && r.Target.EndsWith("git.exe", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        _rules.Add(RuleRow.From(RoutingRule.Create(RuleType.Application, "git", "git.exe", RouteMode.Direct)));
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

    private async Task RefreshTempRealAppStatusAsync(bool force = false)
    {
        if (_connectUiActive && !force)
        {
            return;
        }

        try
        {
            TempAppVpnStatus? status = await _client.SendOkAsync<TempAppVpnStatus>(
                IpcMethods.GetTempAppVpnStatus,
                null,
                _cts.Token);
            if (status is not null)
            {
                ApplyTempRealAppStatus(status);
            }
        }
        catch (Exception)
        {
            RealAppStatusLine.Text = "Статус: служба недоступна";
            RealAppStatusLine.Foreground = System.Windows.Media.Brushes.Gray;
            RealAppDetailLine.Text = string.Empty;
            RealAppAdvancedLine.Text = string.Empty;
        }
    }

    private void ApplyTempRealAppStatus(TempAppVpnStatus status)
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
                RealAppStatusLine.Text = "PASS — VPN egress подтверждён (flow завершился успешно)";
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
                DiagResults.Items.Insert(0, "WARNING  " + name + ": отменено");
                return;
            }

            confirm = true;
        }

        try
        {
            DiagnosticResult? r = await _client.SendOkAsync<DiagnosticResult>(IpcMethods.RunDiagnostic, new { name, confirm }, _cts.Token);
            if (r is not null)
            {
                DiagResults.Items.Insert(0, $"{r.Outcome}  {r.Name}: {r.Message}");
            }
        }
        catch (Exception ex)
        {
            DiagResults.Items.Insert(0, "FAIL  " + name + ": " + ex.Message);
        }
    }

    private async Task SaveConfigAsync()
    {
        _config = ReadConfigFromUi();
        await _client.SendOkAsync<AppConfiguration>(IpcMethods.SetConfig, _config, _cts.Token);
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
            StatusLine.Text = "VPN: подключение...";
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
