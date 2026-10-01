using System.Net;
using System.Net.Sockets;

namespace SelectiveVpnRouter.Core;

public static class IpcProtocol
{
    public const int CurrentVersion = 1;
}

public sealed record IpcRequest
{
    public int Version { get; init; } = IpcProtocol.CurrentVersion;
    public required string Id { get; init; }
    public required string Method { get; init; }
    public string? PayloadJson { get; init; }
}

public sealed record IpcResponse
{
    public required string Id { get; init; }
    public bool Ok { get; init; }
    public string? Error { get; init; }
    public string? PayloadJson { get; init; }
}

public sealed record ServiceSnapshot
{
    public bool ServiceAlive { get; init; } = true;
    public bool RoutingPaused { get; init; }
    public bool DriverLoaded { get; init; }
    public bool TransparentRedirectActive { get; init; }
    public OpenVpnLiveStatus Vpn { get; init; } = new();
    public AdapterLiveStatus? VpnAdapter { get; init; }
    public VpnAdapterSelectionDiagnostics? VpnAdapterSelection { get; init; }
    public AdapterLiveStatus? DirectAdapter { get; init; }
    public IReadOnlyList<FlowEvent> Flows { get; init; } = [];
    public IReadOnlyList<DiagnosticResult> LastDiagnostics { get; init; } = [];
    public IReadOnlyList<OwnedRoute> OwnedRoutes { get; init; } = [];
    public DefaultRouteSnapshot? PreferredDefault { get; init; }
    public OwnedRoute? OwnedTransportDefault { get; init; }
    public CalloutArmStatus Callout { get; init; } = new();
    public TransparentProxyDiagnostics ProxyDiagnostics { get; init; } = new();
    public WfpPolicyDiagnostics WfpPolicy { get; init; } = WfpPolicyDiagnostics.Empty;
    public string Ipv6PolicyNote { get; init; } = "";
    public string UdpNote { get; init; } = "UDP/QUIC per-process routing is unsupported in this MVP (TCP only).";
}

public sealed record OpenVpnLiveStatus
{
    public bool Running { get; init; }
    public bool Connected { get; init; }
    public int? Pid { get; init; }
    public DateTimeOffset? ConnectedSince { get; init; }
    public string? Gateway { get; init; }
    public IReadOnlyList<string> RecentLog { get; init; } = [];
}

public sealed record AdapterLiveStatus
{
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public int? Ipv4Index { get; init; }
    public IReadOnlyList<string> Ipv4 { get; init; } = [];
    public IReadOnlyList<string> Ipv6 { get; init; } = [];
}

public sealed record VpnAdapterSelectionDiagnostics
{
    public int IfIndex { get; init; }
    public string Name { get; init; } = "";
    public string Ipv4 { get; init; } = "";
    public string Ipv4State { get; init; } = "";
    public string Gateway { get; init; } = "";
    public string SelectionReason { get; init; } = "";
}

public sealed record DiagnosticResult
{
    public required string Name { get; init; }
    public required string Outcome { get; init; }
    public required string Message { get; init; }
    public DateTimeOffset Time { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record ConnectVpnRequest
{
    public string? OpenVpnPath { get; init; }
    public string? ProfilePath { get; init; }
    public bool? DisableDco { get; init; }
}

public sealed record DefaultRouteSnapshot
{
    public int InterfaceIndex { get; init; }
    public uint Metric { get; init; }
    public string NextHop { get; init; } = "";
    public string AdapterName { get; init; } = "";
    public bool IsVpnAdapter { get; init; }
}

public sealed record CalloutArmStatus
{
    public bool DeviceOpen { get; init; }
    public bool Enabled { get; init; }
    public uint ProxyPid { get; init; }
    public ushort ProxyPort { get; init; }
    public uint CalloutId { get; init; }
    public uint OpenHandles { get; init; }
    /// <summary>Legacy field: successful FwpsApplyModifiedLayerData count.</summary>
    public uint Redirects { get; init; }
    public uint RedirectAttempts { get; init; }
    public uint RedirectApplySuccess { get; init; }
    public uint RedirectApplyFailures { get; init; }
    public int LastRedirectApplyStatus { get; init; }
    public uint ClassifyEntries { get; init; }
    public uint ExitNoActionWrite { get; init; }
    public uint ExitDisabled { get; init; }
    public uint ExitProxyPidZero { get; init; }
    public uint ExitProxyPortZero { get; init; }
    public uint ExitRedirectHandleNull { get; init; }
    public uint ExitClassifyContextNull { get; init; }
    public uint ExitPidZero { get; init; }
    public uint ExitProxyPid { get; init; }
    public uint AcquireClassifyHandleFailures { get; init; }
    public uint AcquireWritableLayerDataFailures { get; init; }
    public uint AlreadyLoopbackProxy { get; init; }
    public uint AllocationFailures { get; init; }
    public ulong LastClassifyPid { get; init; }
    public ulong LastFilterId { get; init; }
    public uint LastRights { get; init; }
    public uint StatusStructVersion { get; init; }
    public uint RuntimeCaptureCount { get; init; }
    public bool RuntimeAppIdPresent { get; init; }
    public uint RuntimeAppIdByteLength { get; init; }
    public uint RuntimeAppIdValueType { get; init; }
    public ulong RuntimeProcessId { get; init; }
    public ulong RuntimeFilterId { get; init; }
    public uint RuntimeRights { get; init; }
    public string RuntimeAppId { get; init; } = "";
}

public sealed record TransparentProxyDiagnostics
{
    public uint AcceptedConnections { get; init; }
    public uint RedirectContextQueries { get; init; }
    public uint RedirectContextSuccess { get; init; }
    public uint RedirectContextFailures { get; init; }
    public int LastRedirectContextError { get; init; }
}

public static class DiagnosticText
{
    public static string Bilingual(string english, string russian) => english + " / " + russian;
}

public static class DiagnosticOutcomes
{
    public const string Pass = "PASS";
    public const string Fail = "FAIL";
    public const string Warning = "WARNING";
}

public static class IpcMethods
{
    public const string GetStatus = "GetStatus";
    public const string GetConfig = "GetConfig";
    public const string SetConfig = "SetConfig";
    public const string ConnectVpn = "ConnectVpn";
    public const string DisconnectVpn = "DisconnectVpn";
    public const string PauseRouting = "PauseRouting";
    public const string ResumeRouting = "ResumeRouting";
    public const string EmergencyRestore = "EmergencyRestore";
    public const string RunDiagnostic = "RunDiagnostic";
    public const string ExportDiagnostics = "ExportDiagnostics";
    public const string GetFlows = "GetFlows";
    public const string ApplyTempAppVpnRoute = "ApplyTempAppVpnRoute";
    public const string RemoveTempAppVpnRoute = "RemoveTempAppVpnRoute";
    public const string GetTempAppVpnStatus = "GetTempAppVpnStatus";
    public const string GetTempAppVpnFlows = "GetTempAppVpnFlows";
}

public static class VpnConnectBudget
{
    public const int OpenVpnStartupMs = 45_000;
    public const int VpnAdapterReadinessMs = 15_000;
    public const int ConnectVpnSetupMs = 10_000;

    public const int TotalOperationMs =
        OpenVpnStartupMs + VpnAdapterReadinessMs + ConnectVpnSetupMs;
}

public static class IpcTimeouts
{
    public const int PipeConnectMs = 5_000;
    public const int ShortOperationMs = 15_000;
    public const int ConnectVpnIpcMarginMs = 15_000;
    public const int ConnectVpnMs = VpnConnectBudget.TotalOperationMs + ConnectVpnIpcMarginMs;
    public const int LongOperationMs = 60_000;

    public static int OperationTimeoutMs(string method) =>
        method switch
        {
            IpcMethods.ConnectVpn => ConnectVpnMs,
            IpcMethods.DisconnectVpn => LongOperationMs,
            IpcMethods.EmergencyRestore => LongOperationMs,
            IpcMethods.RunDiagnostic => LongOperationMs,
            IpcMethods.ExportDiagnostics => LongOperationMs,
            _ => ShortOperationMs,
        };
}

public sealed class VpnTunnelNotReadyException : InvalidOperationException
{
    public int ReadinessTimeoutMs { get; }

    public VpnTunnelNotReadyException(string diagnostics, int readinessTimeoutMs)
        : base(diagnostics)
    {
        ReadinessTimeoutMs = readinessTimeoutMs;
    }

    public static bool IsReadinessFailureMessage(string? message) =>
        !string.IsNullOrWhiteSpace(message)
        && message.StartsWith("VPN tunnel adapter is not ready after ", StringComparison.Ordinal);
}

public sealed class IpcTimeoutException : TimeoutException
{
    public const string ConnectVpnUserMessage =
        "Служба не ответила на запрос подключения VPN вовремя.";

    public string Method { get; }
    public int TimeoutMs { get; }

    public IpcTimeoutException(string method, int timeoutMs)
        : base($"IPC '{method}' timed out after {timeoutMs} ms.")
    {
        Method = method;
        TimeoutMs = timeoutMs;
    }
}
