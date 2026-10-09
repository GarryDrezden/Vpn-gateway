using SelectiveVpnRouter.Core.BrowserRouting;
using Xunit;

namespace SelectiveVpnRouter.BrowserRouting.Tests;

public class BrowserIntegrationUiPresentationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void N_never_seen_copy()
    {
        var vm = Map(client: BrowserIntegrationContract.BrowserClientStatus.NeverSeen, proxyReady: true, egressReady: false);
        Assert.Equal("Расширение ещё не обнаружено", vm.ExtensionStatus);
    }

    [Fact]
    public void O_recently_seen_copy()
    {
        var vm = Map(client: BrowserIntegrationContract.BrowserClientStatus.RecentlySeen, proxyReady: true, egressReady: false,
            lastSeen: Now.AddMinutes(-1));
        Assert.Equal("Расширение недавно активно", vm.ExtensionStatus);
        Assert.Equal("1 мин назад", vm.LastContact);
    }

    [Fact]
    public void P_stale_copy()
    {
        var vm = Map(client: BrowserIntegrationContract.BrowserClientStatus.Stale, proxyReady: true, egressReady: true);
        Assert.Equal("Расширение давно не отвечало", vm.ExtensionStatus);
    }

    [Fact]
    public void Q_proxy_ready_egress_unavailable_is_not_error_wording()
    {
        var vm = Map(client: BrowserIntegrationContract.BrowserClientStatus.RecentlySeen, proxyReady: true, egressReady: false);
        Assert.Equal("Готов", vm.BrowserProxyStatus);
        Assert.Equal("Недоступен", vm.VpnEgressStatus);
        Assert.Contains("fail-closed", vm.DetailLine!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void R_proxy_and_egress_ready()
    {
        var vm = Map(client: BrowserIntegrationContract.BrowserClientStatus.RecentlySeen, proxyReady: true, egressReady: true,
            ifIndex: 12, ifName: "TAP");
        Assert.Equal("Готов", vm.BrowserProxyStatus);
        Assert.Equal("Готов", vm.VpnEgressStatus);
        Assert.Equal("TAP / #12", vm.VpnInterface);
        Assert.Null(vm.DetailLine);
    }

    [Fact]
    public void S_endpoint_absent_shows_dash_socks()
    {
        var vm = Map(client: BrowserIntegrationContract.BrowserClientStatus.NeverSeen, proxyReady: false, egressReady: false);
        Assert.Equal("—", vm.SocksEndpoint);
    }

    [Fact]
    public void T_rule_count_is_generic()
    {
        var vm = Map(client: BrowserIntegrationContract.BrowserClientStatus.NeverSeen, proxyReady: true, egressReady: false, ruleCount: 5);
        Assert.Equal("5", vm.RuleCount);
    }

    private static BrowserIntegrationUiPresentation.ViewModel Map(
        string client,
        bool proxyReady,
        bool egressReady,
        DateTimeOffset? lastSeen = null,
        int ruleCount = 0,
        int? ifIndex = null,
        string? ifName = null)
    {
        var integration = new BrowserIntegrationSnapshot
        {
            IntegrationApiVersion = 1,
            BrowserClient = new BrowserIntegrationClientSnapshot(client, lastSeen),
            BrowserProxy = proxyReady
                ? new BrowserIntegrationProxySnapshot(BrowserProxyStatus.Ready, new BrowserIntegrationEndpointSnapshot("127.0.0.1", 19001))
                : new BrowserIntegrationProxySnapshot(BrowserProxyStatus.Unavailable, null),
            VpnEgress = egressReady
                ? new BrowserIntegrationVpnEgressSnapshot(BrowserIntegrationContract.VpnEgressStatus.Ready, ifIndex ?? 1, ifName ?? "tap")
                : new BrowserIntegrationVpnEgressSnapshot(BrowserIntegrationContract.VpnEgressStatus.Unavailable, null, null),
            RuleCount = ruleCount,
        };
        return BrowserIntegrationUiPresentation.Map(integration, Now);
    }
}
