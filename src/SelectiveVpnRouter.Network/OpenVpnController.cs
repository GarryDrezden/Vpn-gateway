using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using SelectiveVpnRouter.Core;

namespace SelectiveVpnRouter.Network;

public sealed class OpenVpnStatus
{
    public bool Running { get; init; }
    public bool Connected { get; init; }
    public int? Pid { get; init; }
    public string? Version { get; init; }
    public DateTimeOffset? ConnectedSince { get; init; }
    public IReadOnlyList<string> RecentLog { get; init; } = [];
}

public sealed class OpenVpnController : IAsyncDisposable
{
    private readonly ConcurrentQueue<string> _log = new();
    private Process? _process;
    private ProcessJob? _job;
    private string? _mgmtPasswordFile;
    private int _mgmtPort;
    private DateTimeOffset? _since;

    public int? Pid => _process is { HasExited: false } ? _process.Id : null;
    public bool Connected { get; private set; }
    public string? RouteGateway { get; private set; }
    public string? TunnelLocalIpv4 { get; private set; }
    public string? IfconfigPeerOrMask { get; private set; }
    public string? DiagnosticCommandLine { get; private set; }
    public IReadOnlyList<string> LogSnapshot => _log.ToArray();

    public static OpenVpnVersionResult ReadVersion(string exe)
        => ReadVersionAsync(exe).GetAwaiter().GetResult();

    public static async Task<OpenVpnVersionResult> ReadVersionAsync(string exe)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("--version");
        using var p = new Process { StartInfo = psi };
        if (!p.Start())
        {
            return new OpenVpnVersionResult(false, -1, null, "start failed");
        }

        Task<string> so = p.StandardOutput.ReadToEndAsync();
        Task<string> se = p.StandardError.ReadToEndAsync();
        await Task.WhenAll(so, se, p.WaitForExitAsync()).ConfigureAwait(false);
        string combined = so.Result + se.Result;
        return new OpenVpnVersionResult(p.ExitCode == 0, p.ExitCode, OpenVpnStateParser.TryParseVersionLine(combined), combined.Length == 0 ? "empty" : null);
    }

    public async Task StartAsync(string exe, string profile, bool disableDco, CancellationToken ct)
    {
        if (_process is { HasExited: false })
        {
            throw new InvalidOperationException("OpenVPN already running.");
        }

        _mgmtPort = GetFreePort();
        _mgmtPasswordFile = WriteMgmtPassword();
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
        psi.ArgumentList.Add("--route-nopull");
        psi.ArgumentList.Add("--verb");
        psi.ArgumentList.Add("3");
        psi.ArgumentList.Add("--management");
        psi.ArgumentList.Add("127.0.0.1");
        psi.ArgumentList.Add(_mgmtPort.ToString());
        psi.ArgumentList.Add(_mgmtPasswordFile);
        if (disableDco)
        {
            psi.ArgumentList.Add("--disable-dco");
        }

        DiagnosticCommandLine = BuildDiagnosticCommandLine(psi);

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _process.OutputDataReceived += (_, e) => Handle(e.Data);
        _process.ErrorDataReceived += (_, e) => Handle(e.Data);
        if (!_process.Start())
        {
            throw new InvalidOperationException("Failed to start openvpn.exe.");
        }

        _job = new ProcessJob();
        try
        {
            _job.Assign(_process.Id);
        }
        catch (Exception)
        {
            // Nested jobs can fail; kill-on-close is best-effort.
        }

        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        DateTime deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(VpnConnectBudget.OpenVpnStartupMs);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (Connected)
            {
                _since = DateTimeOffset.UtcNow;
                return;
            }

            if (_process.HasExited)
            {
                throw new InvalidOperationException("openvpn.exe exited " + _process.ExitCode);
            }

            await Task.Delay(200, ct).ConfigureAwait(false);
        }

        throw new TimeoutException("Timed out waiting for OpenVPN Initialization Sequence Completed.");
    }

    public async Task StopAsync()
    {
        Process? p = _process;
        if (p is null)
        {
            return;
        }

        try
        {
            if (!p.HasExited)
            {
                p.Kill(entireProcessTree: true);
                await p.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8)).ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
        }

        p.Dispose();
        _process = null;
        _job?.Dispose();
        _job = null;
        Connected = false;
        _since = null;
        RouteGateway = null;
        TunnelLocalIpv4 = null;
        IfconfigPeerOrMask = null;
        DiagnosticCommandLine = null;
        TryDelete(_mgmtPasswordFile);
        _mgmtPasswordFile = null;
    }

    public OpenVpnStatus Status()
        => new()
        {
            Running = _process is { HasExited: false },
            Connected = Connected,
            Pid = Pid,
            ConnectedSince = _since,
            RecentLog = _log.TakeLast(40).Select(LogRedactor.Redact).ToArray(),
        };

    public OpenVpnLiveStatus Live()
        => new()
        {
            Running = _process is { HasExited: false },
            Connected = Connected,
            Pid = Pid,
            ConnectedSince = _since,
            Gateway = RouteGateway,
            RecentLog = _log.TakeLast(40).Select(LogRedactor.Redact).ToArray(),
        };

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

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

        string? gw = OpenVpnStateParser.TryParseRouteGateway(line);
        if (gw is not null)
        {
            RouteGateway = gw;
        }

        (string? local, string? peerOrMask)? ifc = OpenVpnStateParser.TryParseIfconfig(line);
        if (ifc is { local: not null, peerOrMask: not null })
        {
            TunnelLocalIpv4 = ifc.Value.local;
            IfconfigPeerOrMask = ifc.Value.peerOrMask;
            if (RouteGateway is null && !ifc.Value.peerOrMask.StartsWith("255.", StringComparison.Ordinal))
            {
                RouteGateway = ifc.Value.peerOrMask;
            }
        }

        if (OpenVpnStateParser.IsConnected(line))
        {
            Connected = true;
        }
    }

    private static string WriteMgmtPassword()
    {
        Directory.CreateDirectory(AppPaths.RuntimeDirectory);
        string path = Path.Combine(AppPaths.RuntimeDirectory, "mgmt.pwd");
        string pw = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        File.WriteAllText(path, pw + Environment.NewLine);
        try
        {
            var info = new FileInfo(path);
            FileSecurity sec = info.GetAccessControl();
            sec.SetAccessRuleProtection(true, false);
            var sys = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            var adm = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            sec.AddAccessRule(new FileSystemAccessRule(sys, FileSystemRights.FullControl, AccessControlType.Allow));
            sec.AddAccessRule(new FileSystemAccessRule(adm, FileSystemRights.FullControl, AccessControlType.Allow));
            info.SetAccessControl(sec);
        }
        catch (Exception)
        {
        }

        return path;
    }

    private static int GetFreePort()
    {
        using var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    internal static string BuildDiagnosticCommandLine(ProcessStartInfo psi)
    {
        var parts = new List<string> { Quote(psi.FileName) };
        foreach (string arg in psi.ArgumentList)
        {
            parts.Add(Quote(arg));
        }

        return string.Join(' ', parts);
    }

    public static bool IsOpenVpnNegotiationLogLine(string line)
    {
        return line.Contains("PUSH_REPLY", StringComparison.OrdinalIgnoreCase)
            || line.Contains("ifconfig ", StringComparison.OrdinalIgnoreCase)
            || line.Contains("route-gateway", StringComparison.OrdinalIgnoreCase)
            || line.Contains("topology ", StringComparison.OrdinalIgnoreCase)
            || line.Contains("TUN/TAP", StringComparison.OrdinalIgnoreCase)
            || line.Contains("Data Channel Offload", StringComparison.OrdinalIgnoreCase)
            || line.Contains("TAP-Windows", StringComparison.OrdinalIgnoreCase)
            || line.Contains("Initialization Sequence Completed", StringComparison.OrdinalIgnoreCase)
            || line.Contains("netsh", StringComparison.OrdinalIgnoreCase)
            || line.Contains("IPv4", StringComparison.OrdinalIgnoreCase)
            || line.Contains("dhcp-option", StringComparison.OrdinalIgnoreCase);
    }

    private static string Quote(string value)
        => value.Contains(' ') || value.Contains('"') ? "\"" + value.Replace("\"", "\\\"") + "\"" : value;

    private static void TryDelete(string? path)
    {
        try
        {
            if (path is not null && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception)
        {
        }
    }
}

public sealed record OpenVpnVersionResult(bool Ok, int ExitCode, string? VersionLine, string? Error);
