using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using SelectiveVpnRouter.Core;

namespace SelectiveVpnRouter.Network;

public sealed class WorkOpenVpnController : IAsyncDisposable
{
    private readonly ConcurrentQueue<string> _log = new();
    private Process? _process;
    private ProcessJob? _job;
    private WorkVpnAuthFileGuard? _authFile;
    private DateTimeOffset? _since;

    public WorkVpnSessionState Phase { get; private set; } = WorkVpnSessionState.Disconnected;
    public bool WaitingForMfa { get; private set; }
    public string? LastError { get; private set; }
    public int? Pid => _process is { HasExited: false } ? _process.Id : null;
    public bool Connected { get; private set; }
    public string? RouteGateway { get; private set; }
    public string? TunnelLocalIpv4 { get; private set; }
    public string? DiagnosticCommandLine { get; private set; }
    public IReadOnlyList<string> LogSnapshot => _log.ToArray();
    internal bool AuthFileExistsForTests => _authFile?.Exists == true;
    public WorkVpnAuthResponseKind LastAuthClassification { get; private set; } = WorkVpnAuthResponseKind.None;

    /// <summary>Sanitized auth-related OpenVPN lines for service diagnostics.</summary>
    public Action<string>? AuthLogSink { get; set; }

    public async Task SpawnAsync(
        string exe,
        string profile,
        string username,
        string password,
        bool disableDco,
        CancellationToken ct)
    {
        if (_process is { HasExited: false })
        {
            throw new InvalidOperationException("Work OpenVPN already running.");
        }

        ResetRuntimeState();
        Phase = WorkVpnSessionState.Connecting;
        _authFile = SecureCredentialFile.WriteAuthUserPass(username, password);
        string cwd = Path.GetDirectoryName(profile) ?? Environment.CurrentDirectory;

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = cwd,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("--config");
        psi.ArgumentList.Add(profile);
        psi.ArgumentList.Add("--auth-user-pass");
        psi.ArgumentList.Add(_authFile.Path);
        psi.ArgumentList.Add("--verb");
        psi.ArgumentList.Add("3");
        if (disableDco)
        {
            psi.ArgumentList.Add("--disable-dco");
        }

        DiagnosticCommandLine = OpenVpnController.BuildDiagnosticCommandLine(psi);

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _process.OutputDataReceived += (_, e) => Handle(e.Data);
        _process.ErrorDataReceived += (_, e) => Handle(e.Data);
        if (!_process.Start())
        {
            Phase = WorkVpnSessionState.Failed;
            LastError = "Failed to start openvpn.exe.";
            DeleteAuthFileOnce();
            throw new InvalidOperationException(LastError);
        }

        _job = new ProcessJob();
        try
        {
            _job.Assign(_process.Id);
        }
        catch (Exception)
        {
        }

        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
        await Task.Yield();
        ct.ThrowIfCancellationRequested();
    }

    public async Task WaitUntilConnectedOrFailedAsync(CancellationToken ct)
    {
        if (_process is null)
        {
            throw new InvalidOperationException("Work OpenVPN has not been spawned.");
        }

        DateTime deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(WorkVpnConnectBudget.TotalOperationMs);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (Connected)
            {
                Phase = WorkVpnSessionState.Connected;
                WaitingForMfa = false;
                DeleteAuthFileOnce();
                _since = DateTimeOffset.UtcNow;
                return;
            }

            if (Phase == WorkVpnSessionState.Failed)
            {
                DeleteAuthFileOnce();
                throw new InvalidOperationException(LastError ?? "Work VPN failed.");
            }

            if (_process.HasExited)
            {
                Phase = WorkVpnSessionState.Failed;
                LastError = "openvpn.exe exited " + _process.ExitCode;
                DeleteAuthFileOnce();
                throw new InvalidOperationException(LastError);
            }

            await Task.Delay(200, ct).ConfigureAwait(false);
        }

        Phase = WorkVpnSessionState.Failed;
        LastError = WaitingForMfa
            ? "Timed out waiting for MFA approval on Work VPN."
            : "Timed out waiting for Work VPN Initialization Sequence Completed.";
        DeleteAuthFileOnce();
        throw new TimeoutException(LastError);
    }

    internal void AttachAuthFileForTests(string path) => _authFile = new WorkVpnAuthFileGuard(path);

    internal void ApplyLogLineForTests(string? line) => Handle(line);

    public async Task StopAsync()
    {
        Phase = WorkVpnSessionState.Disconnecting;
        Process? p = _process;
        try
        {
            if (p is not null && !p.HasExited)
            {
                try
                {
                    p.StandardInput.Close();
                }
                catch (Exception)
                {
                }

                bool exited = await WaitForExitAsync(p, TimeSpan.FromSeconds(6)).ConfigureAwait(false);
                if (!exited && !p.HasExited)
                {
                    p.Kill(entireProcessTree: true);
                    await WaitForExitAsync(p, TimeSpan.FromSeconds(8)).ConfigureAwait(false);
                }
            }
        }
        catch (Exception)
        {
        }
        finally
        {
            DeleteAuthFileOnce();
            p?.Dispose();
            _process = null;
            _job?.Dispose();
            _job = null;
            ResetRuntimeState();
            Phase = WorkVpnSessionState.Disconnected;
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private void ResetRuntimeState()
    {
        Connected = false;
        WaitingForMfa = false;
        _since = null;
        RouteGateway = null;
        TunnelLocalIpv4 = null;
        DiagnosticCommandLine = null;
        LastAuthClassification = WorkVpnAuthResponseKind.None;
    }

    private void DeleteAuthFileOnce() => _authFile?.DeleteOnce();

    private void Handle(string? line)
    {
        if (line is null)
        {
            return;
        }

        _log.Enqueue(line);
        while (_log.Count > 400 && _log.TryDequeue(out _))
        {
        }

        if (OpenVpnStateParser.IsWorkVpnAuthRelatedLogLine(line))
        {
            AuthLogSink?.Invoke(LogRedactor.Redact(line));
        }

        WorkVpnAuthResponseKind authKind = WorkVpnAuthLineClassifier.Classify(line);
        if (authKind is not WorkVpnAuthResponseKind.None)
        {
            LastAuthClassification = authKind;
        }

        switch (authKind)
        {
            case WorkVpnAuthResponseKind.MfaPending:
                Phase = WorkVpnSessionState.WaitingForMfa;
                WaitingForMfa = true;
                DeleteAuthFileOnce();
                return;
            case WorkVpnAuthResponseKind.ChallengeRequired:
                Phase = WorkVpnSessionState.WaitingForMfa;
                WaitingForMfa = true;
                return;
            case WorkVpnAuthResponseKind.WrongCredentials:
            case WorkVpnAuthResponseKind.AuthRejected:
                Phase = WorkVpnSessionState.Failed;
                LastError = WorkVpnAuthLineClassifier.UserFacingMessage(authKind);
                WaitingForMfa = false;
                DeleteAuthFileOnce();
                return;
            case WorkVpnAuthResponseKind.UnknownAuthResponse:
                Phase = WorkVpnSessionState.Failed;
                LastError = WorkVpnAuthLineClassifier.UserFacingMessage(authKind);
                WaitingForMfa = false;
                DeleteAuthFileOnce();
                return;
        }

        if (OpenVpnStateParser.IsAuthPrompt(line))
        {
            Phase = WorkVpnSessionState.WaitingForCredentials;
            return;
        }

        string? gw = OpenVpnStateParser.TryParseRouteGateway(line);
        if (gw is not null)
        {
            RouteGateway = gw;
        }

        (string? local, string? peerOrMask)? ifc = OpenVpnStateParser.TryParseIfconfig(line);
        if (ifc is { local: not null, peerOrMask: not null })
        {
            TunnelLocalIpv4 = ifc.Value.local;
        }

        if (OpenVpnStateParser.IsConnected(line))
        {
            Connected = true;
            WaitingForMfa = false;
            DeleteAuthFileOnce();
            if (Phase is WorkVpnSessionState.WaitingForMfa or WorkVpnSessionState.Connecting)
            {
                Phase = WorkVpnSessionState.Connected;
            }
        }
    }

    private static async Task<bool> WaitForExitAsync(Process process, TimeSpan timeout)
    {
        try
        {
            await process.WaitForExitAsync().WaitAsync(timeout).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }
}