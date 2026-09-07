using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
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
    private AppConfiguration _config = new();

    public MainWindow()
    {
        InitializeComponent();
        RulesGrid.ItemsSource = _rules;
        _tray.Text = "Selective VPN Router";
        _tray.Visible = true;
        _tray.Icon = System.Drawing.SystemIcons.Shield;
        _tray.DoubleClick += (_, _) => { Show(); WindowState = WindowState.Normal; Activate(); };
        _tray.ContextMenuStrip = BuildTray();
        Loaded += async (_, _) => await StartAsync();
    }

    private Forms.ContextMenuStrip BuildTray()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => { Show(); Activate(); });
        menu.Items.Add("Start VPN", null, async (_, _) => await Call(IpcMethods.ConnectVpn));
        menu.Items.Add("Stop VPN", null, async (_, _) => await Call(IpcMethods.DisconnectVpn));
        menu.Items.Add("Pause routing", null, async (_, _) => await Call(IpcMethods.PauseRouting));
        menu.Items.Add("Emergency Restore", null, async (_, _) => await Call(IpcMethods.EmergencyRestore));
                menu.Items.Add("Exit", null, (_, _) => { _exit = true; _tray.Visible = false; System.Windows.Application.Current.Shutdown(); });
        return menu;
    }

    private async Task StartAsync()
    {
        _config = ConfigSerializer.LoadOrDefault(AppPaths.ConfigFile);
        ApplyConfigToUi(_config);
        _ = RefreshLoop();
        await RefreshAsync();
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

    private async Task RefreshAsync()
    {
        try
        {
            ServiceSnapshot? snap = await _client.SendOkAsync<ServiceSnapshot>(IpcMethods.GetStatus, null, _cts.Token);
            if (snap is null)
            {
                return;
            }

            StatusLine.Text = snap.Vpn.Connected
                ? $"VPN: Connected    Driver: {(snap.DriverLoaded ? "loaded" : "NOT loaded")}    Redirect: {(snap.TransparentRedirectActive ? "armed" : "inactive")}"
                : $"VPN: Disconnected    Service: up    Driver: {(snap.DriverLoaded ? "loaded" : "NOT loaded")}";
            IfaceLine.Text = $"Direct: {snap.DirectAdapter?.Name ?? "—"}    VPN: {snap.VpnAdapter?.Name ?? "—"} if={snap.VpnAdapter?.Ipv4Index?.ToString() ?? "—"}  {string.Join(",", snap.VpnAdapter?.Ipv4 ?? [])}";
            FlowsGrid.ItemsSource = snap.Flows;
            LogBox.Text = string.Join(Environment.NewLine, snap.Vpn.RecentLog);
            _tray.Text = snap.Vpn.Connected ? "Selective VPN Router — Connected" : "Selective VPN Router — Disconnected";
        }
        catch (Exception)
        {
            StatusLine.Text = "Service: not connected. Start SelectiveVpnRouter.Service.exe as Administrator (--console or Windows Service).";
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

    private async void OnStart(object sender, RoutedEventArgs e) => await Call(IpcMethods.ConnectVpn, new ConnectVpnRequest
    {
        OpenVpnPath = ExeBox.Text.Trim(),
        ProfilePath = ProfileBox.Text.Trim(),
        DisableDco = DcoBox.IsChecked == true,
    });

    private async void OnStop(object sender, RoutedEventArgs e) => await Call(IpcMethods.DisconnectVpn);
    private async void OnPause(object sender, RoutedEventArgs e) => await Call(IpcMethods.PauseRouting);
    private async void OnEmergency(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Remove this app's routes, WFP filters, and managed OpenVPN? Other VPNs are not touched.",
                "Emergency restore", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
        {
            return;
        }

        await Call(IpcMethods.EmergencyRestore);
    }

    private void OnAddApp(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Programs (*.exe)|*.exe" };
        if (dlg.ShowDialog() != true)
        {
            return;
        }

        _rules.Add(RuleRow.From(RoutingRule.Create(RuleType.Application, Path.GetFileNameWithoutExtension(dlg.FileName), dlg.FileName, RouteMode.Vpn)));
    }

    private void OnAddDomain(object sender, RoutedEventArgs e)
    {
        string? host = Prompt("Domain (example.com or *.example.com). Applies to ALL processes.", "youtube.com");
        if (string.IsNullOrWhiteSpace(host))
        {
            return;
        }

        _rules.Add(RuleRow.From(RoutingRule.Create(RuleType.Domain, host, host, RouteMode.Vpn)));
    }

    private void OnAddCidr(object sender, RoutedEventArgs e)
    {
        string? cidr = Prompt("IP or CIDR. Applies to ALL processes.", "1.2.3.0/24");
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
        WizardHint.Text = "Saved. Connect VPN, then run Test Center. Add git.exe as DIRECT if Cursor should use VPN while git stays on the work network.";
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
        var dlg = new OpenFileDialog { Filter = "Programs (*.exe)|*.exe" };
        if (dlg.ShowDialog() == true)
        {
            FirstAppBox.Text = dlg.FileName;
        }
    }

    private async void OnDiag(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string name })
        {
            return;
        }

        try
        {
            DiagnosticResult? r = await _client.SendOkAsync<DiagnosticResult>(IpcMethods.RunDiagnostic, new { name }, _cts.Token);
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
        try
        {
            await _client.SendOkAsync<AppConfiguration>(IpcMethods.SetConfig, _config, _cts.Token);
        }
        catch (Exception)
        {
            Directory.CreateDirectory(AppPaths.ProgramData);
            ConfigSerializer.Save(AppPaths.ConfigFile, _config);
        }
    }

    private async Task Call(string method, object? payload = null)
    {
        try
        {
            await SaveConfigAsync();
            await _client.SendOkAsync<ServiceSnapshot>(method, payload, _cts.Token);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Selective VPN Router");
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
