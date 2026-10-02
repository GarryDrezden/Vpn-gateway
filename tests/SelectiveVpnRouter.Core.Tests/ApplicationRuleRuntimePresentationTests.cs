using SelectiveVpnRouter.Core;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public class ApplicationRuleRuntimePresentationTests
{
    private const string Exe = @"C:\Apps\qBittorrent\qbittorrent.exe";

    [Fact]
    public void Direct_configured_without_observed_flow_shows_napryamuyu_not_inactive()
    {
        string status = ApplicationRuleRuntimePresentation.ComputeRuntimeTrafficText(
            enabled: true,
            mode: RouteMode.Direct,
            vpnConnected: true,
            exePath: Exe,
            activeUserFlows: Array.Empty<FlowEvent>());

        Assert.Equal(ApplicationRuleRuntimePresentation.DirectConfiguredStatus, status);
        Assert.NotEqual(ApplicationRuleRuntimePresentation.InactiveStatus, status);
    }

    [Fact]
    public void Direct_configured_ignores_observed_proxy_flows()
    {
        var flows = new[]
        {
            new FlowEvent { ProcessPath = Exe, Route = FlowRoute.Vpn, WfpRedirect = true },
        };

        string status = ApplicationRuleRuntimePresentation.ComputeRuntimeTrafficText(
            true, RouteMode.Direct, true, Exe, flows);

        Assert.Equal(ApplicationRuleRuntimePresentation.DirectConfiguredStatus, status);
    }

    [Fact]
    public void Vpn_configured_without_flow_stays_inactive()
    {
        string status = ApplicationRuleRuntimePresentation.ComputeRuntimeTrafficText(
            true, RouteMode.Vpn, true, Exe, Array.Empty<FlowEvent>());

        Assert.Equal(ApplicationRuleRuntimePresentation.InactiveStatus, status);
    }

    [Fact]
    public void Vpn_with_redirect_flow_shows_active_via_vpn()
    {
        var flows = new[]
        {
            new FlowEvent { ProcessPath = Exe, Route = FlowRoute.Vpn, WfpRedirect = true },
        };

        string status = ApplicationRuleRuntimePresentation.ComputeRuntimeTrafficText(
            true, RouteMode.Vpn, true, Exe, flows);

        Assert.Equal(ApplicationRuleRuntimePresentation.ActiveViaVpnStatus, status);
    }
}