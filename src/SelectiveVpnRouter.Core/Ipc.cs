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
    public AdapterLiveStatus? DirectAdapter { get; init; }
    public IReadOnlyList<FlowEvent> Flows { get; init; } = [];
    public IReadOnlyList<DiagnosticResult> LastDiagnostics { get; init; } = [];
    public IReadOnlyList<OwnedRoute> OwnedRoutes { get; init; } = [];
    public DefaultRouteSnapshot? PreferredDefault { get; init; }
    public OwnedRoute? OwnedTransportDefault { get; init; }
    public CalloutArmStatus Callout { get; init; } = new();
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
    public uint Redirects { get; init; }
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
}
