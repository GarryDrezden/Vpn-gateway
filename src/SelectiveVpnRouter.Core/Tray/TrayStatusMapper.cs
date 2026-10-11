namespace SelectiveVpnRouter.Core.Tray;

public static class TrayStatusMapper
{
    private const string ProductName = "VPN Route";

    public static TrayStatusPresentation Map(
        ServiceSnapshot? snapshot,
        bool serviceAvailable,
        bool connectUiInProgress)
    {
        if (!serviceAvailable || snapshot is null)
        {
            return Error("Служба недоступна");
        }

        if (IsDriverLoadError(snapshot))
        {
            return Error(ShortDetail(snapshot.DriverLoadError ?? "Ошибка драйвера"));
        }

        VpnResourceHealthReport health = VpnResourceHealthEvaluator.Evaluate(ToHealthSnapshot(snapshot));
        if (!health.Healthy)
        {
            return Error(ShortDetail(health.Summary));
        }

        bool connecting = connectUiInProgress
            || (!snapshot.VpnRoutingReady && snapshot.Vpn.Running);

        if (snapshot.VpnRoutingReady)
        {
            string status = snapshot.RoutingPaused ? "Подключен (пауза)" : "Подключен";
            return new TrayStatusPresentation(
                TrayVisualState.Active,
                $"{ProductName} — {status}",
                status,
                ConnectEnabled: false,
                DisconnectEnabled: !connectUiInProgress,
                ErrorSummary: null);
        }

        if (connecting)
        {
            return new TrayStatusPresentation(
                TrayVisualState.Connecting,
                $"{ProductName} — Подключение…",
                "Подключение…",
                ConnectEnabled: false,
                DisconnectEnabled: true,
                ErrorSummary: null);
        }

        return new TrayStatusPresentation(
            TrayVisualState.Ready,
            $"{ProductName} — Готов",
            "Готов",
            ConnectEnabled: true,
            DisconnectEnabled: false,
            ErrorSummary: null);
    }

    public static VpnResourceHealthSnapshot ToHealthSnapshot(ServiceSnapshot snapshot)
        => new()
        {
            VpnRoutingReady = snapshot.VpnRoutingReady,
            OpenVpnTunnelConnected = snapshot.Vpn.Connected,
            OpenVpnRunning = snapshot.Vpn.Running,
            OpenVpnPid = snapshot.Vpn.Pid,
            HasVpnAdapter = snapshot.VpnAdapter is not null,
            HasProxyListener = snapshot.ProxyPort is > 0,
            ProxyPort = snapshot.ProxyPort,
            HasOwnedTransportRoute = snapshot.OwnedTransportDefault is not null,
            OwnedRouteCount = snapshot.OwnedRoutes.Count,
            WfpSessionOpen = snapshot.Callout.DeviceOpen,
            WfpPolicyHealthy = snapshot.WfpPolicy.PolicyHealthy,
            ConfiguredAppFilterCount = snapshot.WfpPolicy.RequestedVpnApps,
            DriverLoaded = snapshot.DriverLoaded,
            DriverExpected = snapshot.DriverExpected,
            TransparentRedirectActive = snapshot.TransparentRedirectActive,
        };

    private static bool IsDriverLoadError(ServiceSnapshot snapshot)
        => snapshot.DriverExpected
           && !snapshot.DriverLoaded
           && !string.IsNullOrWhiteSpace(snapshot.DriverLoadError);

    private static TrayStatusPresentation Error(string detail)
        => new(
            TrayVisualState.Error,
            $"{ProductName} — Ошибка: {detail}",
            detail,
            ConnectEnabled: false,
            DisconnectEnabled: false,
            ErrorSummary: detail);

    private static string ShortDetail(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Неизвестная ошибка";
        }

        const int max = 96;
        string trimmed = value.Trim();
        int newline = trimmed.IndexOfAny(['\r', '\n']);
        if (newline >= 0)
        {
            trimmed = trimmed[..newline].Trim();
        }

        return trimmed.Length <= max ? trimmed : trimmed[..max] + "…";
    }
}
