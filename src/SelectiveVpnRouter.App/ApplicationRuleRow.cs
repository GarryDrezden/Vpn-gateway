using System.ComponentModel;
using System.Windows.Media;
using SelectiveVpnRouter.Core;

using System.IO;

namespace SelectiveVpnRouter.App;

public sealed class ApplicationRuleRow : INotifyPropertyChanged
{
    private RouteMode _mode;
    private bool _vpnConnected;
    private string _runtimeTrafficText = ApplicationRuleRuntimePresentation.InactiveStatus;

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
        _runtimeTrafficText = ApplicationRuleRuntimePresentation.ComputeRuntimeTrafficText(
            Enabled, _mode, _vpnConnected, ExePath, Array.Empty<FlowEvent>());
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
            Notify(nameof(Mode)); Notify(nameof(IsVpnSelected)); Notify(nameof(IsDirectSelected)); Notify(nameof(ConfiguredRouteText)); Notify(nameof(StatusText)); Notify(nameof(StatusBrushKey));
        }
    }

    public bool VpnConnected
    {
        get => _vpnConnected;
        set { if (_vpnConnected == value) return; _vpnConnected = value; Notify(nameof(VpnConnected)); Notify(nameof(StatusText)); Notify(nameof(StatusBrushKey)); }
    }

    public bool IsVpnSelected => Mode == RouteMode.Vpn;
    public bool IsDirectSelected => Mode == RouteMode.Direct;
    public string ConfiguredRouteText => !Enabled ? "Правило отключено" : Mode == RouteMode.Vpn ? "Настроено: VPN" : "Настроено: напрямую";

    public string RuntimeTrafficText
    {
        get => _runtimeTrafficText;
        private set
        {
            if (_runtimeTrafficText == value) return;
            _runtimeTrafficText = value;
            Notify(nameof(RuntimeTrafficText));
            Notify(nameof(StatusText));
            Notify(nameof(StatusBrushKey));
        }
    }

    public void UpdateRuntimeTraffic(IEnumerable<FlowEvent> activeUserFlows)
    {
        RuntimeTrafficText = ApplicationRuleRuntimePresentation.ComputeRuntimeTrafficText(
            Enabled, Mode, VpnConnected, ExePath, activeUserFlows);
    }

    public string StatusText => RuntimeTrafficText;
    public string StatusBrushKey =>
        ApplicationRuleRuntimePresentation.GetStatusBrushKey(Enabled, RuntimeTrafficText);

    public RoutingRule ToRule() => new() { Id = Id, Enabled = Enabled, Type = RuleType.Application, Name = DisplayName, Target = ExePath, Mode = Mode };
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}