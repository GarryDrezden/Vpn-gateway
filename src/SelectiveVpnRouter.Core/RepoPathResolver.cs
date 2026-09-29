namespace SelectiveVpnRouter.Core;

public sealed record RepoLookupDiagnostics
{
    public required string BaseDirectory { get; init; }
    public required string CurrentDirectory { get; init; }
    public required IReadOnlyList<string> Searched { get; init; }
}

public sealed record ScriptLookupResult
{
    public bool Found { get; init; }
    public string? ScriptPath { get; init; }
    public string? RepoRoot { get; init; }
    public RepoLookupDiagnostics? Diagnostics { get; init; }

    public static ScriptLookupResult Success(string scriptPath, string? repoRoot = null) =>
        new() { Found = true, ScriptPath = scriptPath, RepoRoot = repoRoot };

    public static ScriptLookupResult Failure(RepoLookupDiagnostics diagnostics) =>
        new() { Found = false, Diagnostics = diagnostics };

    public string FormatFailureMessage()
    {
        if (Diagnostics is null)
        {
            return "repo root not found";
        }

        return "repo root not found" + Environment.NewLine
            + "baseDir=" + Diagnostics.BaseDirectory + Environment.NewLine
            + "currentDir=" + Diagnostics.CurrentDirectory + Environment.NewLine
            + "searched=" + string.Join("; ", Diagnostics.Searched);
    }
}

public static class RepoPathResolver
{
    public const string SolutionFileName = "SelectiveVpnRouter.sln";

    public static string? TryFindRepoRoot() => TryFindRepoRoot(out _);

    public static string? TryFindRepoRoot(out RepoLookupDiagnostics? diagnostics)
    {
        var allSearched = new List<string>();
        diagnostics = null;
        foreach (string start in EnumerateSearchStartPaths())
        {
            if (TryFindRepoRootFrom(start, out string? repoRoot, out IReadOnlyList<string> searched))
            {
                return repoRoot;
            }

            foreach (string path in searched)
            {
                if (!allSearched.Contains(path, StringComparer.OrdinalIgnoreCase))
                {
                    allSearched.Add(path);
                }
            }
        }

        diagnostics = new RepoLookupDiagnostics
        {
            BaseDirectory = AppContext.BaseDirectory,
            CurrentDirectory = Environment.CurrentDirectory,
            Searched = allSearched,
        };
        return null;
    }

    public static ScriptLookupResult ResolveScript(string scriptFileName)
    {
        string publishScript = Path.Combine(AppContext.BaseDirectory, "scripts", scriptFileName);
        if (File.Exists(publishScript))
        {
            return ScriptLookupResult.Success(publishScript);
        }

        string? repoRoot = TryFindRepoRoot(out RepoLookupDiagnostics? diagnostics);
        if (repoRoot is not null)
        {
            string repoScript = Path.Combine(repoRoot, "scripts", scriptFileName);
            if (File.Exists(repoScript))
            {
                return ScriptLookupResult.Success(repoScript, repoRoot);
            }
        }

        return ScriptLookupResult.Failure(diagnostics ?? new RepoLookupDiagnostics
        {
            BaseDirectory = AppContext.BaseDirectory,
            CurrentDirectory = Environment.CurrentDirectory,
            Searched = [],
        });
    }

    public static string? ResolveRepoRelativePath(params string[] relativeParts)
    {
        string? repoRoot = TryFindRepoRoot();
        return repoRoot is null ? null : Path.Combine([repoRoot, .. relativeParts]);
    }

    private static IEnumerable<string> EnumerateSearchStartPaths()
    {
        yield return AppContext.BaseDirectory;

        if (!string.IsNullOrWhiteSpace(Environment.ProcessPath))
        {
            string? processDir = Path.GetDirectoryName(Environment.ProcessPath);
            if (!string.IsNullOrWhiteSpace(processDir))
            {
                yield return processDir;
            }
        }

        if (!string.IsNullOrWhiteSpace(Environment.CurrentDirectory))
        {
            yield return Environment.CurrentDirectory;
        }
    }

    private static bool TryFindRepoRootFrom(string startPath, out string? repoRoot, out IReadOnlyList<string> searched)
    {
        var searchedPaths = new List<string>();
        var dir = new DirectoryInfo(Path.GetFullPath(startPath));
        while (dir is not null)
        {
            searchedPaths.Add(dir.FullName);
            if (IsRepoRoot(dir.FullName))
            {
                repoRoot = dir.FullName;
                searched = searchedPaths;
                return true;
            }

            dir = dir.Parent;
        }

        repoRoot = null;
        searched = searchedPaths;
        return false;
    }

    private static bool IsRepoRoot(string directory) =>
        File.Exists(Path.Combine(directory, SolutionFileName))
        && Directory.Exists(Path.Combine(directory, "scripts"))
        && Directory.Exists(Path.Combine(directory, "src"));
}