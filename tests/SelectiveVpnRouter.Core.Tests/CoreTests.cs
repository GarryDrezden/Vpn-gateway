using System.Net;
using SelectiveVpnRouter.Core;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public class RuleEvaluatorTests
{
    private static readonly string Cursor = @"C:\Users\me\AppData\Local\Programs\cursor\Cursor.exe";
    private static readonly string Git = @"C:\Program Files\Git\cmd\git.exe";

    [Fact]
    public void Direct_application_rule_beats_vpn_application_rule()
    {
        RoutingRule[] rules =
        [
            RoutingRule.Create(RuleType.Application, "cursor", Cursor, RouteMode.Vpn),
            RoutingRule.Create(RuleType.Application, "git", Git, RouteMode.Direct),
        ];

        Assert.Equal(RouteMode.Vpn, RuleEvaluator.MatchProcess(rules, Cursor).EffectiveMode);
        Assert.Equal(RouteMode.Direct, RuleEvaluator.MatchProcess(rules, Git).EffectiveMode);
    }

    [Fact]
    public void Child_git_does_not_inherit_cursor_vpn_policy()
    {
        RoutingRule[] rules =
        [
            RoutingRule.Create(RuleType.Application, "cursor", Cursor, RouteMode.Vpn),
        ];

        ProcessMatch git = RuleEvaluator.MatchProcess(rules, Git);
        Assert.Null(git.Rule);
        Assert.Equal(RouteMode.Direct, git.EffectiveMode);
    }

    [Fact]
    public void Bare_filename_git_rule_matches_any_git_exe()
    {
        RoutingRule[] rules =
        [
            RoutingRule.Create(RuleType.Application, "git", "git.exe", RouteMode.Direct),
            RoutingRule.Create(RuleType.Application, "cursor", Cursor, RouteMode.Vpn),
        ];

        Assert.Equal(RouteMode.Direct, RuleEvaluator.MatchProcess(rules, Git).EffectiveMode);
        Assert.Equal(RouteMode.Direct, RuleEvaluator.MatchProcess(rules, @"D:\tools\git.exe").EffectiveMode);
        Assert.Equal(RouteMode.Vpn, RuleEvaluator.MatchProcess(rules, Cursor).EffectiveMode);
    }

    [Fact]
    public void Full_path_rule_does_not_match_other_install()
    {
        RoutingRule[] rules =
        [
            RoutingRule.Create(RuleType.Application, "cursor", Cursor, RouteMode.Vpn),
        ];

        Assert.Null(RuleEvaluator.MatchProcess(rules, @"C:\Other\Cursor.exe").Rule);
    }

    [Fact]
    public void Domain_wildcard_and_cidr_are_destination_global()
    {
        RoutingRule domain = RoutingRule.Create(RuleType.Domain, "yt", "*.youtube.com", RouteMode.Vpn);
        RoutingRule cidr = RoutingRule.Create(RuleType.Cidr, "lab", "10.1.2.0/24", RouteMode.Vpn);

        Assert.NotNull(RuleEvaluator.MatchDestination([domain], "api.youtube.com", null));
        Assert.NotNull(RuleEvaluator.MatchDestination([domain], "youtube.com", null));
        Assert.Null(RuleEvaluator.MatchDestination([domain], "notyoutube.com", null));
        Assert.NotNull(RuleEvaluator.MatchDestination([cidr], null, IPAddress.Parse("10.1.2.9")));
        Assert.Null(RuleEvaluator.MatchDestination([cidr], null, IPAddress.Parse("10.1.3.9")));
    }

    [Fact]
    public void Destination_direct_beats_destination_vpn()
    {
        RoutingRule[] rules =
        [
            RoutingRule.Create(RuleType.Domain, "all", "*.example.com", RouteMode.Vpn),
            RoutingRule.Create(RuleType.Domain, "exact", "gitlab.example.com", RouteMode.Direct),
        ];

        Assert.Equal(RouteMode.Direct, RuleEvaluator.MatchDestination(rules, "gitlab.example.com", null)!.Mode);
        Assert.Equal(RouteMode.Vpn, RuleEvaluator.MatchDestination(rules, "api.example.com", null)!.Mode);
    }
}

public class DestinationPlannerTests
{
    [Fact]
    public void Cidr_and_domain_routes_are_destination_owned_not_process()
    {
        RoutingRule[] rules =
        [
            RoutingRule.Create(RuleType.Cidr, "lab", "10.9.0.0/24", RouteMode.Vpn),
            RoutingRule.Create(RuleType.Domain, "api", "api.example.com", RouteMode.Vpn),
            RoutingRule.Create(RuleType.Domain, "exact", "api.example.com", RouteMode.Direct),
        ];
        var resolved = new Dictionary<string, IReadOnlyList<IPAddress>>
        {
            ["api.example.com"] = [IPAddress.Parse("203.0.113.9")],
        };
        IReadOnlyList<OwnedRoute> dest = DestinationRoutePlanner.PlanVpnDestinations(rules, 12, "10.8.0.1", resolved);
        Assert.Contains(dest, r => r.DestinationPrefix.StartsWith("10.9.0.0/", StringComparison.Ordinal));
        Assert.DoesNotContain(dest, r => r.DestinationPrefix.StartsWith("203.0.113.9", StringComparison.Ordinal));
    }
}

public class CrashStateTests
{
    [Fact]
    public void Crash_state_roundtrips_owned_routes()
    {
        string dir = Path.Combine(Path.GetTempPath(), "svr-crash-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "crash.json");
            var state = new CrashState
            {
                OpenVpnPid = 4242,
                ProxyPort = 1234,
                OwnedRoutes = [RouteReconciler.TransportDefault(7, "10.8.0.1")],
            };
            ConfigSerializer.SaveCrashState(path, state);
            CrashState loaded = ConfigSerializer.LoadCrashState(path);
            Assert.Equal(4242, loaded.OpenVpnPid);
            Assert.Single(loaded.OwnedRoutes);
            ConfigSerializer.ClearCrashState(path);
            Assert.Empty(ConfigSerializer.LoadCrashState(path).OwnedRoutes);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}

public class GatewayParserTests
{
    [Fact]
    public void Parses_push_gateway_and_tun_ifconfig()
    {
        Assert.Equal("10.8.0.1", OpenVpnStateParser.TryParseRouteGateway("PUSH_REPLY,route-gateway 10.8.0.1,ifconfig 10.8.0.2 255.255.255.0"));
        (string? local, string? peerOrMask)? tun = OpenVpnStateParser.TryParseIfconfig("ifconfig 10.8.0.6 10.8.0.5");
        Assert.Equal("10.8.0.6", tun?.local);
        Assert.Equal("10.8.0.5", tun?.peerOrMask);
    }
}

public class NormalizerTests
{
    [Fact]
    public void Normalizes_quotes_slashes_and_case()
    {
        Assert.Equal(@"c:\app\cursor.exe", ExecutablePathNormalizer.Normalize("\"C:/App/Cursor.exe\""));
    }

    [Fact]
    public void Bare_filename_is_not_resolved_against_cwd()
    {
        Assert.Equal("git.exe", ExecutablePathNormalizer.Normalize("git.exe"));
        Assert.Equal("git.exe", ExecutablePathNormalizer.FileName(@"C:\Program Files\Git\cmd\git.exe"));
    }
}

public class DnsCacheTests
{
    [Fact]
    public void Expires_entries()
    {
        var cache = new DnsCache(TimeSpan.FromMinutes(5));
        cache.Set("Example.COM", [IPAddress.Parse("1.2.3.4")], TimeSpan.FromSeconds(1));
        Assert.NotNull(cache.Get("example.com"));
        Assert.Null(cache.Get("example.com", DateTimeOffset.UtcNow.AddSeconds(2)));
    }
}

public class RouteReconcilerTests
{
    [Fact]
    public void Adds_and_removes_only_owned_differences()
    {
        OwnedRoute a = RouteReconciler.TransportDefault(12, "10.8.0.1");
        OwnedRoute b = RouteReconciler.HostRoute("1.2.3.4", 12, "10.8.0.1", "domain:example.com");
        RoutePlan plan = RouteReconciler.Plan([a, b], [a]);
        Assert.Single(plan.ToAdd);
        Assert.Empty(plan.ToRemove);
        plan = RouteReconciler.Plan([a], [a, b]);
        Assert.Empty(plan.ToAdd);
        Assert.Single(plan.ToRemove);
    }
}

public class ConfigTests
{
    [Fact]
    public void Roundtrips_configuration()
    {
        string dir = Path.Combine(Path.GetTempPath(), "svr-test-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "config.json");
            var cfg = new AppConfiguration
            {
                Vpn = new VpnProfileSettings { ProfilePath = @"D:\vpn\a.ovpn", CompatibilityDisableDco = true },
                Rules = [RoutingRule.Create(RuleType.Application, "c", @"C:\Cursor.exe", RouteMode.Vpn)],
            };
            ConfigSerializer.Save(path, cfg);
            AppConfiguration loaded = ConfigSerializer.LoadOrDefault(path);
            Assert.True(loaded.Vpn.CompatibilityDisableDco);
            Assert.Single(loaded.Rules);
            Assert.Equal(RuleType.Application, loaded.Rules[0].Type);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

public class OpenVpnParserTests
{
    [Fact]
    public void Parses_connected_and_version()
    {
        Assert.True(OpenVpnStateParser.IsConnected("Initialization Sequence Completed"));
        Assert.Equal("OpenVPN 2.6.6 [git:foo]", OpenVpnStateParser.TryParseVersionLine("banner\nOpenVPN 2.6.6 [git:foo]\n"));
        Assert.Equal("[REDACTED]", LogRedactor.Redact("Enter Auth Password: secret"));
    }
}

public class ProfileSafetyTests
{
    [Fact]
    public void Flags_local_redirect_gateway_not_comments()
    {
        ProfileSafety.Result r = ProfileSafety.Scan(
        [
            "# redirect-gateway def1",
            "client",
            "redirect-gateway def1",
            "<ca>",
            "route 1.2.3.4",
            "</ca>",
        ]);
        Assert.False(r.Ok);
        Assert.Contains(r.Findings, f => f.Directive == "redirect-gateway");
        Assert.DoesNotContain(r.Findings, f => f.Directive == "route");
    }
}

public class Ipv6PolicyTests
{
    [Fact]
    public void Default_policy_is_block_for_vpn_routed_apps()
    {
        Assert.Equal(Ipv6Policy.BlockForVpnRoutedApps, new VpnProfileSettings().Ipv6Policy);
    }
}
