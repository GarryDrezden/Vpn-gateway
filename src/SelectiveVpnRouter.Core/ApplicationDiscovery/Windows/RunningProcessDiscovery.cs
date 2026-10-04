using System.Diagnostics;
using SelectiveVpnRouter.Core.ApplicationDiscovery;

namespace SelectiveVpnRouter.Core.ApplicationDiscovery.Windows;

internal static class RunningProcessDiscovery
{
    public static IEnumerable<DiscoveredApplicationCandidate> Collect(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            yield break;
        }

        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch (Exception)
        {
            yield break;
        }

        foreach (Process process in processes)
        {
            using (process)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string? exePath = TryGetMainModulePath(process);
                if (string.IsNullOrWhiteSpace(exePath))
                {
                    continue;
                }

                string displayName = ApplicationRulesHelper.ResolveFriendlyAppName(exePath);
                yield return new DiscoveredApplicationCandidate
                {
                    ExecutablePath = exePath,
                    DisplayName = displayName,
                    Source = DiscoverySource.RunningProcess,
                    IsRunning = true,
                    LaunchConfidence = ApplicationDiscoveryLaunchConfidence.Low,
                };
            }
        }
    }

    private static string? TryGetMainModulePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch (Exception)
        {
            return null;
        }
    }
}