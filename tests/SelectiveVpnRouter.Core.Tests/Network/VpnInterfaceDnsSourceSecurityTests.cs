using System.Text.RegularExpressions;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests.Network;

public class VpnInterfaceDnsSourceSecurityTests
{
    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static IEnumerable<string> BrowserDnsPathFiles()
    {
        string dir = Path.Combine(RepoRoot, "src", "SelectiveVpnRouter.Network");
        yield return Path.Combine(dir, "VpnInterfaceDnsResolver.cs");
        yield return Path.Combine(dir, "VpnDnsUdpTransport.cs");
        yield return Path.Combine(dir, "VpnAdapterDnsServers.cs");
        yield return Path.Combine(dir, "VpnSessionDnsStore.cs");
        yield return Path.Combine(dir, "VpnDnsWireFormat.cs");
        string coreDir = Path.Combine(RepoRoot, "src", "SelectiveVpnRouter.Core");
        yield return Path.Combine(coreDir, "OpenVpnPushReplyDnsParser.cs");
    }

    [Fact]
    public void Browser_vpn_dns_path_has_no_system_dns_api()
    {
        foreach (string file in BrowserDnsPathFiles())
        {
            string text = File.ReadAllText(file);
            Assert.False(Regex.IsMatch(text, @"\bDns\.GetHost"), file);
        }
    }
}
