namespace SelectiveVpnRouter.Core;

public enum RuleType
{
    Application = 0,
    Domain = 1,
    Cidr = 2,
}

public enum RouteMode
{
    Vpn = 0,
    Direct = 1,
}

public enum Ipv6Policy
{
    Auto = 0,
    VpnIfAvailable = 1,
    BlockForVpnRoutedApps = 2,
    AllowDirect = 3,
}

public enum FlowRoute
{
    Direct = 0,
    Vpn = 1,
    Blocked = 2,
    Unknown = 3,
}

public sealed record RoutingRule
{
    public required Guid Id { get; init; }
    public bool Enabled { get; init; } = true;
    public required RuleType Type { get; init; }
    public required string Name { get; init; }
    public required string Target { get; init; }
    public required RouteMode Mode { get; init; }

    public static RoutingRule Create(RuleType type, string name, string target, RouteMode mode)
        => new()
        {
            Id = Guid.NewGuid(),
            Enabled = true,
            Type = type,
            Name = name,
            Target = target,
            Mode = mode,
        };
}

public sealed record VpnProfileSettings
{
    public string OpenVpnPath { get; init; } = @"C:\Program Files\OpenVPN\bin\openvpn.exe";
    public string ProfilePath { get; init; } = "";
    public bool CompatibilityDisableDco { get; init; }
    public Ipv6Policy Ipv6Policy { get; init; } = Ipv6Policy.BlockForVpnRoutedApps;
    public string? PublicIpEndpoint { get; init; }
    public bool BlockQuicForVpnApps { get; init; }
}

public sealed record AppConfiguration
{
    public VpnProfileSettings Vpn { get; init; } = new();
    public IReadOnlyList<RoutingRule> Rules { get; init; } = [];
    public UiSettings Ui { get; init; } = new();
}

public sealed record UiSettings
{
    public bool MinimizeToTray { get; init; } = true;
    public bool StartMinimized { get; init; }
}

public sealed record OwnedRoute
{
    public required Guid Id { get; init; }
    public required string DestinationPrefix { get; init; }
    public required int InterfaceIndex { get; init; }
    public required string NextHop { get; init; }
    public required uint Metric { get; init; }
    public required string Reason { get; init; }
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record CrashState
{
    public int? OpenVpnPid { get; init; }
    public int? ProxyPort { get; init; }
    public IReadOnlyList<OwnedRoute> OwnedRoutes { get; init; } = [];
    public DateTimeOffset WrittenUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record FlowEvent
{
    public DateTimeOffset Time { get; init; } = DateTimeOffset.UtcNow;
    public string ProcessPath { get; init; } = "";
    public int Pid { get; init; }
    public string Destination { get; init; } = "";
    public int Port { get; init; }
    public string Protocol { get; init; } = "TCP";
    public string? RuleName { get; init; }
    public FlowRoute Route { get; init; }
    public string? LocalInterface { get; init; }
    public bool WfpRedirect { get; init; }
    public bool RedirectRecordsApplied { get; init; }
    public string Status { get; init; } = "new";
}
