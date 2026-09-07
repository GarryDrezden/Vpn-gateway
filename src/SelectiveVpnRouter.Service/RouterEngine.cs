using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using SelectiveVpnRouter.Core;
using SelectiveVpnRouter.Network;
using SelectiveVpnRouter.Proxy;

namespace SelectiveVpnRouter.Service;

public sealed class RouterEngine : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly List<OwnedRoute> _owned = [];
    private readonly DnsCache _dns = new();
    private readonly ConcurrentQueue<DiagnosticResult> _diagnostics = new();
    private AppConfiguration _config = new();
    private OpenVpnController? _vpn;
    private TransparentTcpProxy? _proxy;
    private WfpSession? _wfp;
    private CalloutDriverClient? _driver;
    private CancellationTokenSource? _loopCts;
    private bool _paused;
    private AdapterView? _vpnAdapter;
    private AdapterView? _directAdapter;
    private IReadOnlyList<AdapterView> _beforeConnect = [];

    public AppConfiguration Config
    {
        get { lock (_gate) return _config; }
        private set { lock (_gate) _config = value; }
    }

    public void Load()
    {
        Directory.CreateDirectory(AppPaths.ProgramData);
        Directory.CreateDirectory(AppPaths.RuntimeDirectory);
        Directory.CreateDirectory(AppPaths.LogDirectory);
        Config = ConfigSerializer.LoadOrDefault(AppPaths.ConfigFile);
        CrashCleanup.ReconcileStale(Log);
    }

    public void SaveConfig(AppConfiguration config)
    {
        Config = config;
        ConfigSerializer.Save(AppPaths.ConfigFile, config);
        _ = RefreshPolicyAsync();
    }

    public ServiceSnapshot Snapshot()
    {
        OpenVpnController? vpn = _vpn;
        TransparentTcpProxy? proxy = _proxy;
        return new ServiceSnapshot
        {
            RoutingPaused = _paused,
            DriverLoaded = _driver?.IsLoaded == true,
            TransparentRedirectActive = _driver?.IsLoaded == true && !_paused && vpn?.Connected == true,
            Vpn = vpn?.Live() ?? new OpenVpnLiveStatus(),
            VpnAdapter = ToLive(_vpnAdapter),
            DirectAdapter = ToLive(_directAdapter),
            Flows = proxy?.Flows ?? [],
            LastDiagnostics = _diagnostics.TakeLast(40).ToArray(),
            OwnedRoutes = _owned.ToArray(),
            Ipv6PolicyNote = DescribeIpv6(Config.Vpn.Ipv6Policy),
            UdpNote = Config.Vpn.BlockQuicForVpnApps
                ? "Optional QUIC/UDP 443 block for VPN-routed apps is enabled (forces TCP fallback). Full UDP routing is not implemented."
                : "UDP/QUIC per-process routing is unsupported in this MVP (TCP only). Optional QUIC block is off.",
        };
    }

    public async Task ConnectAsync(ConnectVpnRequest? request, CancellationToken ct)
    {
        AppConfiguration cfg = Config;
        string exe = request?.OpenVpnPath ?? cfg.Vpn.OpenVpnPath;
        string profile = request?.ProfilePath ?? cfg.Vpn.ProfilePath;
        bool dco = request?.DisableDco ?? cfg.Vpn.CompatibilityDisableDco;
        if (!File.Exists(exe))
        {
            throw new InvalidOperationException("openvpn.exe not found: " + exe);
        }

        if (!File.Exists(profile))
        {
            throw new InvalidOperationException("Profile not found: " + profile);
        }

        ProfileSafety.Result safety = ProfileSafety.Scan(File.ReadAllLines(profile));
        Log("Profile scan: dangerous local directives=" + safety.Findings.Count + " (they are ignored via --route-nopull).");

        await DisconnectAsync().ConfigureAwait(false);
        _beforeConnect = AdapterCatalog.All();
        _directAdapter = PickDirect(_beforeConnect);
        _vpn = new OpenVpnController();
        await _vpn.StartAsync(exe, profile, dco, ct).ConfigureAwait(false);

        AdapterView? vpnNic = null;
        for (int i = 0; i < 25 && vpnNic is null; i++)
        {
            vpnNic = AdapterCatalog.GuessVpn(_beforeConnect, AdapterCatalog.All());
            if (vpnNic is null)
            {
                await Task.Delay(200, ct).ConfigureAwait(false);
            }
        }

        _vpnAdapter = vpnNic ?? throw new InvalidOperationException("Could not detect OpenVPN tunnel adapter.");
        string? gw = _vpn.RouteGateway ?? GatewayGuess.FromAdapter(_vpnAdapter);
        if (gw is null || _vpnAdapter.Ipv4Index is not int ifIndex)
        {
            throw new InvalidOperationException("VPN adapter has no IPv4/gateway.");
        }

        var desired = new List<OwnedRoute> { RouteReconciler.TransportDefault(ifIndex, gw) };
        RouteOwnership.Apply(RouteReconciler.Plan(desired, _owned), _owned, Log);
        PersistCrash();

        _proxy = new TransparentTcpProxy
        {
            VpnInterfaceIndex = ifIndex,
            VpnInterfaceName = _vpnAdapter.Name,
            BindOutboundToVpn = true,
        };
        await _proxy.StartAsync(IPAddress.Loopback, 0, ct).ConfigureAwait(false);
        Log("Proxy listening on 127.0.0.1:" + _proxy.Port);

        _driver = CalloutDriverClient.TryOpen();
        if (_driver.IsLoaded)
        {
            _driver.TrySetRedirectTarget(Environment.ProcessId, (ushort)_proxy.Port, out string err);
            if (err.Length > 0)
            {
                Log(err);
            }
        }
        else
        {
            Log("Callout driver not loaded. Transparent per-process TCP requires the KMDF driver. SOCKS/Probe --via-proxy still tests VPN-bound sockets.");
        }

        _wfp = new WfpSession();
        try
        {
            _wfp.Open(_driver.IsLoaded);
        }
        catch (Exception ex)
        {
            Log("WFP engine open failed: " + ex.Message);
        }

        await RefreshPolicyAsync().ConfigureAwait(false);
        _loopCts = new CancellationTokenSource();
        _ = Task.Run(() => ReconcileLoop(_loopCts.Token), _loopCts.Token);
    }

    public async Task DisconnectAsync()
    {
        _loopCts?.Cancel();
        _loopCts = null;
        try { _driver?.TryDisable(out _); } catch (Exception) { }
        _driver?.Dispose();
        _driver = null;
        _wfp?.Dispose();
        _wfp = null;
        if (_proxy is not null)
        {
            await _proxy.DisposeAsync().ConfigureAwait(false);
            _proxy = null;
        }

        RouteOwnership.RemoveAll(_owned, Log);
        if (_vpn is not null)
        {
            await _vpn.DisposeAsync().ConfigureAwait(false);
            _vpn = null;
        }

        _vpnAdapter = null;
        ConfigSerializer.ClearCrashState(AppPaths.CrashStateFile);
        _paused = false;
    }

    public async Task EmergencyRestoreAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        CrashCleanup.ReconcileStale(Log);
        try
        {
            using var wfp = new WfpSession();
            wfp.Open(driverLoaded: false);
            wfp.ClearFilters();
        }
        catch (Exception ex)
        {
            Log("Emergency WFP: " + ex.Message);
        }

        Log("Emergency network restore completed (owned routes/filters/OpenVPN only).");
    }

    public void PauseRouting(bool pause)
    {
        _paused = pause;
        _ = RefreshPolicyAsync();
    }

    public async Task<DiagnosticResult> RunDiagnosticAsync(string name, CancellationToken ct)
    {
        DiagnosticResult result = await DiagnosticCenter.RunAsync(this, name, ct).ConfigureAwait(false);
        _diagnostics.Enqueue(result);
        while (_diagnostics.Count > 80 && _diagnostics.TryDequeue(out _))
        {
        }

        return result;
    }

    public string ExportDiagnosticsZip()
    {
        Directory.CreateDirectory(AppPaths.LogDirectory);
        string zip = Path.Combine(AppPaths.LogDirectory, "diagnostics-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip");
        string tmp = Path.Combine(Path.GetTempPath(), "svr-diag-" + Guid.NewGuid());
        Directory.CreateDirectory(tmp);
        try
        {
            File.WriteAllText(Path.Combine(tmp, "version.txt"),
                "SelectiveVpnRouter " + (Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.2.0")
                + Environment.NewLine + RuntimeInformation.OSDescription);
            File.WriteAllText(Path.Combine(tmp, "status.json"),
                System.Text.Json.JsonSerializer.Serialize(Snapshot(), ConfigSerializer.JsonOptions));
            File.WriteAllText(Path.Combine(tmp, "config.redacted.json"),
                System.Text.Json.JsonSerializer.Serialize(Config with { }, ConfigSerializer.JsonOptions));
            File.WriteAllText(Path.Combine(tmp, "adapters.json"),
                System.Text.Json.JsonSerializer.Serialize(AdapterCatalog.All(), ConfigSerializer.JsonOptions));
            File.WriteAllText(Path.Combine(tmp, "routes-ipv4.txt"),
                string.Join(Environment.NewLine, RouteTable.IPv4().Select(r => $"{r.Destination}/{r.Mask} via {r.NextHop} if {r.InterfaceIndex} metric {r.Metric}")));
            string logDir = AppPaths.LogDirectory;
            ZipFile.CreateFromDirectory(tmp, zip);
            if (Directory.Exists(logDir))
            {
                using ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Update);
                foreach (string file in Directory.GetFiles(logDir, "*.log"))
                {
                    archive.CreateEntryFromFile(file, "logs/" + Path.GetFileName(file));
                }
            }

            return zip;
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch (Exception) { }
        }
    }

    public int? ProxyPort => _proxy?.Port;
    public AdapterView? VpnAdapter => _vpnAdapter;
    public OpenVpnController? Vpn => _vpn;

    public async ValueTask DisposeAsync() => await DisconnectAsync().ConfigureAwait(false);

    internal void Log(string message)
    {
        Directory.CreateDirectory(AppPaths.LogDirectory);
        string line = DateTimeOffset.Now.ToString("o") + " " + LogRedactor.Redact(message);
        File.AppendAllText(Path.Combine(AppPaths.LogDirectory, "service.log"), line + Environment.NewLine);
    }

    private async Task RefreshPolicyAsync()
    {
        AppConfiguration cfg = Config;
        IReadOnlyList<string> vpnExes = _paused
            ? []
            : RuleEvaluator.VpnApplicationRules(cfg.Rules)
                .Select(r => r.Target)
                .Where(File.Exists)
                .ToList();
        try
        {
            _wfp?.ReplaceVpnAppFilters(vpnExes, cfg.Vpn.Ipv6Policy);
        }
        catch (Exception ex)
        {
            Log("WFP filter update: " + ex.Message);
        }

        if (_vpnAdapter?.Ipv4Index is int ifIndex)
        {
            string gw = _vpn?.RouteGateway ?? GatewayGuess.FromAdapter(_vpnAdapter) ?? "0.0.0.0";
            IReadOnlyList<OwnedRoute> dest = DestinationRoutePlanner.PlanVpnDestinations(cfg.Rules, ifIndex, gw, _dns.Snapshot());
            var desired = new List<OwnedRoute> { RouteReconciler.TransportDefault(ifIndex, gw) };
            desired.AddRange(dest);
            RouteOwnership.Apply(RouteReconciler.Plan(desired, _owned), _owned, Log);
            PersistCrash();
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private async Task ReconcileLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RefreshDnsAsync(ct).ConfigureAwait(false);
                await RefreshPolicyAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log("Reconcile: " + ex.Message);
            }

            await Task.Delay(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
        }
    }

    private async Task RefreshDnsAsync(CancellationToken ct)
    {
        foreach (RoutingRule rule in RuleEvaluator.VpnDestinationRules(Config.Rules).Where(r => r.Type == RuleType.Domain))
        {
            string host = DestinationRoutePlanner.StripWildcard(rule.Target);
            try
            {
                IPAddress[] addrs = await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);
                _dns.Set(host, addrs);
            }
            catch (Exception)
            {
            }
        }
    }

    private void PersistCrash()
    {
        ConfigSerializer.SaveCrashState(AppPaths.CrashStateFile, new CrashState
        {
            OpenVpnPid = _vpn?.Pid,
            ProxyPort = _proxy?.Port,
            OwnedRoutes = _owned.ToArray(),
        });
    }

    private static AdapterLiveStatus? ToLive(AdapterView? nic)
        => nic is null ? null : new AdapterLiveStatus
        {
            Name = nic.Name,
            Description = nic.Description,
            Ipv4Index = nic.Ipv4Index,
            Ipv4 = nic.Ipv4,
            Ipv6 = nic.Ipv6,
        };

    private static AdapterView? PickDirect(IReadOnlyList<AdapterView> nics)
        => nics.FirstOrDefault(n => n.Status == System.Net.NetworkInformation.OperationalStatus.Up
            && n.Ipv4Index is not null
            && !AdapterCatalog.LooksVpn(n.Name)
            && !AdapterCatalog.LooksVpn(n.Description)
            && n.Ipv4.Count > 0);

    private static string DescribeIpv6(Ipv6Policy p) => p switch
    {
        Ipv6Policy.AllowDirect => "IPv6: allow direct (may leak around a v4-only VPN).",
        Ipv6Policy.VpnIfAvailable => "IPv6: use VPN when the tunnel has IPv6.",
        Ipv6Policy.Auto => "IPv6: auto — block direct IPv6 for VPN-routed apps when the tunnel has no IPv6.",
        _ => "IPv6: block direct IPv6 for VPN-routed apps (default, prevents leaks).",
    };
}

public static class DiagnosticCenter
{
    public static async Task<DiagnosticResult> RunAsync(RouterEngine engine, string name, CancellationToken ct)
    {
        try
        {
            return name switch
            {
                "admin" => PrivilegeProbe.IsAdministrator()
                    ? Pass(name, "Process is elevated.")
                    : Fail(name, "Not running as administrator. The service must be elevated."),
                "direct-if" => Adapters("direct-if", false),
                "vpn-if" => Adapters("vpn-if", true),
                "openvpn-exe" => CheckExe(engine),
                "ovpn-profile" => CheckProfile(engine),
                "connect-vpn" => await Connect(engine, ct).ConfigureAwait(false),
                "direct-route" => await Http(name, engine.Config.Vpn.PublicIpEndpoint, bindVpn: false, engine, ct).ConfigureAwait(false),
                "vpn-bound-socket" => await Http(name, engine.Config.Vpn.PublicIpEndpoint, bindVpn: true, engine, ct).ConfigureAwait(false),
                "app-routing" => AppRouting(engine),
                "dns" => await DnsTest(name, ct).ConfigureAwait(false),
                "ipv4" => Ipv4(engine),
                "ipv6" => Ipv6(engine),
                "cleanup" => await Cleanup(engine).ConfigureAwait(false),
                "export" => Pass(name, "Wrote " + engine.ExportDiagnosticsZip()),
                _ => Fail(name, "Unknown diagnostic '" + name + "'."),
            };
        }
        catch (Exception ex)
        {
            return Fail(name, ex.Message);
        }
    }

    private static DiagnosticResult Adapters(string name, bool vpn)
    {
        var nics = AdapterCatalog.All().Where(n => n.Status == System.Net.NetworkInformation.OperationalStatus.Up).ToList();
        IEnumerable<AdapterView> pick = vpn
            ? nics.Where(n => AdapterCatalog.LooksVpn(n.Name) || AdapterCatalog.LooksVpn(n.Description))
            : nics.Where(n => !AdapterCatalog.LooksVpn(n.Name) && !AdapterCatalog.LooksVpn(n.Description) && n.Ipv4.Count > 0);
        AdapterView[] list = pick.ToArray();
        if (list.Length == 0)
        {
            return vpn ? Warn(name, "No OpenVPN/Wintun adapter is up. Connect the personal VPN first.")
                : Fail(name, "No non-VPN IPv4 adapter is up.");
        }

        return Pass(name, string.Join("; ", list.Select(n => n.Name + " if4=" + n.Ipv4Index + " " + string.Join(",", n.Ipv4))));
    }

    private static DiagnosticResult CheckExe(RouterEngine engine)
    {
        string path = engine.Config.Vpn.OpenVpnPath;
        if (!File.Exists(path))
        {
            return Fail("openvpn-exe", "Not found: " + path);
        }

        OpenVpnVersionResult v = OpenVpnController.ReadVersion(path);
        return v.Ok && v.VersionLine is not null
            ? Pass("openvpn-exe", v.VersionLine)
            : Fail("openvpn-exe", v.Error ?? ("exit " + v.ExitCode));
    }

    private static DiagnosticResult CheckProfile(RouterEngine engine)
    {
        string path = engine.Config.Vpn.ProfilePath;
        if (!File.Exists(path))
        {
            return Fail("ovpn-profile", "Profile not set or missing.");
        }

        ProfileSafety.Result r = ProfileSafety.Scan(File.ReadAllLines(path));
        string extra = r.Findings.Count == 0
            ? "No local redirect/DNS directives."
            : "Local directives " + string.Join(", ", r.Findings.Select(f => f.Directive)) + " will be overridden by --route-nopull.";
        if (r.AuthInteraction)
        {
            extra += " Profile may prompt for credentials; use an auth-user-pass file beside the profile.";
        }

        return r.Error is not null ? Fail("ovpn-profile", r.Error) : Pass("ovpn-profile", extra);
    }

    private static async Task<DiagnosticResult> Connect(RouterEngine engine, CancellationToken ct)
    {
        if (engine.Snapshot().Vpn.Connected)
        {
            return Pass("connect-vpn", "Already connected.");
        }

        await engine.ConnectAsync(null, ct).ConfigureAwait(false);
        ServiceSnapshot s = engine.Snapshot();
        bool stolen = RouteTable.HasInternetWideVia(s.VpnAdapter?.Ipv4Index ?? -1);
        string msg = "Connected. Adapter " + s.VpnAdapter?.Name + " if=" + s.VpnAdapter?.Ipv4Index
            + ". Owned high-metric VPN default is for the proxy only.";
        if (stolen)
        {
            return Warn("connect-vpn", msg + " WARNING: an Internet-wide route with metric < 5000 exists on the VPN NIC; Direct traffic may be captured.");
        }

        return Pass("connect-vpn", msg);
    }

    private static async Task<DiagnosticResult> Http(string name, string? endpoint, bool bindVpn, RouterEngine engine, CancellationToken ct)
    {
        string url = string.IsNullOrWhiteSpace(endpoint) ? "https://api.ipify.org" : endpoint;
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || uri.Host.Length == 0)
        {
            return Fail(name, "Invalid public IP endpoint.");
        }

        IPAddress[] addrs = await Dns.GetHostAddressesAsync(uri.Host, AddressFamily.InterNetwork, ct).ConfigureAwait(false);
        if (addrs.Length == 0)
        {
            return Fail(name, "DNS failed for " + uri.Host);
        }

        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        if (bindVpn)
        {
            int? idx = engine.VpnAdapter?.Ipv4Index;
            if (idx is null)
            {
                return Fail(name, "VPN adapter not connected.");
            }

            SocketInterfaceBinder.BindIpv4UnicastIf(socket, idx.Value);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        await socket.ConnectAsync(addrs[0], uri.Port == 0 ? 443 : uri.Port, timeout.Token).ConfigureAwait(false);
        IPEndPoint? local = socket.LocalEndPoint as IPEndPoint;
        return Pass(name, (bindVpn ? "VPN-bound" : "Direct") + " TCP connect to " + addrs[0] + " local " + local);
    }

    private static DiagnosticResult AppRouting(RouterEngine engine)
    {
        if (engine.Snapshot().DriverLoaded && engine.Snapshot().TransparentRedirectActive)
        {
            return Pass("app-routing", "Callout driver is loaded and redirect is armed for VPN application rules. Use Probe.exe under an Application rule to verify isolation.");
        }

        if (engine.ProxyPort is int port)
        {
            return Warn("app-routing", "Driver not loaded. Transparent EXE isolation is inactive. Probe --via-proxy 127.0.0.1:" + port + " still exercises the VPN-bound proxy path. Direct processes remain Direct.");
        }

        return Fail("app-routing", "Proxy is not running. Connect VPN first.");
    }

    private static async Task<DiagnosticResult> DnsTest(string name, CancellationToken ct)
    {
        IPHostEntry e = await Dns.GetHostEntryAsync("example.com", ct).ConfigureAwait(false);
        return e.AddressList.Length > 0 ? Pass(name, string.Join(", ", e.AddressList.Select(a => a.ToString())))
            : Fail(name, "No addresses.");
    }

    private static DiagnosticResult Ipv4(RouterEngine engine)
    {
        bool any = AdapterCatalog.All().Any(a => a.Ipv4.Count > 0);
        return any ? Pass("ipv4", "IPv4 unicast addresses present.") : Fail("ipv4", "No IPv4 addresses.");
    }

    private static DiagnosticResult Ipv6(RouterEngine engine)
    {
        bool vpn6 = engine.VpnAdapter?.Ipv6.Any(a => !a.StartsWith("fe80", StringComparison.OrdinalIgnoreCase)) == true;
        string policy = engine.Snapshot().Ipv6PolicyNote;
        return vpn6
            ? Pass("ipv6", "Tunnel has global IPv6. " + policy)
            : Warn("ipv6", "Tunnel has no global IPv6. " + policy);
    }

    private static async Task<DiagnosticResult> Cleanup(RouterEngine engine)
    {
        await engine.EmergencyRestoreAsync().ConfigureAwait(false);
        return Pass("cleanup", "Owned routes/filters/OpenVPN cleared.");
    }

    private static DiagnosticResult Pass(string n, string m) => new() { Name = n, Outcome = DiagnosticOutcomes.Pass, Message = m };
    private static DiagnosticResult Fail(string n, string m) => new() { Name = n, Outcome = DiagnosticOutcomes.Fail, Message = m };
    private static DiagnosticResult Warn(string n, string m) => new() { Name = n, Outcome = DiagnosticOutcomes.Warning, Message = m };
}
