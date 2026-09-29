using System.Text;

namespace SelectiveVpnRouter.Core;

public static class TextEncodingBootstrap
{
    private static int _registered;

    public static void EnsureRegistered()
    {
        if (Interlocked.CompareExchange(ref _registered, 1, 0) != 0)
        {
            return;
        }

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static Encoding GetConsoleOemEncoding()
    {
        EnsureRegistered();
        try
        {
            int codePage = System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage;
            return codePage > 0 ? Encoding.GetEncoding(codePage) : Encoding.UTF8;
        }
        catch (ArgumentException)
        {
            return Encoding.UTF8;
        }
    }
}