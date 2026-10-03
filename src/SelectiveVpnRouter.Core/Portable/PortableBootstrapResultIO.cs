using System.Text.Json;

namespace SelectiveVpnRouter.Core.Portable;

public static class PortableBootstrapResultIO
{
    public static string LastResultPath()
    {
        string? overridePath = Environment.GetEnvironmentVariable("VPN_ROUTE_BOOTSTRAP_LAST_RESULT_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return Path.GetFullPath(overridePath);
        }

        return Path.Combine(AppPaths.RuntimeDirectory, "bootstrap-last-result.json");
    }

    public static void WriteLastResult(
        string command,
        PortableBootstrapCommandResult result,
        PortableIpcSmokeResult? ipcSmoke = null)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.RuntimeDirectory);
            var doc = new PortableBootstrapLastResult
            {
                Command = command,
                ExitCode = result.ExitCode,
                Message = result.Message,
                Status = result.Status,
                CompletedAt = DateTimeOffset.UtcNow,
                IpcAttempts = ipcSmoke?.Attempts ?? result.IpcAttempts,
                LastIpcError = ipcSmoke?.LastError ?? result.LastIpcError,
                IpcElapsedMs = ipcSmoke?.ElapsedMs ?? result.IpcElapsedMs,
            };
            File.WriteAllText(LastResultPath(), JsonSerializer.Serialize(doc, ConfigSerializer.JsonOptions));
        }
        catch (Exception)
        {
        }
    }

    public static PortableBootstrapLastResult? TryReadLastResult()
    {
        string path = LastResultPath();
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<PortableBootstrapLastResult>(
                File.ReadAllText(path),
                ConfigSerializer.JsonOptions);
        }
        catch (Exception)
        {
            return null;
        }
    }
}