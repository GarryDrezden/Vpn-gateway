namespace SelectiveVpnRouter.Service;

internal static class ServiceRuntimeContext
{
    private static int _hostStopping;

    internal static string? ActiveIpcMethod;
    internal static string? ActiveDiagnosticName;

    internal static bool HostStopping => Volatile.Read(ref _hostStopping) != 0;

    internal static void MarkHostStopping() => Interlocked.Exchange(ref _hostStopping, 1);

    internal static void SetIpcActivity(string? method, string? diagnosticName = null)
    {
        ActiveIpcMethod = method;
        ActiveDiagnosticName = diagnosticName;
    }

    internal static void ClearIpcActivity()
    {
        ActiveIpcMethod = null;
        ActiveDiagnosticName = null;
    }
}