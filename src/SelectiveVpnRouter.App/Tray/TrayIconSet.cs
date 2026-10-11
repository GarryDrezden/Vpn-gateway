using System.Drawing;
using System.IO;
using SelectiveVpnRouter.Core.Tray;
using DrawingIcon = System.Drawing.Icon;

namespace SelectiveVpnRouter.App.Tray;

internal sealed class TrayIconSet
{
    private readonly Dictionary<TrayVisualState, DrawingIcon> _icons = new();

    public DrawingIcon GetIcon(TrayVisualState state)
    {
        TrayVisualState key = state == TrayVisualState.Connecting ? TrayVisualState.Ready : state;
        if (_icons.TryGetValue(key, out DrawingIcon? cached))
        {
            return cached;
        }

        DrawingIcon loaded = LoadIcon(key) ?? AppIconHelper.CloneTrayIcon();
        _icons[key] = loaded;
        return loaded;
    }

    private static DrawingIcon? LoadIcon(TrayVisualState state)
    {
        string fileName = state switch
        {
            TrayVisualState.Active => "vpn-route-tray-active.ico",
            TrayVisualState.Error => "vpn-route-tray-error.ico",
            _ => "vpn-route-tray-ready.ico",
        };

        string path = Path.Combine(AppContext.BaseDirectory, "tray", fileName);
        if (!File.Exists(path))
        {
            return null;
        }

        using DrawingIcon icon = new(path);
        return (DrawingIcon)icon.Clone();
    }
}
