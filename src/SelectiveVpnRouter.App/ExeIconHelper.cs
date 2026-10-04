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

    public static ImageSource GetIcon(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return FallbackIcon;
        }

        string key;
        try
        {
            key = Path.GetFullPath(path);
        }
        catch
        {
            return FallbackIcon;
        }

        if (Cache.TryGetValue(key, out ImageSource? cached))
        {
            return cached;
        }

        ImageSource icon = TryLoadIcon(key) ?? FallbackIcon;
        Cache[key] = icon;
        return icon;
    }

    private static ImageSource FallbackIcon
    {
        get
        {
            _fallback ??= AppIconHelper.WpfIcon;
            return _fallback;
        }
    }

    private static ImageSource? TryLoadIcon(string fullPath)
    {
        if (!File.Exists(fullPath))
        {
            return null;
        }

        if (IsRasterOrIconFile(fullPath))
        {
            return TryLoadImageFile(fullPath) ?? TryExtractExecutableIcon(fullPath);
        }

        if (fullPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return TryExtractExecutableIcon(fullPath);
    }

    private static bool IsRasterOrIconFile(string path)
    {
        ReadOnlySpan<string> extensions = [".png", ".jpg", ".jpeg", ".ico", ".bmp"];
        foreach (string extension in extensions)
        {
            if (path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static ImageSource? TryLoadImageFile(string fullPath)
    {
        try
        {
            using FileStream stream = new(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            BitmapDecoder decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.IgnoreColorProfile,
                BitmapCacheOption.OnLoad);

            BitmapFrame? frame = decoder.Frames.OrderByDescending(f => f.PixelWidth * f.PixelHeight).FirstOrDefault();
            if (frame is null)
            {
                return null;
            }

            BitmapSource scaled = frame;
            if (frame.PixelWidth != 32 || frame.PixelHeight != 32)
            {
                scaled = new TransformedBitmap(frame, new ScaleTransform(32d / frame.PixelWidth, 32d / frame.PixelHeight));
            }

            if (scaled.CanFreeze)
            {
                scaled.Freeze();
            }

            return scaled;
        }
        catch
        {
            return null;
        }
    }

    private static ImageSource? TryExtractExecutableIcon(string fullPath)
    {
        try
        {
            using Icon? icon = Icon.ExtractAssociatedIcon(fullPath);
            if (icon is null)
            {
                return null;
            }

            using Icon sized = new(icon, new System.Drawing.Size(32, 32));
            BitmapSource bitmap = Imaging.CreateBitmapSourceFromHIcon(
                sized.Handle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(32, 32));
            if (bitmap.CanFreeze)
            {
                bitmap.Freeze();
            }

            return bitmap;
        }
        catch
        {
            return null;
        }
    }
}
