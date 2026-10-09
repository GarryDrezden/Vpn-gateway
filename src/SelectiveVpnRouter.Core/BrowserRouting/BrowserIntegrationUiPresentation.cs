namespace SelectiveVpnRouter.Core.BrowserRouting;

/// <summary>Maps browser integration snapshot fields to App UI copy (testable, no WPF).</summary>
public static class BrowserIntegrationUiPresentation
{
    public sealed record ViewModel(
        string ExtensionStatus,
        string BrowserApi,
        string BrowserProxyStatus,
        string SocksEndpoint,
        string VpnEgressStatus,
        string VpnInterface,
        string RuleCount,
        string LastContact,
        string? DetailLine);

    public static ViewModel Map(BrowserIntegrationSnapshot? integration, DateTimeOffset utcNow)
    {
        if (integration is null)
        {
            return new ViewModel(
                "Нет данных",
                "—",
                "—",
                "—",
                "—",
                "—",
                "—",
                "—",
                null);
        }

        bool proxyReady = integration.BrowserProxy.Status == BrowserProxyStatus.Ready;
        bool egressReady = integration.VpnEgress.Status == BrowserIntegrationContract.VpnEgressStatus.Ready;
        string? detail = BuildDetailLine(proxyReady, egressReady, integration.BrowserProxy.Status);

        return new ViewModel(
            FormatExtensionStatus(integration.BrowserClient.Status),
            "v" + integration.IntegrationApiVersion,
            proxyReady ? "Готов" : "Недоступен",
            FormatSocks(integration.BrowserProxy),
            egressReady ? "Готов" : "Недоступен",
            FormatVpnInterface(integration.VpnEgress),
            integration.RuleCount.ToString(),
            FormatLastContact(integration.BrowserClient.LastSeenUtc, utcNow),
            detail);
    }

    public static string FormatExtensionStatus(string status) => status switch
    {
        BrowserIntegrationContract.BrowserClientStatus.NeverSeen => "Расширение ещё не обнаружено",
        BrowserIntegrationContract.BrowserClientStatus.RecentlySeen => "Расширение недавно активно",
        BrowserIntegrationContract.BrowserClientStatus.Stale => "Расширение давно не отвечало",
        _ => status,
    };

    public static string FormatLastContact(DateTimeOffset? lastSeenUtc, DateTimeOffset utcNow)
    {
        if (lastSeenUtc is null)
            return "никогда";

        TimeSpan elapsed = utcNow >= lastSeenUtc.Value ? utcNow - lastSeenUtc.Value : TimeSpan.Zero;
        if (elapsed < TimeSpan.FromMinutes(1))
            return "только что";
        if (elapsed < TimeSpan.FromHours(1))
            return ((int)elapsed.TotalMinutes).ToString() + " мин назад";
        if (elapsed < TimeSpan.FromDays(1))
            return ((int)elapsed.TotalHours).ToString() + " ч назад";
        return lastSeenUtc.Value.ToLocalTime().ToString("g");
    }

    private static string FormatSocks(BrowserIntegrationProxySnapshot proxy)
    {
        if (proxy.Status != BrowserProxyStatus.Ready || proxy.Endpoint is null)
            return "—";
        return proxy.Endpoint.Host + ":" + proxy.Endpoint.Port;
    }

    private static string FormatVpnInterface(BrowserIntegrationVpnEgressSnapshot egress)
    {
        if (egress.Status != BrowserIntegrationContract.VpnEgressStatus.Ready || egress.InterfaceIndex is null)
            return "—";
        if (!string.IsNullOrWhiteSpace(egress.InterfaceName))
            return egress.InterfaceName + " / #" + egress.InterfaceIndex;
        return "#" + egress.InterfaceIndex;
    }

    private static string? BuildDetailLine(bool proxyReady, bool egressReady, string proxyStatus)
    {
        if (proxyReady && !egressReady)
            return "Режим VPN-маршрутов: fail-closed (VPN не подключён)";
        if (proxyStatus == BrowserProxyStatus.Unavailable && !proxyReady)
            return "VPN-маршрутам требуется Browser Proxy";
        if (proxyReady && egressReady)
            return null;
        return null;
    }
}
