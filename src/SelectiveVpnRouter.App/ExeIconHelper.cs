using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SelectiveVpnRouter.App;

public static class ExeIconHelper
{
    private static readonly Dictionary<string, ImageSource> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static ImageSource? _fallback;

    public static ImageSource GetIcon(string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return FallbackIcon;
        string key;
        try { key = Path.GetFullPath(exePath); } catch { return FallbackIcon; }
        if (Cache.TryGetValue(key, out ImageSource? cached)) return cached;
        ImageSource icon = TryExtract(key) ?? FallbackIcon;
        Cache[key] = icon;
        return icon;
    }

    private static ImageSource FallbackIcon { get { _fallback ??= AppIconHelper.WpfIcon; return _fallback; } }

    private static ImageSource? TryExtract(string fullPath)
    {
        if (!File.Exists(fullPath)) return null;
        try
        {
            using Icon? icon = Icon.ExtractAssociatedIcon(fullPath);
            if (icon is null) return null;
            using Icon sized = new(icon, new System.Drawing.Size(32, 32));
            return Imaging.CreateBitmapSourceFromHIcon(sized.Handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(32, 32));
        }
        catch { return null; }
    }
}