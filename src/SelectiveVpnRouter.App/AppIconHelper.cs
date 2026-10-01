using System.IO;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DrawingIcon = System.Drawing.Icon;
using WpfApplication = System.Windows.Application;

namespace SelectiveVpnRouter.App;

internal static class AppIconHelper
{
    public const string IconFileName = "vpn-route-icon.ico";

    private static DrawingIcon? _drawingIcon;
    private static ImageSource? _wpfIcon;

    public static DrawingIcon DrawingIcon => _drawingIcon ??= LoadDrawingIcon();

    public static ImageSource WpfIcon => _wpfIcon ??= LoadWpfIcon();

    public static DrawingIcon CloneTrayIcon()
    {
        using DrawingIcon icon = (DrawingIcon)DrawingIcon.Clone();
        return new DrawingIcon(icon, 16, 16);
    }

    private static ImageSource LoadWpfIcon()
    {
        Stream? stream = OpenIconStream();
        if (stream is not null)
        {
            using (stream)
            {
                var frame = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                frame.Freeze();
                return frame;
            }
        }

        using var ms = new MemoryStream();
        using (DrawingIcon icon = (DrawingIcon)DrawingIcon.Clone())
        {
            icon.Save(ms);
        }

        ms.Position = 0;
        var fallback = BitmapFrame.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        fallback.Freeze();
        return fallback;
    }

    private static DrawingIcon LoadDrawingIcon()
    {
        Stream? stream = OpenIconStream();
        if (stream is not null)
        {
            using (stream)
            {
                return new DrawingIcon(stream);
            }
        }

        string icoPath = Path.Combine(AppContext.BaseDirectory, IconFileName);
        if (File.Exists(icoPath))
        {
            return new DrawingIcon(icoPath);
        }

        string? exePath = Environment.ProcessPath;
        if (exePath is not null)
        {
            DrawingIcon? fromExe = DrawingIcon.ExtractAssociatedIcon(exePath);
            if (fromExe is not null)
            {
                return fromExe;
            }
        }

        return System.Drawing.SystemIcons.Application;
    }

    private static Stream? OpenIconStream()
    {
        try
        {
            Stream? stream = WpfApplication.GetResourceStream(
                new Uri("pack://application:,,,/" + IconFileName, UriKind.Absolute))?.Stream;
            if (stream is not null)
            {
                var ms = new MemoryStream();
                stream.CopyTo(ms);
                ms.Position = 0;
                return ms;
            }

            return Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("SelectiveVpnRouter.App." + IconFileName);
        }
        catch (IOException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
