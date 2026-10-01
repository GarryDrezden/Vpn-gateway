using System.ComponentModel;
using System.Windows.Media;
using SelectiveVpnRouter.Core;

using System.IO;

namespace SelectiveVpnRouter.App;

public sealed class ApplicationRuleRow : INotifyPropertyChanged
{
    private RouteMode _mode;
    private bool _vpnConnected;

    public ApplicationRuleRow(RoutingRule rule, bool vpnConnected)
    {
        Id = rule.Id;
        DisplayName = rule.Name;
        ExePath = rule.Target;
        FileName = Path.GetFileName(rule.Target);
        _mode = rule.Mode;
        _vpnConnected = vpnConnected;
        Enabled = rule.Enabled;
        Icon = ExeIconHelper.GetIcon(rule.Target);
    }

    public Guid Id { get; }
    public string DisplayName { get; }
    public string ExePath { get; }
    public string FileName { get; }
    public ImageSource Icon { get; }
    public bool Enabled { get; }

    public RouteMode Mode
    {
        get => _mode;
        set
        {
            if (_mode == value) return;
            _mode = value;
            Notify(nameof(Mode)); Notify(nameof(IsVpnSelected)); Notify(nameof(IsDirectSelected)); Notify(nameof(StatusText)); Notify(nameof(StatusBrushKey));
        }
    }

    public bool VpnConnected
    {
        get => _vpnConnected;
        set { if (_vpnConnected == value) return; _vpnConnected = value; Notify(nameof(VpnConnected)); Notify(nameof(StatusText)); Notify(nameof(StatusBrushKey)); }
    }

    public bool IsVpnSelected => Mode == RouteMode.Vpn;
    public bool IsDirectSelected => Mode == RouteMode.Direct;
    public string StatusText => !Enabled ? "Правило отключено" : Mode == RouteMode.Direct ? "Правило активно" : (VpnConnected ? "Правило активно" : "Ожидает VPN");
    public string StatusBrushKey => Mode == RouteMode.Vpn && !VpnConnected ? "StatusWaiting" : !Enabled ? "StatusNeutral" : "StatusSuccess";

    public RoutingRule ToRule() => new() { Id = Id, Enabled = Enabled, Type = RuleType.Application, Name = DisplayName, Target = ExePath, Mode = Mode };
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}