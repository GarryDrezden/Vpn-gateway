using System.Diagnostics;
using System.Text;
using SelectiveVpnRouter.Core;

namespace SelectiveVpnRouter.Network;

public static class ProcessOutputEncoding
{
    public static Encoding ConsoleOem => TextEncodingBootstrap.GetConsoleOemEncoding();

    public static void UseConsoleEncoding(ProcessStartInfo psi)
    {
        Encoding enc = ConsoleOem;
        psi.StandardOutputEncoding = enc;
        psi.StandardErrorEncoding = enc;
    }
}