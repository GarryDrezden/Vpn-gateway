namespace SelectiveVpnRouter.Core;

public static class BasicDiagnosticsOrchestration
{
    public static readonly string[] SafeSteps =
    [
        "admin",
        "direct-if",
        "vpn-if",
        "openvpn-exe",
        "ovpn-profile",
        "dns",
        "ipv4",
        "callout-registration",
    ];

    private static readonly HashSet<string> DestructiveSteps = new(StringComparer.OrdinalIgnoreCase)
    {
        "driver-install",
        "driver-remove",
        "kill-service",
        "wdk-install",
        "testsigning-enable",
        "emergency-restore",
    };

    public static bool UsesOnlySafeSteps(IEnumerable<string> steps) =>
        steps.All(step => !DestructiveSteps.Contains(step));
}