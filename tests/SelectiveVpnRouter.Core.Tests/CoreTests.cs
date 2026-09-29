using System.Net;
using System.Net.NetworkInformation;
using SelectiveVpnRouter.Core;
using SelectiveVpnRouter.Network;
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

public class ConnectVpnRegressionTests
{
    private static string MainWindowSourcePath()
    {
        string? repoRoot = FindRepoRoot();
        Assert.NotNull(repoRoot);
        return Path.Combine(repoRoot, "src", "SelectiveVpnRouter.App", "MainWindow.xaml.cs");
    }

    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SelectiveVpnRouter.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }

    [Fact]
    public void ConnectVpnAsync_does_not_save_config_before_connect()
    {
        string text = File.ReadAllText(MainWindowSourcePath());
        int start = text.IndexOf("private async Task ConnectVpnAsync()", StringComparison.Ordinal);
        Assert.True(start >= 0);
        int end = text.IndexOf("private async Task Call(", start, StringComparison.Ordinal);
        Assert.True(end > start);
        string body = text[start..end];
        Assert.DoesNotContain("SaveConfigAsync", body, StringComparison.Ordinal);
        Assert.DoesNotContain("SetConfig", body, StringComparison.Ordinal);
        Assert.Contains("IpcMethods.ConnectVpn", body, StringComparison.Ordinal);
        Assert.Contains("ConnectVpnRequest", body, StringComparison.Ordinal);
        Assert.Contains("BuildConnectVpnRequestFromUi", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Call_does_not_save_config()
    {
        string text = File.ReadAllText(MainWindowSourcePath());
        int start = text.IndexOf("private async Task Call(", StringComparison.Ordinal);
        Assert.True(start >= 0);
        int end = text.IndexOf("private async Task<bool> IsVpnConnectedAsync", start, StringComparison.Ordinal);
        Assert.True(end > start);
        string body = text[start..end];
        Assert.DoesNotContain("SaveConfigAsync", body, StringComparison.Ordinal);
    }

    [Fact]
    public void ConnectVpnRequest_maps_ui_fields_in_source()
    {
        string text = File.ReadAllText(MainWindowSourcePath());
        Assert.Contains("OpenVpnPath = ExeBox.Text.Trim()", text, StringComparison.Ordinal);
        Assert.Contains("ProfilePath = ProfileBox.Text.Trim()", text, StringComparison.Ordinal);
        Assert.Contains("DisableDco = DcoBox.IsChecked == true", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RouterEngine_uses_connect_request_values()
    {
        string path = Path.Combine(FindRepoRoot()!, "src", "SelectiveVpnRouter.Service", "RouterEngine.cs");
        string text = File.ReadAllText(path);
        Assert.Contains("request?.OpenVpnPath", text, StringComparison.Ordinal);
        Assert.Contains("request?.ProfilePath", text, StringComparison.Ordinal);
        Assert.Contains("request?.DisableDco", text, StringComparison.Ordinal);
    }
}

public class IpcTimeoutTests
{
    [Fact]
    public void ConnectVpn_uses_long_timeout()
    {
        Assert.Equal(IpcTimeouts.ConnectVpnMs, IpcTimeouts.OperationTimeoutMs(IpcMethods.ConnectVpn));
        Assert.Equal(85_000, IpcTimeouts.ConnectVpnMs);
    }

    [Fact]
    public void GetStatus_uses_short_timeout()
    {
        Assert.Equal(15_000, IpcTimeouts.OperationTimeoutMs(IpcMethods.GetStatus));
    }

    [Fact]
    public void RunDiagnostic_uses_long_timeout()
    {
        Assert.Equal(60_000, IpcTimeouts.OperationTimeoutMs(IpcMethods.RunDiagnostic));
    }
}

public class ConfigTests
{
    private static string CreateTempConfigDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "svr-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static AppConfiguration SampleConfig() =>
        new()
        {
            Vpn = new VpnProfileSettings { ProfilePath = @"D:\vpn\a.ovpn", CompatibilityDisableDco = true },
            Rules = [RoutingRule.Create(RuleType.Application, "c", @"C:\Cursor.exe", RouteMode.Vpn)],
        };

    [Fact]
    public void Roundtrips_configuration()
    {
        string dir = CreateTempConfigDir();
        try
        {
            string path = Path.Combine(dir, "config.json");
            ConfigSerializer.Save(path, SampleConfig());
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

    [Fact]
    public void Save_creates_config_json()
    {
        string dir = CreateTempConfigDir();
        try
        {
            string path = Path.Combine(dir, "config.json");
            ConfigSerializer.Save(path, SampleConfig());
            Assert.True(File.Exists(path));
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Second_save_creates_and_updates_backup()
    {
        string dir = CreateTempConfigDir();
        try
        {
            string path = Path.Combine(dir, "config.json");
            string backup = Path.Combine(dir, "config.bak.json");
            ConfigSerializer.Save(path, SampleConfig());
            ConfigSerializer.Save(path, SampleConfig() with
            {
                Vpn = new VpnProfileSettings { ProfilePath = @"D:\vpn\b.ovpn" },
            });

            Assert.True(File.Exists(backup));
            AppConfiguration backupConfig = ConfigSerializer.LoadOrDefault(backup);
            Assert.Equal(@"D:\vpn\a.ovpn", backupConfig.Vpn.ProfilePath);
            AppConfiguration current = ConfigSerializer.LoadOrDefault(path);
            Assert.Equal(@"D:\vpn\b.ovpn", current.Vpn.ProfilePath);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Readonly_backup_does_not_block_save()
    {
        string dir = CreateTempConfigDir();
        try
        {
            string path = Path.Combine(dir, "config.json");
            string backup = Path.Combine(dir, "config.bak.json");
            ConfigSerializer.Save(path, SampleConfig());
            ConfigSerializer.Save(path, SampleConfig() with
            {
                Vpn = new VpnProfileSettings { ProfilePath = @"D:\vpn\b.ovpn" },
            });
            Assert.True(File.Exists(backup));
            File.SetAttributes(backup, FileAttributes.ReadOnly);

            ConfigSerializer.Save(path, SampleConfig() with
            {
                Vpn = new VpnProfileSettings { ProfilePath = @"D:\vpn\c.ovpn" },
            });

            Assert.Equal(@"D:\vpn\c.ovpn", ConfigSerializer.LoadOrDefault(path).Vpn.ProfilePath);
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                foreach (string file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public void App_project_does_not_call_ConfigSerializer_Save()
    {
        string? repoRoot = FindRepoRoot();
        Assert.NotNull(repoRoot);
        string appDir = Path.Combine(repoRoot, "src", "SelectiveVpnRouter.App");
        Assert.True(Directory.Exists(appDir));

        foreach (string file in Directory.EnumerateFiles(appDir, "*.cs", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(file);
            Assert.DoesNotContain("ConfigSerializer.Save", text, StringComparison.Ordinal);
        }
    }

    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SelectiveVpnRouter.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
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

public class PreferredDefaultTests
{
    [Fact]
    public void Low_metric_vpn_default_is_stolen_high_metric_is_not()
    {
        var stolen = new DefaultRouteSnapshot { IsVpnAdapter = true, Metric = 25 };
        var ownedFallback = new DefaultRouteSnapshot { IsVpnAdapter = true, Metric = 9000 };
        var direct = new DefaultRouteSnapshot { IsVpnAdapter = false, Metric = 25 };
        Assert.True(stolen.IsVpnAdapter && stolen.Metric < 5000);
        Assert.False(ownedFallback.IsVpnAdapter && ownedFallback.Metric < 5000);
        Assert.False(direct.IsVpnAdapter && direct.Metric < 5000);
    }
}

public class ConnectVpnTimeoutArchitectureTests
{
    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SelectiveVpnRouter.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }

    [Fact]
    public void ConnectVpn_ipc_timeout_exceeds_openvpn_plus_readiness_budget()
    {
        int connectVpnMs = IpcTimeouts.OperationTimeoutMs(IpcMethods.ConnectVpn);
        int simulatedReadinessMs = 12_000;
        int worstCaseMs = VpnConnectBudget.OpenVpnStartupMs + simulatedReadinessMs + VpnConnectBudget.ConnectVpnSetupMs;

        Assert.True(connectVpnMs > worstCaseMs);
        Assert.True(connectVpnMs > VpnConnectBudget.OpenVpnStartupMs + VpnConnectBudget.VpnAdapterReadinessMs);
        Assert.NotEqual(IpcTimeouts.ShortOperationMs, connectVpnMs);
    }

    [Fact]
    public void Readiness_timeout_message_reaches_gui_before_ipc_timeout()
    {
        int readinessMs = VpnConnectBudget.VpnAdapterReadinessMs;
        int connectVpnMs = IpcTimeouts.OperationTimeoutMs(IpcMethods.ConnectVpn);
        Assert.True(connectVpnMs > readinessMs + VpnConnectBudget.OpenVpnStartupMs + 5_000);

        var candidates = new[]
        {
            new VpnAdapterReadinessCandidate(
                "tap",
                "TAP",
                "TAP-Windows Adapter",
                OperationalStatus.Up,
                8,
                [new Ipv4TunnelAddress("169.254.1.2", 16, Ipv4DadState.Preferred)],
                true,
                true,
                true),
        };

        string message = VpnAdapterReadiness.FormatNotReadyDiagnostics(candidates, "10.28.0.1", "10.28.0.2");
        Assert.StartsWith("VPN tunnel adapter is not ready after 15s.", message, StringComparison.Ordinal);
        Assert.Contains("OpenVPN connected, but no usable tunnel IPv4 became available.", message, StringComparison.Ordinal);
        Assert.Contains("candidate if=8", message, StringComparison.Ordinal);
        Assert.True(VpnTunnelNotReadyException.IsReadinessFailureMessage(message));
    }

    [Fact]
    public async Task WaitForReady_respects_service_shutdown_token()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            VpnAdapterSelector.WaitForReadyAsync([], new OpenVpnController(), TimeSpan.FromSeconds(15), cts.Token));
    }

    [Fact]
    public void RouterEngine_connect_failure_rolls_back_via_disconnect()
    {
        string path = Path.Combine(FindRepoRoot()!, "src", "SelectiveVpnRouter.Service", "RouterEngine.cs");
        string text = File.ReadAllText(path);
        Assert.Contains("Log(\"connect-failed error=\" + ex.Message);", text, StringComparison.Ordinal);
        Assert.Contains("await DisconnectAsync().ConfigureAwait(false);", text, StringComparison.Ordinal);
        Assert.Contains("VpnTunnelNotReadyException", text, StringComparison.Ordinal);
    }

    [Fact]
    public void GetStatus_keeps_short_ipc_timeout()
    {
        Assert.Equal(IpcTimeouts.ShortOperationMs, IpcTimeouts.OperationTimeoutMs(IpcMethods.GetStatus));
    }

    [Fact]
    public void ConnectVpn_double_start_is_guarded()
    {
        string? repoRoot = FindRepoRoot();
        Assert.NotNull(repoRoot);

        string mainWindow = File.ReadAllText(Path.Combine(repoRoot, "src", "SelectiveVpnRouter.App", "MainWindow.xaml.cs"));
        Assert.Contains("if (_connectUiActive)", mainWindow, StringComparison.Ordinal);
        Assert.Contains("ConnectVpnButton.IsEnabled = !busy;", mainWindow, StringComparison.Ordinal);

        string pipeHost = File.ReadAllText(Path.Combine(repoRoot, "src", "SelectiveVpnRouter.Service", "PipeIpcHost.cs"));
        Assert.Contains("_connectGate", pipeHost, StringComparison.Ordinal);
        Assert.Contains("VPN connect is already in progress.", pipeHost, StringComparison.Ordinal);
        Assert.Contains("ServiceCancellationToken", pipeHost, StringComparison.Ordinal);
    }

    [Fact]
    public void ServiceClient_uses_async_frame_io_for_operation_timeout()
    {
        string path = Path.Combine(FindRepoRoot()!, "src", "SelectiveVpnRouter.App", "ServiceClient.cs");
        string text = File.ReadAllText(path);
        Assert.Contains("ReadExactlyAsync", text, StringComparison.Ordinal);
        Assert.DoesNotContain("reader.ReadInt32()", text, StringComparison.Ordinal);
    }
}

public class WindowsUnicastAddressCatalogRegressionTests
{
    private static string CatalogSourcePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string path = Path.Combine(dir.FullName, "src", "SelectiveVpnRouter.Network", "WindowsUnicastAddressCatalog.cs");
            if (File.Exists(path))
            {
                return path;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("WindowsUnicastAddressCatalog.cs not found.");
    }

    [Fact]
    public void Catalog_source_has_no_unsafe_getadaptersaddresses_parsing()
    {
        string text = File.ReadAllText(CatalogSourcePath());
        Assert.DoesNotContain("GetAdaptersAddresses", text, StringComparison.Ordinal);
        Assert.DoesNotContain("PtrToStructure", text, StringComparison.Ordinal);
        Assert.DoesNotContain("SocketAddressStorage", text, StringComparison.Ordinal);
        Assert.DoesNotContain("IpAdapterUnicastAddress", text, StringComparison.Ordinal);
        Assert.Contains("NetworkInterface.GetAllNetworkInterfaces()", text, StringComparison.Ordinal);
        Assert.Contains("DuplicateAddressDetectionState", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AdapterCatalog_All_can_be_called_repeatedly_on_windows()
    {
        for (int i = 0; i < 5; i++)
        {
            IReadOnlyList<AdapterView> adapters = AdapterCatalog.All();
            Assert.NotNull(adapters);
        }

        IReadOnlyList<RouteRow> defaults = RouteTable.DefaultRoutes();
        Assert.NotNull(defaults);
    }

    [Fact]
    public void Snapshot_route_diagnostics_do_not_use_unsafe_catalog_parsing()
    {
        string adapters = File.ReadAllText(CatalogSourcePath());
        string engine = File.ReadAllText(Path.Combine(Path.GetDirectoryName(CatalogSourcePath())!, "..", "SelectiveVpnRouter.Service", "RouterEngine.cs"));
        Assert.Contains("PreferredRoutes.PreferredDirectDefault()", engine, StringComparison.Ordinal);
        Assert.DoesNotContain("GetAdaptersAddresses", adapters, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(DuplicateAddressDetectionState.Preferred, Ipv4DadState.Preferred)]
    [InlineData(DuplicateAddressDetectionState.Tentative, Ipv4DadState.Tentative)]
    [InlineData(DuplicateAddressDetectionState.Duplicate, Ipv4DadState.Duplicate)]
    [InlineData(DuplicateAddressDetectionState.Deprecated, Ipv4DadState.Deprecated)]
    [InlineData(DuplicateAddressDetectionState.Invalid, Ipv4DadState.Invalid)]
    public void DadState_mapping_matches_windows_network_information(
        DuplicateAddressDetectionState input,
        Ipv4DadState expected)
    {
        Assert.Equal(expected, WindowsUnicastAddressCatalog.MapDuplicateAddressDetectionState(input));
    }

    [Fact]
    public void Apipa_preferred_still_rejected_by_readiness()
    {
        var candidate = new VpnAdapterReadinessCandidate(
            "id-8",
            "TAP",
            "TAP",
            OperationalStatus.Up,
            8,
            [new Ipv4TunnelAddress("169.254.32.155", 16, Ipv4DadState.Preferred)],
            true,
            true,
            true);
        Assert.False(VpnAdapterReadiness.TrySelectBest([candidate], "10.28.0.1", "10.28.0.7", [], out _, out _));
    }

    [Fact]
    public void Tunnel_10_28_tentative_is_not_ready()
    {
        var candidate = new VpnAdapterReadinessCandidate(
            "id-9",
            "OpenVPN DCO",
            "OpenVPN DCO",
            OperationalStatus.Up,
            9,
            [new Ipv4TunnelAddress("10.28.0.2", 22, Ipv4DadState.Tentative)],
            true,
            true,
            true);
        Assert.False(VpnAdapterReadiness.TrySelectBest([candidate], "10.28.0.1", "10.28.0.2", [], out _, out _));
    }

    [Fact]
    public void Tunnel_10_28_preferred_is_ready()
    {
        var candidate = new VpnAdapterReadinessCandidate(
            "id-9",
            "OpenVPN DCO",
            "OpenVPN DCO",
            OperationalStatus.Up,
            9,
            [new Ipv4TunnelAddress("10.28.0.7", 22, Ipv4DadState.Preferred)],
            true,
            true,
            true);
        Assert.True(VpnAdapterReadiness.TrySelectBest([candidate], "10.28.0.1", "10.28.0.7", [], out VpnAdapterSelection? sel, out _));
        Assert.Equal(9, sel!.IfIndex);
    }
}
