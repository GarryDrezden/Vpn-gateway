namespace SelectiveVpnRouter.Core.ApplicationDiscovery.Windows;

public interface IProcessPathResolver
{
    string? TryGetExecutablePath(int processId);
}