namespace SelectiveVpnRouter.Core.ApplicationDiscovery.Windows;

public sealed record InteractiveWindowSnapshot(int ProcessId, string? WindowTitle, string? ClassName);