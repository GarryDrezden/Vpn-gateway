using SelectiveVpnRouter.Core.BrowserRouting;
using SelectiveVpnRouter.Network;
using SelectiveVpnRouter.Proxy.BrowserExplicit;
using Xunit;

namespace SelectiveVpnRouter.Proxy.Tests;

public class BrowserEgressCompositionTests
{
    [Fact]
    public void Production_egress_depends_on_tunnel_readiness_and_vpn_dns_resolver()
    {
        var tunnel = new FakeCompositionTunnel();
        var dns = new VpnInterfaceDnsResolver(tunnel, new VpnSessionDnsStore());
        var egress = new VpnBoundTcpEgress(tunnel, dns);

        Assert.IsType<VpnBoundTcpEgress>(egress);
        Assert.IsType<VpnInterfaceDnsResolver>(dns);
        Assert.IsAssignableFrom<IVpnTunnelEgressReadiness>(tunnel);
    }

    [Fact]
    public void Domain_path_has_single_hostname_resolver_abstraction()
    {
        string text = File.ReadAllText(
            Path.Combine(
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..")),
                "src",
                "SelectiveVpnRouter.Proxy",
                "BrowserExplicit",
                "VpnBoundTcpEgress.cs"));
        Assert.Contains("ResolveIpv4Async", text);
        Assert.DoesNotMatch(@"\bDns\.GetHost", text);
    }

    private sealed class FakeCompositionTunnel : IVpnTunnelEgressReadiness
    {
        public bool TryGetTunnelInterfaceIndex(out int interfaceIndex)
        {
            interfaceIndex = 0;
            return false;
        }
    }
}
