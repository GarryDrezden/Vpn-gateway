using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using WpfColor = System.Windows.Media.Color;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfSolidColorBrush = System.Windows.Media.SolidColorBrush;
using WpfPanel = System.Windows.Controls.Panel;
using WpfControl = System.Windows.Controls.Control;

namespace SelectiveVpnRouter.App;

public static class LayoutDebugHelper
{
    private static readonly WpfBrush DebugBorder = new WpfSolidColorBrush(WpfColor.FromArgb(0xAA, 0xE5, 0x7C, 0x00));

    public static void Attach(Window window, FrameworkElement root)
    {
        if (!LayoutDebugOptions.Enabled)
        {
            return;
        }

        var overlay = new TextBlock
        {
            Background = new WpfSolidColorBrush(WpfColor.FromArgb(0xDD, 0x1F, 0x29, 0x37)),
            Foreground = WpfBrushes.White,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            FontSize = 11,
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(8, 0, 8, 4),
            TextWrapping = TextWrapping.Wrap,
            IsHitTestVisible = false,
        };

        if (root is DockPanel dockRoot)
        {
            DockPanel.SetDock(overlay, Dock.Top);
            dockRoot.Children.Insert(1, overlay);
        }
        else if (root is WpfPanel panel)
        {
            panel.Children.Add(overlay);
        }

        void Refresh()
        {
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(window);
            overlay.Text =
                "layout-debug  client=" + window.ActualWidth.ToString("F0") + "x" + window.ActualHeight.ToString("F0") +
                "  dpi=" + (dpi.DpiScaleX * 100).ToString("F0") + "%  pixelsPerDip=" + dpi.PixelsPerDip.ToString("F2");
            LogLayout(window, "resize");
        }

        window.Loaded += (_, _) => { Highlight(root); Refresh(); };
        window.SizeChanged += (_, _) => Refresh();
    }

    private static void Highlight(DependencyObject node)
    {
        if (node is FrameworkElement fe && IsTracked(fe.Name))
        {
            if (fe is Border border)
            {
                border.BorderBrush = DebugBorder;
                border.BorderThickness = new Thickness(2);
            }
            else if (fe is WpfControl control && control.Background is null)
            {
                control.Background = new WpfSolidColorBrush(WpfColor.FromArgb(0x20, 0xE5, 0x7C, 0x00));
            }
            else if (fe is WpfPanel panel && panel.Background is null)
            {
                panel.Background = new WpfSolidColorBrush(WpfColor.FromArgb(0x20, 0xE5, 0x7C, 0x00));
            }
        }

        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < count; i++)
        {
            Highlight(System.Windows.Media.VisualTreeHelper.GetChild(node, i));
        }
    }

    private static void LogLayout(Window window, string reason)
    {
        Debug.WriteLine("[layout-debug] " + reason + " client=" + window.ActualWidth.ToString("F0") + "x" + window.ActualHeight.ToString("F0"));
        string[] names = { "RootDock", "HeaderBorder", "MainTabs", "RulesTabRoot", "ConnectionsTabRoot", "TestTabRoot", "SetupTabRoot", "LogTabRoot" };
        foreach (string name in names)
        {
            if (window.FindName(name) is FrameworkElement target)
            {
                Debug.WriteLine("[layout-debug] " + name + ": " + target.ActualWidth.ToString("F0") + "x" + target.ActualHeight.ToString("F0") +
                    " render=" + target.RenderSize.Width.ToString("F0") + "x" + target.RenderSize.Height.ToString("F0"));
            }
        }
    }

    private static bool IsTracked(string? name) =>
        name is "RootDock" or "HeaderBorder" or "MainTabs" or "RulesTabRoot" or "ConnectionsTabRoot"
            or "TestTabRoot" or "SetupTabRoot" or "LogTabRoot";
}