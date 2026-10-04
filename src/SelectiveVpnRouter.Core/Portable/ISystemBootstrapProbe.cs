namespace SelectiveVpnRouter.Core.Portable;

public sealed record ServiceProbeSnapshot
{
    public bool Installed { get; init; }
    public bool Running { get; init; }
    public string? ImagePath { get; init; }
    public string? Version { get; init; }
}

public sealed record DriverProbeSnapshot
{
    public bool Installed { get; init; }
    public bool Running { get; init; }
    public string? BinaryPath { get; init; }
    public string? Version { get; init; }
}

public interface ISystemBootstrapProbe
{
    ServiceProbeSnapshot ProbeProductService();
    DriverProbeSnapshot ProbeCalloutDriver();
    bool IsElevated();
    string? TryReadAuthenticodeStatus(string filePath);
    bool IsTestSigningEnabled();
}
