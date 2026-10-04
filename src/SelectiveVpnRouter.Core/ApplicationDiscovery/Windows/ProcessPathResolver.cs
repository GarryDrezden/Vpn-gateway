using System.Diagnostics;

namespace SelectiveVpnRouter.Core.ApplicationDiscovery.Windows;

public sealed class ProcessPathResolver : IProcessPathResolver
{
    public string? TryGetExecutablePath(int processId)
    {
        if (processId <= 0)
        {
            return null;
        }

        try
        {
            using Process process = Process.GetProcessById(processId);
            return process.MainModule?.FileName;
        }
        catch (Exception)
        {
            return null;
        }
    }
}