using System.ComponentModel;
using System.Windows.Media;
using SelectiveVpnRouter.Core;
using SelectiveVpnRouter.Core.ApplicationDiscovery;

namespace SelectiveVpnRouter.App;

public sealed class DiscoveredApplicationRow : INotifyPropertyChanged
{
    private bool _isSelected;

    public DiscoveredApplicationRow(DiscoveredApplication application)
    {
        Application = application;
        DisplayName = application.DisplayName;
        Publisher = application.Publisher ?? "";
        ExecutablePath = application.ExecutablePath;
        Icon = ExeIconHelper.GetIcon(application.IconPath ?? application.ExecutablePath);
        IsAlreadyConfigured = application.IsAlreadyConfigured;
        IsSelectable = !application.IsAlreadyConfigured;
        ExistingRouteText = application.ExistingRouteMode switch
        {
            RouteMode.Vpn => "Уже настроено: VPN",
            RouteMode.Direct => "Уже настроено: напрямую",
            _ => "",
        };
    }

    public DiscoveredApplication Application { get; }
    public string DisplayName { get; }
    public string Publisher { get; }
    public string ExecutablePath { get; }
    public ImageSource Icon { get; }
    public bool IsAlreadyConfigured { get; }
    public bool IsSelectable { get; }
    public string ExistingRouteText { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            if (value && !IsSelectable)
            {
                return;
            }

            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            SelectedChanged?.Invoke();
        }
    }

    public event Action? SelectedChanged;
    public event PropertyChangedEventHandler? PropertyChanged;
}
