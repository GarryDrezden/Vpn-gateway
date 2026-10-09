using System.Net;
using SelectiveVpnRouter.Core;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests.OpenVpn;

public class OpenVpnPushReplyDnsParserTests
{
    [Fact]
    public void A_single_dhcp_option_dns()
    {
        IReadOnlyList<IPAddress> list = OpenVpnPushReplyDnsParser.ParseIpv4DnsServersFromLogLine(
            "PUSH: Received control message: 'PUSH_REPLY,dhcp-option DNS 1.1.1.1'");

        Assert.Single(list);
        Assert.Equal("1.1.1.1", list[0].ToString());
    }

    [Fact]
    public void B_multiple_dns_values()
    {
        IReadOnlyList<IPAddress> list = OpenVpnPushReplyDnsParser.ParseIpv4DnsServersFromLogLine(
            "PUSH_REPLY,dhcp-option DNS 1.1.1.1,dhcp-option DNS 1.0.0.1");

        Assert.Equal(2, list.Count);
        Assert.Equal("1.1.1.1", list[0].ToString());
        Assert.Equal("1.0.0.1", list[1].ToString());
    }

    [Fact]
    public void C_duplicates_deduplicated_preserving_order()
    {
        IReadOnlyList<IPAddress> list = OpenVpnPushReplyDnsParser.ParsePushPayloadIpv4Dns(
            "dhcp-option DNS 1.1.1.1,dhcp-option DNS 1.0.0.1,dhcp-option DNS 1.1.1.1");

        Assert.Equal(2, list.Count);
        Assert.Equal("1.1.1.1", list[0].ToString());
        Assert.Equal("1.0.0.1", list[1].ToString());
    }

    [Fact]
    public void D_invalid_dns_ignored()
    {
        IReadOnlyList<IPAddress> list = OpenVpnPushReplyDnsParser.ParsePushPayloadIpv4Dns(
            "dhcp-option DNS not-an-ip,dhcp-option DNS 1.1.1.1");

        Assert.Single(list);
        Assert.Equal("1.1.1.1", list[0].ToString());
    }

    [Fact]
    public void E_ipv6_pushed_dns_ignored_in_v1()
    {
        IReadOnlyList<IPAddress> list = OpenVpnPushReplyDnsParser.ParsePushPayloadIpv4Dns(
            "dhcp-option DNS 2001:db8::1,dhcp-option DNS 1.1.1.1");

        Assert.Single(list);
        Assert.Equal("1.1.1.1", list[0].ToString());
    }

    [Fact]
    public void F_unrelated_push_options_ignored()
    {
        IReadOnlyList<IPAddress> list = OpenVpnPushReplyDnsParser.ParsePushPayloadIpv4Dns(
            "route 10.0.0.0 255.255.0.0,redirect-gateway def1,block-outside-dns,topology subnet,"
            + "ifconfig 10.28.0.6 255.255.252.0,dhcp-option DNS 8.8.4.4");

        Assert.Single(list);
        Assert.Equal("8.8.4.4", list[0].ToString());
    }

    [Fact]
    public void G_no_dns_returns_empty()
    {
        IReadOnlyList<IPAddress> list = OpenVpnPushReplyDnsParser.ParsePushPayloadIpv4Dns(
            "route 10.0.0.0 255.255.0.0,redirect-gateway def1");

        Assert.Empty(list);
    }

    [Fact]
    public void H_repeated_push_reply_replaces_session_values()
    {
        var store = new SelectiveVpnRouter.Network.VpnSessionDnsStore();
        store.ApplyFromOpenVpnLogLine("PUSH_REPLY,dhcp-option DNS 1.1.1.1,dhcp-option DNS 1.0.0.1");
        store.ApplyFromOpenVpnLogLine("PUSH_REPLY,dhcp-option DNS 9.9.9.9");

        IReadOnlyList<IPAddress> list = store.GetIpv4DnsServers();
        Assert.Single(list);
        Assert.Equal("9.9.9.9", list[0].ToString());
    }

    [Fact]
    public void Push_reply_log_line_without_ipv4_dns_is_empty_parse()
    {
        IReadOnlyList<IPAddress> list = OpenVpnPushReplyDnsParser.ParseIpv4DnsServersFromLogLine(
            "PUSH_REPLY,route-gateway 10.28.0.1,topology subnet,redirect-gateway def1");

        Assert.Empty(list);
    }

    [Fact]
    public void Golden_real_provider_push_reply_shape()
    {
        const string line =
            "PUSH: Received control message: 'PUSH_REPLY,"
            + "dhcp-option DNS 1.1.1.1,"
            + "dhcp-option DNS 1.0.0.1,"
            + "dhcp-option DNS 9.9.9.10,"
            + "dhcp-option DNS 149.112.112.10'";

        IReadOnlyList<IPAddress> list = OpenVpnPushReplyDnsParser.ParseIpv4DnsServersFromLogLine(line);

        Assert.Equal(
            ["1.1.1.1", "1.0.0.1", "9.9.9.10", "149.112.112.10"],
            list.Select(a => a.ToString()).ToArray());
    }
}
