namespace SelectiveVpnRouter.Core.Portable;

public sealed record PublishRuntimeSnapshot(
    bool ProductInstalled,
    bool ProductWasRunning,
    string? ProductImagePath,
    bool DriverInstalled,
    bool DriverWasRunning,
    string? DriverImagePath);

public sealed record PublishRuntimeRestorePlan(
    bool StartDriver,
    bool StartProduct);