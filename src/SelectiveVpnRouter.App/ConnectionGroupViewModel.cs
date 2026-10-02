using System.ComponentModel;
using SelectiveVpnRouter.Core;

namespace SelectiveVpnRouter.App;

public sealed class ConnectionGroupViewModel : INotifyPropertyChanged
{
    private bool _isExpanded;

    public ConnectionGroupViewModel(ConnectionAppGroupProjection projection, bool isExpanded)
    {
        Projection = projection;
        _isExpanded = isExpanded;
    }

    public ConnectionAppGroupProjection Projection { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
