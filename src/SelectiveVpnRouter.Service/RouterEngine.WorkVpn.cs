using SelectiveVpnRouter.Core;
using SelectiveVpnRouter.Network;

namespace SelectiveVpnRouter.Service;

public sealed partial class RouterEngine
{
    private readonly object _workGate = new();
    private WorkOpenVpnController? _workVpn;
    private AdapterView? _workVpnAdapter;
    private string? _workExcludeAdapterId;
    private IReadOnlyList<AdapterView> _workBeforeConnect = [];
    private string? _workLastError;
    private CancellationTokenSource? _workConnectCts;
    private Task? _workConnectTask;

    public void BeginConnectWorkVpn(ConnectWorkVpnRequest? request)
    {
        WorkVpnFeatureGate.ThrowIfDisabled();

        lock (_workGate)
        {
            if (_workConnectTask is { IsCompleted: false })
            {
                throw new InvalidOperationException("Work VPN connect is already in progress.");
            }

            AppConfiguration cfg = Config;
            WorkVpnSettings work = WorkVpnProfileDefaults.WithMigrationDefaults(cfg.WorkVpn);
            if (!work.Enabled)
            {
                throw new InvalidOperationException("Work VPN is disabled in settings.");
            }

            string profile = request?.ProfilePath ?? work.ProfilePath;
            if (string.IsNullOrWhiteSpace(profile) || !File.Exists(profile))
            {
                throw new InvalidOperationException("Work VPN profile is missing: " + (profile ?? "(empty)"));
            }

            if (_workVpn is { Connected: true } && _workVpn.Pid is not null)
            {
                Log("work-vpn-connect skipped already-connected pid=" + _workVpn.Pid);
                return;
            }

            string exe = request?.OpenVpnPath ?? cfg.Vpn.OpenVpnPath;
            if (!File.Exists(exe))
            {
                throw new InvalidOperationException("openvpn.exe not found: " + exe);
            }

            string? username = request?.Username ?? work.Username;
            string? password = request?.Password;
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                throw new InvalidOperationException("Work VPN credentials are required.");
            }

            _workConnectCts?.Cancel();
            _workConnectCts?.Dispose();
            _workConnectCts = CancellationTokenSource.CreateLinkedTokenSource(ServiceCancellationToken);
            _workLastError = null;
            _workBeforeConnect = AdapterCatalog.All();
            _workExcludeAdapterId = _vpnAdapter?.Id;
            int? excludeIf = _vpnAdapter?.Ipv4Index;
            bool dco = request?.DisableDco ?? cfg.Vpn.CompatibilityDisableDco;
            string user = username.Trim();
            string pass = password;
            bool remember = work.RememberUsername;
            string profilePath = profile;

            _workConnectTask = Task.Run(() => RunWorkConnectBackgroundAsync(
                exe,
                profilePath,
                user,
                pass,
                dco,
                excludeIf,
                remember,
                cfg,
                work,
                _workConnectCts.Token));
        }
    }

    
    private async Task CleanupWorkVpnProcessOnlyAsync()
    {
        if (_workVpn is not null)
        {
            int? pid = _workVpn.Pid;
            await _workVpn.DisposeAsync().ConfigureAwait(false);
            _workVpn = null;
            if (pid is not null)
            {
                Log("work-vpn-stopped pid=" + pid);
            }
        }

        _workVpnAdapter = null;
    }
    private async Task RunWorkConnectBackgroundAsync(
        string exe,
        string profile,
        string username,
        string password,
        bool disableDco,
        int? excludeIfIndex,
        bool rememberUsername,
        AppConfiguration cfg,
        WorkVpnSettings workSettings,
        CancellationToken ct)
    {
        try
        {
            await CleanupWorkVpnProcessOnlyAsync().ConfigureAwait(false);
            _workBeforeConnect = AdapterCatalog.All();
            _workVpn = new WorkOpenVpnController();
            _workVpn.AuthLogSink = line => Log("work-vpn-openvpn-log: " + line);
            Log("work-vpn-start profile=" + profile);
            await _workVpn.SpawnAsync(exe, profile, username, password, disableDco, ct).ConfigureAwait(false);
            Log("work-vpn-openvpn-started pid=" + (_workVpn.Pid?.ToString() ?? "?"));
            if (!string.IsNullOrWhiteSpace(_workVpn.DiagnosticCommandLine))
            {
                Log("work-vpn-cmdline " + _workVpn.DiagnosticCommandLine);
            }
            await _workVpn.WaitUntilConnectedOrFailedAsync(ct).ConfigureAwait(false);

            _workVpnAdapter = await WorkVpnAdapterResolver.WaitForAdapterAsync(
                _workBeforeConnect,
                _workVpn,
                _workExcludeAdapterId,
                excludeIfIndex,
                TimeSpan.FromMilliseconds(WorkVpnConnectBudget.AdapterReadinessMs),
                ct).ConfigureAwait(false);
            if (_workVpnAdapter is null)
            {
                throw new InvalidOperationException("Work VPN tunnel adapter was not detected.");
            }

            Log("work-vpn-adapter name=" + _workVpnAdapter.Name + " ifIndex=" + _workVpnAdapter.Ipv4Index
                + " addr=" + string.Join(",", _workVpnAdapter.Ipv4));

            if (rememberUsername && !string.Equals(workSettings.Username, username, StringComparison.Ordinal))
            {
                SaveConfig(cfg with { WorkVpn = workSettings with { Username = username, ProfilePath = profile } });
            }
            else if (string.IsNullOrWhiteSpace(workSettings.ProfilePath))
            {
                SaveConfig(cfg with { WorkVpn = workSettings with { ProfilePath = profile } });
            }

            Log("work-vpn-connect-complete");
        }
        catch (OperationCanceledException)
        {
            Log("work-vpn-connect-cancelled");
            _workLastError = "Cancelled.";
            await CleanupWorkVpnProcessOnlyAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogWorkVpnConnectFailure(ex);
            await CleanupWorkVpnProcessOnlyAsync().ConfigureAwait(false);
        }
        finally
        {
            lock (_workGate)
            {
                _workConnectTask = null;
                _workConnectCts?.Dispose();
                _workConnectCts = null;
            }
        }
    }

    private void LogWorkVpnConnectFailure(Exception ex)
    {
        string message = _workVpn?.LastError ?? ex.Message;
        Log("work-vpn-connect-failed error=" + message);
        if (_workVpn?.LastAuthClassification is WorkVpnAuthResponseKind kind and not WorkVpnAuthResponseKind.None)
        {
            Log("work-vpn-auth-classification=" + kind);
        }

        foreach (string line in _workVpn?.LogSnapshot.Where(OpenVpnStateParser.IsWorkVpnAuthRelatedLogLine).TakeLast(12)
                     ?? [])
        {
            Log("work-vpn-openvpn-log: " + LogRedactor.Redact(line));
        }

        _workLastError = message;
    }

    public async Task DisconnectWorkVpnAsync(bool clearLastError = true)
    {
        if (!FeatureFlags.WorkVpn)
        {
            return;
        }

        await DisconnectWorkVpnInternalAsync(clearLastError, cancelInFlight: true).ConfigureAwait(false);
    }

    private async Task DisconnectWorkVpnInternalAsync(bool clearLastError, bool cancelInFlight)
    {
        Task? task;
        lock (_workGate)
        {
            if (cancelInFlight)
            {
                _workConnectCts?.Cancel();
            }

            task = _workConnectTask;
        }

        if (task is not null)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (Exception)
            {
            }

            lock (_workGate)
            {
                _workConnectTask = null;
                _workConnectCts?.Dispose();
                _workConnectCts = null;
            }
        }

        if (_workVpn is not null)
        {
            int? pid = _workVpn.Pid;
            await _workVpn.DisposeAsync().ConfigureAwait(false);
            _workVpn = null;
            if (pid is not null)
            {
                Log("work-vpn-stopped pid=" + pid);
            }
        }

        _workVpnAdapter = null;
        _workExcludeAdapterId = null;
        _workBeforeConnect = [];
        if (clearLastError)
        {
            _workLastError = null;
        }
    }

    private WorkVpnLiveStatus BuildWorkVpnSnapshot()
    {
        if (!FeatureFlags.WorkVpn)
        {
            return WorkVpnLiveStatusFactory.Disabled();
        }

        AppConfiguration cfg = Config;
        WorkVpnSettings work = WorkVpnProfileDefaults.WithMigrationDefaults(cfg.WorkVpn);
        WorkOpenVpnController? controller = _workVpn;
        bool taskRunning;
        lock (_workGate)
        {
            taskRunning = _workConnectTask is { IsCompleted: false };
        }

        bool processRunning = controller?.Pid is not null;
        bool running = processRunning || taskRunning;
        bool connected = controller?.Connected == true;
        WorkVpnSessionState state = controller?.Phase ?? WorkVpnSessionState.Disconnected;
        if (taskRunning && state == WorkVpnSessionState.Disconnected)
        {
            state = WorkVpnSessionState.Connecting;
        }

        if (!running && state is not WorkVpnSessionState.Disconnected and not WorkVpnSessionState.Failed)
        {
            state = WorkVpnSessionState.Disconnected;
        }

        if (_workLastError is not null && state == WorkVpnSessionState.Disconnected && !connected)
        {
            state = WorkVpnSessionState.Failed;
        }

        string profile = work.ProfilePath;
        bool configured = work.Enabled && !string.IsNullOrWhiteSpace(profile) && File.Exists(profile);
        int? ifIndex = _workVpnAdapter?.Ipv4Index;
        string? address = _workVpnAdapter?.Ipv4.FirstOrDefault() ?? controller?.TunnelLocalIpv4;
        bool ready = WorkVpnReadiness.IsReady(
            connected ? WorkVpnSessionState.Connected : state,
            processRunning,
            ifIndex,
            address);
        return new WorkVpnLiveStatus
        {
            FeatureEnabled = work.Enabled,
            Configured = configured,
            WorkVpnReady = ready,
            State = state,
            Connected = connected,
            WaitingForMfa = controller?.WaitingForMfa == true || state == WorkVpnSessionState.WaitingForMfa,
            ProfilePath = string.IsNullOrWhiteSpace(profile) ? null : profile,
            AdapterName = _workVpnAdapter?.Name,
            InterfaceIndex = ifIndex,
            Address = address,
            ProcessId = controller?.Pid,
            LastError = _workLastError ?? controller?.LastError,
            RecentLog = controller?.LogSnapshot.TakeLast(20).Select(LogRedactor.Redact).ToArray() ?? [],
        };
    }
}