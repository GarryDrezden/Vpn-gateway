namespace SelectiveVpnRouter.Core;

/// <summary>
/// Prepares isolated framework-dependent copies of SelectiveVpnRouter.Probe for diagnostics.
/// </summary>
public static class ProbeCopyHelper
{
    public const string ProbeExeName = "SelectiveVpnRouter.Probe.exe";
    public const string ProbeDllName = "SelectiveVpnRouter.Probe.dll";
    public const string ProbeDepsName = "SelectiveVpnRouter.Probe.deps.json";
    public const string ProbeRuntimeConfigName = "SelectiveVpnRouter.Probe.runtimeconfig.json";

    public static string? FindProbeSourceDirectory(string? baseDirectory = null)
    {
        foreach (string root in EnumerateProbeSearchRoots(baseDirectory))
        {
            if (HasMinimumPayload(root))
            {
                return root;
            }
        }

        return null;
    }

    public static IEnumerable<string> EnumerateProbeSearchRoots(string? baseDirectory = null)
    {
        baseDirectory ??= AppContext.BaseDirectory;
        yield return baseDirectory;

        string[] relativeDevRoots =
        [
            Path.Combine(baseDirectory, "..", "SelectiveVpnRouter"),
            Path.Combine(baseDirectory, "..", "..", "..", "..", "SelectiveVpnRouter.Probe", "bin", "Release", "net10.0-windows", "win-x64"),
            Path.Combine(baseDirectory, "..", "..", "..", "..", "SelectiveVpnRouter.Probe", "bin", "Release", "net10.0-windows"),
            Path.Combine(baseDirectory, "..", "..", "..", "..", "..", "src", "SelectiveVpnRouter.Probe", "bin", "Release", "net10.0-windows", "win-x64"),
            Path.Combine(baseDirectory, "..", "..", "..", "..", "..", "src", "SelectiveVpnRouter.Probe", "bin", "Release", "net10.0-windows"),
        ];

        foreach (string relative in relativeDevRoots)
        {
            string full = Path.GetFullPath(relative);
            if (Directory.Exists(full))
            {
                yield return full;
            }
        }
    }

    public static bool HasMinimumPayload(string directory)
    {
        return File.Exists(Path.Combine(directory, ProbeExeName))
            && File.Exists(Path.Combine(directory, ProbeDllName))
            && File.Exists(Path.Combine(directory, ProbeDepsName))
            && File.Exists(Path.Combine(directory, ProbeRuntimeConfigName));
    }

    /// <summary>
    /// Creates targetDirectory and copies the full Probe publish/build payload into it.
    /// Returns the path to ProbeExeName (name is never changed — apphost requires matching DLL).
    /// </summary>
    public static string PrepareProbeCopy(string targetDirectory)
    {
        string? source = FindProbeSourceDirectory()
            ?? throw new InvalidOperationException("SelectiveVpnRouter.Probe source directory not found.");

        Directory.CreateDirectory(targetDirectory);
        CopyPayload(source, targetDirectory);

        string exe = Path.Combine(targetDirectory, ProbeExeName);
        if (!File.Exists(exe))
        {
            throw new InvalidOperationException("Probe copy incomplete: " + ProbeExeName + " missing.");
        }

        if (!HasMinimumPayload(targetDirectory))
        {
            throw new InvalidOperationException("Probe copy incomplete: required framework-dependent files missing.");
        }

        return exe;
    }

    public static void CopyPayload(string sourceDirectory, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);
        foreach (string file in Directory.EnumerateFiles(sourceDirectory))
        {
            string name = Path.GetFileName(file);
            File.Copy(file, Path.Combine(targetDirectory, name), overwrite: true);
        }
    }

    public static void Cleanup(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch
        {
            // Best-effort: a running Probe may still hold files.
        }
    }
}

public sealed class ProbeRunResult
{
    public bool Launched { get; init; }
    public string Output { get; init; } = "";
    public string? LaunchError { get; init; }

    public bool LooksLikeRuntimeLaunchFailure =>
        Output.Contains("The application to execute does not exist", StringComparison.OrdinalIgnoreCase)
        || Output.Contains("Failed to load", StringComparison.OrdinalIgnoreCase)
        || Output.Contains("Could not load", StringComparison.OrdinalIgnoreCase);
}