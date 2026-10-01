using SelectiveVpnRouter.Core;
using SelectiveVpnRouter.Network;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public class WfpAppIdentityTests
{
    [Fact]
    public void ShortPathOnly_preserves_8dot3_input_without_GetFullPath_expansion()
    {
        const string shortInput = @"C:\Users\2C82~1\VPNROU~1\SELECT~1.EXE";
        IReadOnlyList<string> paths = WfpAppIdentity.GetIdentityPaths(shortInput, WfpAppIdentityPathMode.ShortPathOnly);
        Assert.Single(paths);
        Assert.Equal(shortInput, paths[0], StringComparer.OrdinalIgnoreCase);
        Assert.Contains('~', paths[0]);
    }

    [Fact]
    public void LongPathOnly_expands_to_full_path()
    {
        string temp = Path.Combine(Path.GetTempPath(), "svr-wfp-id-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            string exe = Path.Combine(temp, "probe.exe");
            File.WriteAllText(exe, "x");
            IReadOnlyList<string> paths = WfpAppIdentity.GetIdentityPaths(exe, WfpAppIdentityPathMode.LongPathOnly);
            Assert.Single(paths);
            Assert.Equal(Path.GetFullPath(exe), paths[0], StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(temp, true); } catch { }
        }
    }

    [Fact]
    public void Default_ascii_path_returns_single_long_identity()
    {
        string temp = Path.Combine(Path.GetTempPath(), "svr-ascii-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            string exe = Path.Combine(temp, "SelectiveVpnRouter.Probe.exe");
            File.WriteAllText(exe, "x");
            IReadOnlyList<string> paths = WfpAppIdentity.GetIdentityPaths(exe, WfpAppIdentityPathMode.Default);
            Assert.Single(paths);
        }
        finally
        {
            try { Directory.Delete(temp, true); } catch { }
        }
    }

    [Fact]
    public void SummarizeCalloutFilters_includes_long_and_short()
    {
        var filters = new[]
        {
            new WfpFilterInstallResult
            {
                ExePath = @"C:\Users\Test\app.exe",
                IdentityPathUsed = @"C:\Users\Test\app.exe",
                IsShortPathFallback = false,
                IsCalloutFilter = true,
                FilterId = 100,
            },
            new WfpFilterInstallResult
            {
                ExePath = @"C:\Users\Test\app.exe",
                IdentityPathUsed = @"C:\Users\2C82~1\APP~1.EXE",
                IsShortPathFallback = true,
                IsCalloutFilter = true,
                FilterId = 101,
            },
        };

        string summary = WfpPolicyHealth.SummarizeCalloutFilters(filters);
        Assert.Contains("long:id=100", summary, StringComparison.Ordinal);
        Assert.Contains("short:id=101", summary, StringComparison.Ordinal);
    }
}