using System.Windows.Forms;
using SelectiveVpnRouter.Core.Tray;
using DrawingIcon = System.Drawing.Icon;

namespace SelectiveVpnRouter.App.Tray;

internal sealed class AppTrayController : IDisposable
{
    public sealed class HostActions
    {
        public required Action OpenMainWindow { get; init; }
        public required Func<Task> ConnectVpnAsync { get; init; }
        public required Func<Task> DisconnectVpnAsync { get; init; }
        public required Action OpenSettings { get; init; }
        public required Action ExitApplication { get; init; }
    }

    private readonly HostActions _host;
    private readonly NotifyIcon _notifyIcon = new();
    private readonly TrayIconSet _icons = new();
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _connectItem;
    private readonly ToolStripMenuItem _disconnectItem;
    private bool _initialized;
    private bool _disposed;

    public AppTrayController(HostActions host)
    {
        _host = host;
        _statusItem = new ToolStripMenuItem { Enabled = false };
        _connectItem = new ToolStripMenuItem("Подключить", null, async (_, _) => await _host.ConnectVpnAsync().ConfigureAwait(true));
        _disconnectItem = new ToolStripMenuItem("Отключить", null, async (_, _) => await _host.DisconnectVpnAsync().ConfigureAwait(true));
    }

    public void EnsureInitialized()
    {
        if (_initialized || _disposed)
        {
            return;
        }

        _notifyIcon.Icon = CloneIcon(_icons.GetIcon(TrayVisualState.Ready));
        _notifyIcon.Text = AppBranding.ProductName;
        _notifyIcon.Visible = true;
        _notifyIcon.DoubleClick += (_, _) => _host.OpenMainWindow();
        _notifyIcon.ContextMenuStrip = BuildMenu();
        _initialized = true;
    }

    public void Apply(TrayStatusPresentation presentation)
    {
        EnsureInitialized();
        if (_disposed)
        {
            return;
        }

        DrawingIcon next = CloneIcon(_icons.GetIcon(presentation.VisualState));
        DrawingIcon? previous = _notifyIcon.Icon;
        _notifyIcon.Icon = next;
        previous?.Dispose();
        _notifyIcon.Text = presentation.Tooltip;
        _statusItem.Text = presentation.MenuStatusText;
        _connectItem.Enabled = presentation.ConnectEnabled;
        _disconnectItem.Enabled = presentation.DisconnectEnabled;
    }

    public void ShowBalloon(string title, string message, ToolTipIcon icon = ToolTipIcon.Warning)
    {
        EnsureInitialized();
        if (_disposed)
        {
            return;
        }

        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.BalloonTipIcon = icon;
        _notifyIcon.ShowBalloonTip(3000);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem(AppBranding.ProductName) { Enabled = false });
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Открыть приложение", null, (_, _) => _host.OpenMainWindow()));
        menu.Items.Add(_connectItem);
        menu.Items.Add(_disconnectItem);
        menu.Items.Add(new ToolStripMenuItem("Настройки", null, (_, _) => _host.OpenSettings()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Выход", null, (_, _) => _host.ExitApplication()));
        return menu;
    }

    private static DrawingIcon CloneIcon(DrawingIcon icon)
    {
        using DrawingIcon clone = (DrawingIcon)icon.Clone();
        return new DrawingIcon(clone, 16, 16);
    }
}
