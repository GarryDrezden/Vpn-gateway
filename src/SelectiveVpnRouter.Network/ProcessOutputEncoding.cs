using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace SelectiveVpnRouter.Network;

public static class ProcessOutputEncoding
{
    public static Encoding ConsoleOem
    {
        get
        {
            try
            {
                int codePage = CultureInfo.CurrentCulture.TextInfo.OEMCodePage;
                return codePage > 0 ? Encoding.GetEncoding(codePage) : Encoding.UTF8;
            }
            catch (ArgumentException)
            {
                return Encoding.UTF8;
            }
        }
    }

    public static void UseConsoleEncoding(ProcessStartInfo psi)
    {
        Encoding enc = ConsoleOem;
        psi.StandardOutputEncoding = enc;
        psi.StandardErrorEncoding = enc;
    }
}