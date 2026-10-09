using System.Net;
using System.Reflection;
using SelectiveVpnRouter.Network;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests.Network;

public class VpnSessionDnsStoreTests
{
    private const string PushLine =
        "PUSH_REPLY,dhcp-option DNS 1.1.1.1,dhcp-option DNS 1.0.0.1";

    [Fact]
    public void I_initial_state_empty()
    {
        var store = new VpnSessionDnsStore();
        Assert.Empty(store.GetIpv4DnsServers());
    }

    [Fact]
    public void J_negotiation_captures_dns_from_log_line()
    {
        var store = new VpnSessionDnsStore();
        store.ApplyFromOpenVpnLogLine(PushLine);
        Assert.Equal(2, store.GetIpv4DnsServers().Count);
    }

    [Fact]
    public void K_successful_session_exposes_dns()
    {
        var store = new VpnSessionDnsStore();
        store.ApplyFromOpenVpnLogLine(PushLine);
        Assert.Equal("1.1.1.1", store.GetIpv4DnsServers()[0].ToString());
    }

    [Fact]
    public void L_disconnect_clears_dns()
    {
        var store = new VpnSessionDnsStore();
        store.ApplyFromOpenVpnLogLine(PushLine);
        store.Clear();
        Assert.Empty(store.GetIpv4DnsServers());
    }

    [Fact]
    public void M_failed_connect_clears_dns()
    {
        var store = new VpnSessionDnsStore();
        store.ApplyFromOpenVpnLogLine(PushLine);
        store.Clear();
        Assert.Empty(store.GetIpv4DnsServers());
    }

    [Fact]
    public void N_reconnect_does_not_retain_old_dns_before_new_push()
    {
        var store = new VpnSessionDnsStore();
        store.ApplyFromOpenVpnLogLine(PushLine);
        store.Clear();
        Assert.Empty(store.GetIpv4DnsServers());
    }

    [Fact]
    public void O_new_session_replaces_previous_dns()
    {
        var store = new VpnSessionDnsStore();
        store.ApplyFromOpenVpnLogLine(PushLine);
        store.ApplyFromOpenVpnLogLine("PUSH_REPLY,dhcp-option DNS 9.9.9.10");
        Assert.Single(store.GetIpv4DnsServers());
        Assert.Equal("9.9.9.10", store.GetIpv4DnsServers()[0].ToString());
    }

    [Fact]
    public void Non_push_lines_do_not_populate()
    {
        var store = new VpnSessionDnsStore();
        store.ApplyFromOpenVpnLogLine("Initialization Sequence Completed");
        Assert.Empty(store.GetIpv4DnsServers());
    }

    [Fact]
    public void A_newer_push_reply_without_dns_clears_existing()
    {
        var store = new VpnSessionDnsStore();
        store.ApplyFromOpenVpnLogLine(PushLine);
        store.ApplyFromOpenVpnLogLine(
            "PUSH: Received control message: 'PUSH_REPLY,route-gateway 10.28.0.1,topology subnet,redirect-gateway def1'");

        Assert.Empty(store.GetIpv4DnsServers());
    }

    [Fact]
    public void B_newer_push_reply_malformed_dns_only_clears_existing()
    {
        var store = new VpnSessionDnsStore();
        store.ApplyFromOpenVpnLogLine(PushLine);
        store.ApplyFromOpenVpnLogLine("PUSH_REPLY,dhcp-option DNS not-an-ip,dhcp-option DNS also-bad");

        Assert.Empty(store.GetIpv4DnsServers());
    }

    [Fact]
    public void C_newer_push_reply_ipv6_dns_only_clears_existing()
    {
        var store = new VpnSessionDnsStore();
        store.ApplyFromOpenVpnLogLine(PushLine);
        store.ApplyFromOpenVpnLogLine("PUSH_REPLY,dhcp-option DNS 2001:db8::1");

        Assert.Empty(store.GetIpv4DnsServers());
    }

    [Fact]
    public void D_unrelated_log_line_after_dns_leaves_store_unchanged()
    {
        var store = new VpnSessionDnsStore();
        store.ApplyFromOpenVpnLogLine(PushLine);
        store.ApplyFromOpenVpnLogLine("Initialization Sequence Completed");

        Assert.Equal(2, store.GetIpv4DnsServers().Count);
        Assert.Equal("1.1.1.1", store.GetIpv4DnsServers()[0].ToString());
    }

    [Fact]
    public void E_newer_push_reply_replaces_with_exactly_new_dns_no_retention()
    {
        var store = new VpnSessionDnsStore();
        store.ApplyFromOpenVpnLogLine(PushLine);
        store.ApplyFromOpenVpnLogLine("PUSH_REPLY,dhcp-option DNS 8.8.8.8,dhcp-option DNS 8.8.4.4");

        Assert.Equal(
            ["8.8.8.8", "8.8.4.4"],
            store.GetIpv4DnsServers().Select(a => a.ToString()).ToArray());
    }

    [Fact]
    public void Same_openvpn_controller_process_renegotiation_clears_stale_dns()
    {
        var store = new VpnSessionDnsStore();
        var vpn = new OpenVpnController(store);
        EmitOpenVpnLogLine(vpn, PushLine);
        EmitOpenVpnLogLine(vpn, "PUSH_REPLY,route 10.28.0.0 255.255.252.0,redirect-gateway def1");

        Assert.Empty(store.GetIpv4DnsServers());
    }

    private static void EmitOpenVpnLogLine(OpenVpnController vpn, string line)
    {
        MethodInfo? handle = typeof(OpenVpnController).GetMethod("Handle", BindingFlags.Instance | BindingFlags.NonPublic);
        handle!.Invoke(vpn, [line]);
    }
}
