using System.Reflection;

namespace SelectiveVpnRouter.Core;

/// <summary>
/// Product release identity derived from assembly attributes (see Directory.Build.props).
/// RC revisions use file version 1.0.0.N — not a public SemVer 1.0.N release.
/// </summary>
public static class ProductVersionInfo
{
    private static readonly Assembly SourceAssembly = typeof(ProductVersionInfo).Assembly;

    public static string ProductVersion { get; } =
        ReadMetadata("VpnRouteProductVersion")
        ?? SourceAssembly.GetName().Version?.ToString(3)
        ?? "0.0.0";

    public static string ReleaseChannel { get; } = ReadMetadata("VpnRouteReleaseChannel") ?? "";

    public static int ReleaseRevision { get; } = ParseRevision(ReadMetadata("VpnRouteReleaseRevision"));

    public static string FileVersion { get; } =
        SourceAssembly.GetName().Version?.ToString() ?? "0.0.0.0";

    public static string DisplayVersion { get; } = BuildDisplayVersion(
        ProductVersion,
        ReleaseChannel,
        ReleaseRevision,
        SourceAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    public static ProductReleaseSnapshot ToSnapshot() => new()
    {
        ProductVersion = ProductVersion,
        ReleaseChannel = string.IsNullOrWhiteSpace(ReleaseChannel) ? null : ReleaseChannel,
        ReleaseRevision = ReleaseRevision,
        DisplayVersion = DisplayVersion,
        FileVersion = FileVersion,
    };

    internal static string BuildDisplayVersion(
        string productVersion,
        string releaseChannel,
        int releaseRevision,
        string? informationalVersion)
    {
        informationalVersion = StripSourceControlSuffix(informationalVersion);
        if (!string.IsNullOrWhiteSpace(informationalVersion)
            && !string.Equals(informationalVersion, productVersion, StringComparison.OrdinalIgnoreCase))
        {
            return informationalVersion.Trim();
        }

        if (releaseRevision > 0
            && !string.IsNullOrWhiteSpace(releaseChannel)
            && !string.Equals(releaseChannel, "stable", StringComparison.OrdinalIgnoreCase))
        {
            return $"{productVersion} {releaseChannel}{releaseRevision}";
        }

        return productVersion;
    }

    private static string? ReadMetadata(string key)
    {
        foreach (AssemblyMetadataAttribute attribute in SourceAssembly.GetCustomAttributes<AssemblyMetadataAttribute>())
        {
            if (string.Equals(attribute.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return attribute.Value;
            }
        }

        return null;
    }

    private static int ParseRevision(string? raw) =>
        int.TryParse(raw, out int revision) ? revision : 0;

    internal static string? StripSourceControlSuffix(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            return informationalVersion;
        }

        int plus = informationalVersion.IndexOf('+');
        return (plus >= 0 ? informationalVersion[..plus] : informationalVersion).Trim();
    }
}

public sealed record ProductReleaseSnapshot
{
    public string ProductVersion { get; init; } = "";
    public string? ReleaseChannel { get; init; }
    public int ReleaseRevision { get; init; }
    public string DisplayVersion { get; init; } = "";
    public string FileVersion { get; init; } = "";
}
