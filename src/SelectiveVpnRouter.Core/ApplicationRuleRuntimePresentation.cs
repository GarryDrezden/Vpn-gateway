namespace SelectiveVpnRouter.Core;

public static class ApplicationRuleRuntimePresentation
{
    public const string DirectConfiguredStatus = "Напрямую";
    public const string InactiveStatus = "Неактивно";
    public const string DisabledStatus = "—";
    public const string AwaitingVpnStatus = "Ожидает VPN";
    public const string ActiveViaVpnStatus = "Активно через VPN";
    public const string ActiveDirectObservedStatus = "Активно напрямую";
    public const string MixedTrafficStatus = "Смешанный трафик";

    public static string ComputeRuntimeTrafficText(
        bool enabled,
        RouteMode mode,
        bool vpnConnected,
        string exePath,
        IEnumerable<FlowEvent> activeUserFlows)
    {
        if (!enabled)
        {
            return DisabledStatus;
        }

        if (mode == RouteMode.Direct)
        {
            return DirectConfiguredStatus;
        }

        if (!vpnConnected)
        {
            return AwaitingVpnStatus;
        }

        List<FlowEvent> mine = activeUserFlows
            .Where(f => ApplicationRulesHelper.PathsEqual(f.ProcessPath, exePath))
            .ToList();
        if (mine.Count == 0)
        {
            return InactiveStatus;
        }

        bool vpn = mine.Any(f => f.Route == FlowRoute.Vpn);
        bool direct = mine.Any(f => f.Route == FlowRoute.Direct);
        return vpn && direct
            ? MixedTrafficStatus
            : vpn
                ? ActiveViaVpnStatus
                : ActiveDirectObservedStatus;
    }

    public static string GetStatusBrushKey(bool enabled, string runtimeTrafficText)
    {
        if (!enabled)
        {
            return "StatusNeutral";
        }

        if (runtimeTrafficText.Contains("Смешан", StringComparison.Ordinal))
        {
            return "StatusWaiting";
        }

        if (runtimeTrafficText.Contains("Ожидает", StringComparison.Ordinal))
        {
            return "StatusWaiting";
        }

        if (runtimeTrafficText is InactiveStatus or DisabledStatus or DirectConfiguredStatus)
        {
            return "StatusNeutral";
        }

        if (runtimeTrafficText.Contains("Failed", StringComparison.Ordinal)
            || runtimeTrafficText.Contains("Ошиб", StringComparison.Ordinal))
        {
            return "StatusWaiting";
        }

        return "StatusSuccess";
    }
}