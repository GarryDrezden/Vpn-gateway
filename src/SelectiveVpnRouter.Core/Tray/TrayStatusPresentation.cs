namespace SelectiveVpnRouter.Core.Tray;

public sealed record TrayStatusPresentation(
    TrayVisualState VisualState,
    string Tooltip,
    string MenuStatusText,
    bool ConnectEnabled,
    bool DisconnectEnabled,
    string? ErrorSummary);
