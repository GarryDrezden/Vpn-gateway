using SelectiveVpnRouter.Core;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public class DiagnosticUserExecutablePathsTests
{
    [Fact]
    public void Explicit_path_wins_over_configured()
    {
        string temp = Path.Combine(Path.GetTempPath(), "svr-tg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string exe = Path.Combine(temp, "Telegram.exe");
        File.WriteAllText(exe, "x");
        try
        {
            var cfg = new AppConfiguration { Rules = [] };
            Assert.True(DiagnosticUserExecutablePaths.TryResolveTelegramExecutable(cfg, exe, out string resolved, out string source));
            Assert.Equal(exe, resolved, StringComparer.OrdinalIgnoreCase);
            Assert.Equal("explicit-exePath", source);
        }
        finally
        {
            try { Directory.Delete(temp, true); } catch { }
        }
    }

    [Fact]
    public void Configured_vpn_app_path_is_used()
    {
        string temp = Path.Combine(Path.GetTempPath(), "svr-tg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string exe = Path.Combine(temp, "Telegram.exe");
        File.WriteAllText(exe, "x");
        try
        {
            var rule = new RoutingRule
            {
                Id = Guid.NewGuid(),
                Type = RuleType.Application,
                Target = exe,
                Mode = RouteMode.Vpn,
                Enabled = true,
                Name = "Telegram",
            };
            var cfg = new AppConfiguration { Rules = [rule] };
            Assert.True(DiagnosticUserExecutablePaths.TryResolveTelegramExecutable(cfg, null, out string resolved, out string source));
            Assert.Equal(exe, resolved, StringComparer.OrdinalIgnoreCase);
            Assert.Equal("configured-vpn-app", source);
        }
        finally
        {
            try { Directory.Delete(temp, true); } catch { }
        }
    }

    [Theory]
    [InlineData("Public")]
    [InlineData("Default")]
    public void Skips_well_known_non_user_profiles(string name)
    {
        Assert.True(DiagnosticUserExecutablePaths.IsSkippedUserProfileDirectory(name));
    }
}