namespace SelectiveVpnRouter.Core;

public static class RealAppAcceptanceOutcome
{
    public const string DnsFail = "DNS_FAIL";
    public const string TcpRoutingPass = "TCP_ROUTING_PASS";
    public const string VpnEgressVerified = "VPN_EGRESS_VERIFIED";
    public const string DirectEgressVerified = "DIRECT_EGRESS_VERIFIED";

    public const string DnsNote =
        "Application DNS/name resolution failed while the temp VPN rule was active. " +
        "WFP app filters install only ALE_CONNECT_REDIRECT_V4 (+ optional IPv6 TCP block); " +
        "they do not filter UDP DNS. libcurl may report getaddrinfo() thread failed to start " +
        "when its threaded resolver cannot start; use Resolve-DnsName + curl --resolve to verify TCP/VPN egress separately.";

    public const string CurlTestSteps =
        "A) TEMP rule active: Resolve-DnsName api.ipify.org; curl.exe -4 https://api.ipify.org\r\n" +
        "B) If A DNS fails: curl.exe -4 --resolve \"api.ipify.org:443:<A-record>\" https://api.ipify.org\r\n" +
        "C) Remove TEMP rule: curl.exe -4 https://api.ipify.org\r\n" +
        "Interpret: DNS_FAIL vs TCP_ROUTING_PASS vs VPN_EGRESS_VERIFIED vs DIRECT_EGRESS_VERIFIED.";
}