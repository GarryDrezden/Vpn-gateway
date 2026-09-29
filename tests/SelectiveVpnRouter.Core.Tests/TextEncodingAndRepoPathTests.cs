using System.Text;
using SelectiveVpnRouter.Core;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public class TextEncodingBootstrapTests
{
    [Fact]
    public void GetEncoding_866_after_bootstrap_does_not_throw()
    {
        TextEncodingBootstrap.EnsureRegistered();
        Encoding encoding = Encoding.GetEncoding(866);
        Assert.NotNull(encoding);
        Assert.Equal(866, encoding.CodePage);
    }

    [Fact]
    public void Console_oem_encoding_is_available_after_bootstrap()
    {
        TextEncodingBootstrap.EnsureRegistered();
        Encoding encoding = TextEncodingBootstrap.GetConsoleOemEncoding();
        Assert.NotNull(encoding);
        Assert.True(encoding.CodePage > 0);
    }
}

public class RepoPathResolverTests
{
    [Fact]
    public void Finds_repo_root_from_test_output_directory()
    {
        string? repoRoot = RepoPathResolver.TryFindRepoRoot(out RepoLookupDiagnostics? diagnostics);
        Assert.NotNull(repoRoot);
        Assert.True(File.Exists(Path.Combine(repoRoot!, RepoPathResolver.SolutionFileName)));
        Assert.True(Directory.Exists(Path.Combine(repoRoot, "scripts")));
        Assert.True(Directory.Exists(Path.Combine(repoRoot, "src")));
        Assert.Null(diagnostics);
    }

    [Fact]
    public void Resolves_install_driver_script_from_repo()
    {
        ScriptLookupResult lookup = RepoPathResolver.ResolveScript("install-driver.ps1");
        Assert.True(lookup.Found);
        Assert.NotNull(lookup.ScriptPath);
        Assert.True(File.Exists(lookup.ScriptPath));
        Assert.EndsWith("install-driver.ps1", lookup.ScriptPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveScript_failure_includes_search_diagnostics()
    {
        ScriptLookupResult lookup = RepoPathResolver.ResolveScript("__missing-script__.ps1");
        if (lookup.Found)
        {
            return;
        }

        Assert.NotNull(lookup.Diagnostics);
        Assert.Contains("baseDir=", lookup.FormatFailureMessage(), StringComparison.Ordinal);
        Assert.Contains("currentDir=", lookup.FormatFailureMessage(), StringComparison.Ordinal);
        Assert.Contains("searched=", lookup.FormatFailureMessage(), StringComparison.Ordinal);
    }
}