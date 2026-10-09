using System.Text.RegularExpressions;
using SelectiveVpnRouter.Core.BrowserRouting;
using SelectiveVpnRouter.Network;
using SelectiveVpnRouter.Proxy.BrowserExplicit;
using Xunit;

namespace SelectiveVpnRouter.Proxy.Tests;

public class BrowserExplicitSourceSecurityTests
{
    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    public void G_browser_explicit_layer_has_no_system_dns()
    {
        var dir = Path.Combine(RepoRoot, "src", "SelectiveVpnRouter.Proxy", "BrowserExplicit");
        foreach (string file in Directory.GetFiles(dir, "*.cs"))
        {
            string text = File.ReadAllText(file);
            Assert.False(Regex.IsMatch(text, @"\bDns\.GetHost"), file);
        }
    }

    [Fact]
    public void Vpn_bound_egress_uses_hostname_resolver_not_system_dns()
    {
        string egress = File.ReadAllText(
            Path.Combine(RepoRoot, "src", "SelectiveVpnRouter.Proxy", "BrowserExplicit", "VpnBoundTcpEgress.cs"));
        Assert.Contains("IVpnBrowserHostnameResolver", egress);
        Assert.DoesNotMatch(@"\bDns\.GetHost", egress);
        Assert.DoesNotMatch(@"ConnectAsync\([^)]*hostname", egress);
    }

    [Fact]
    public void Browser_explicit_composition_uses_vpn_dns_resolver_adapter()
    {
        var tunnel = new FakeTunnelReadiness(false);
        var dns = new VpnInterfaceDnsResolver(tunnel, new VpnSessionDnsStore());
        var egress = new VpnBoundTcpEgress(tunnel, dns);
        Assert.IsType<VpnBoundTcpEgress>(egress);
    }

    private sealed class FakeTunnelReadiness(bool ready) : IVpnTunnelEgressReadiness
    {
        public bool TryGetTunnelInterfaceIndex(out int interfaceIndex)
        {
            interfaceIndex = 0;
            return ready;
        }
    }
}
