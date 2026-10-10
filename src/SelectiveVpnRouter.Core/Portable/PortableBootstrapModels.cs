namespace SelectiveVpnRouter.Core.Portable;

public enum PortableBootstrapState
{
    Unknown = 0,
    Ready = 1,
    NeedsServiceRegistration = 2,
    NeedsDriverRegistration = 3,
    NeedsRepair = 4,
    VersionMismatch = 5,
    Broken = 6,
}

public enum PortableBootstrapLaunchOutcome
{
    Ready = 0,
    ElevationCancelled = 1,
    BootstrapLaunchFailed = 2,
    RepairIncomplete = 3,
    DriverSigningBlocked = 4,
    IpcFailed = 5,
    InvalidPortableRoot = 6,
    ElevationRequired = 7,
}

public sealed record PortableManifestDocument
{
    public string Product { get; init; } = "VPN Route";
    public string ProductVersion { get; init; } = "";
    public string? DisplayVersion { get; init; }
    public string? ReleaseChannel { get; init; }
    public int? ReleaseRevision { get; init; }
    public string? FileVersion { get; init; }
    public string Architecture { get; init; } = "x64";
    public string BuildCommit { get; init; } = "";
    public string ServiceVersion { get; init; } = "";
    public string CoreVersion { get; init; } = "";
    public string DriverVersion { get; init; } = "";
    public int PackageFormatVersion { get; init; } = PortableLayout.PackageFormatVersion;
}

public sealed record PortablePackageStamp
{
    public string ProductVersion { get; init; } = "";
    public string BuildCommit { get; init; } = "";
    public string ServicePath { get; init; } = "";
    public DateTimeOffset InstalledAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record PortableBootstrapStatus
{
    public PortableBootstrapState BootstrapState { get; init; } = PortableBootstrapState.Unknown;
    public string Message { get; init; } = "";
    public string PortableRoot { get; init; } = "";
    public string PackageVersion { get; init; } = "";
    public string BuildCommit { get; init; } = "";
    public bool ServiceInstalled { get; init; }
    public bool ServiceRunning { get; init; }
    public string? RegisteredServicePath { get; init; }
    public string ExpectedServicePath { get; init; } = "";
    public bool ServicePathMatches { get; init; }
    public string? ServiceVersion { get; init; }
    public bool DriverInstalled { get; init; }
    public bool DriverRunning { get; init; }
    public string? DriverVersion { get; init; }
    public bool OpenVpnAvailable { get; init; }
    public string? OpenVpnPath { get; init; }
    public bool DriverSigningBlocked { get; init; }
}

public sealed record PortableBootstrapCommandResult
{
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string Message { get; init; } = "";
    public PortableBootstrapStatus? Status { get; init; }
    public int? IpcAttempts { get; init; }
    public string? LastIpcError { get; init; }
    public int? IpcElapsedMs { get; init; }
}

public sealed record PortableBootstrapLastResult
{
    public string Command { get; init; } = "";
    public int ExitCode { get; init; }
    public string Message { get; init; } = "";
    public PortableBootstrapStatus? Status { get; init; }
    public DateTimeOffset CompletedAt { get; init; } = DateTimeOffset.UtcNow;
    public int? IpcAttempts { get; init; }
    public string? LastIpcError { get; init; }
    public int? IpcElapsedMs { get; init; }
}

public sealed record PortableBootstrapLaunchResult
{
    public PortableBootstrapLaunchOutcome Outcome { get; init; }
    public string UserMessage { get; init; } = "";
    public PortableBootstrapStatus? Status { get; init; }
    public int? ProcessExitCode { get; init; }
}

public static class PortableBootstrapExitCodes
{
    public const int Success = 0;
    public const int Failed = 1;
    public const int InvalidArguments = 2;
    public const int ElevationRequired = 3;
    public const int DriverSigningBlocked = 4;
    public const int IpcFailed = 5;
    public const int ElevationCancelled = 6;
    public const int BootstrapLaunchFailed = 7;
    public const int ServiceRepairFailed = 8;
    public const int DriverRepairFailed = 9;

    /// <summary>Backward-compatible alias.</summary>
    public const int NotElevated = ElevationRequired;
}