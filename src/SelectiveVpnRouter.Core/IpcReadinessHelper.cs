namespace SelectiveVpnRouter.Core;

public static class IpcReadinessHelper
{
    private static readonly string[] TransientPatterns =
    [
        "IPC response header EOF",
        "IPC response body EOF",
        "Unable to connect",
        "Pipe is broken",
        "broken pipe",
        "Broken pipe",
        "Pipe not found",
        "did not respond",
        "The pipe has been ended",
        "Cannot connect",
        "No process is on the other end of the pipe",
        "The system cannot find the file specified",
        "Timed out",
        "timeout",
        "IOException",
    ];

    public static bool IsTransientIpcError(string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            return false;
        }

        foreach (string pattern in TransientPatterns)
        {
            if (errorMessage.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}