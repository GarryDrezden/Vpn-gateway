using System.Net;
using System.Net.Sockets;
using SelectiveVpnRouter.Core;
using SelectiveVpnRouter.Network;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public class LoopbackRedirectBypassTests
{
    [Theory]
    [InlineData("198.51.100.1", false)]
    [InlineData("8.8.8.8", false)]
    [InlineData("127.0.0.1", true)]
    [InlineData("127.0.0.2", true)]
    [InlineData("127.255.255.254", true)]
    public void Ale_connect_redirect_bypasses_full_ipv4_loopback_range(string ipv4, bool bypass)
    {
        uint sinAddr = WfpAleConnectRedirectLoopback.ToSinAddrSUnSAddr(IPAddress.Parse(ipv4));
        Assert.Equal(bypass, WfpAleConnectRedirectLoopback.IsIpv4LoopbackSockAddrIn(sinAddr));
        Assert.Equal(bypass, WfpAleConnectRedirectLoopback.ShouldBypassAleConnectRedirect(IPAddress.Parse(ipv4)));
    }

    [Fact]
    public void External_and_loopback_decisions_are_independent_per_address()
    {
        Assert.False(WfpAleConnectRedirectLoopback.ShouldBypassAleConnectRedirect(IPAddress.Parse("1.1.1.1")));
        Assert.True(WfpAleConnectRedirectLoopback.ShouldBypassAleConnectRedirect(IPAddress.Parse("127.0.0.1")));
    }

    [Fact]
    public void Direct_app_loopback_still_classified_as_bypass()
    {
        var endpoint = new IPEndPoint(IPAddress.Parse("127.0.0.2"), 1455);
        Assert.Equal("loopback-destination", FlowStatusHelper.TryGetBypassReason(endpoint, socks: false, proxyPort: 19000));
        Assert.Equal(FlowLifecycle.LoopbackBypass, FlowStatusHelper.FormatBypassStatus("loopback-destination"));
    }

    [Fact]
    public void Ipv6_loopback_is_bypass_candidate_when_redirect_v6_exists()
    {
        if (!Socket.OSSupportsIPv6)
        {
            return;
        }

        Assert.True(WfpAleConnectRedirectLoopback.ShouldBypassAleConnectRedirect(IPAddress.IPv6Loopback));
    }

    [Fact]
    public void Loopback_bypass_does_not_imply_external_bypass()
    {
        Assert.True(FlowStatusHelper.IsBypass(FlowLifecycle.LoopbackBypass));
        Assert.False(FlowStatusHelper.IsBypass(FlowLifecycle.Accepted));
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.0.0.2")]
    [InlineData("127.255.255.254")]
    public void Flow_status_helper_treats_entire_127_slash_8_as_loopback(string ipv4)
    {
        Assert.True(FlowStatusHelper.IsLoopbackAddress(IPAddress.Parse(ipv4)));
        Assert.True(FlowStatusHelper.IsIpv4Loopback(IPAddress.Parse(ipv4)));
    }
}
