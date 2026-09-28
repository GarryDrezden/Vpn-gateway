using SelectiveVpnRouter.Network;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public class WfpPolicyLayerAuditTests
{
    [Fact]
    public void Application_rules_install_only_tcp_layers()
    {
        string summary = WfpPolicyLayerAudit.Summary(driverPresent: true, ipv6Block: true);
        Assert.Contains("CONNECT_REDIRECT_V4", summary);
        Assert.Contains("AUTH_CONNECT_V6", summary);
        Assert.Contains("No UDP", summary);
    }
}