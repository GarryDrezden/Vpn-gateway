using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text;

namespace SelectiveVpn.V0.OpenVpn;

internal enum OpenVpnWaitStatus
{
    Connected,
    AuthInteractionRequired,
    ProcessExited,
    TimedOut,
    Canceled,
}

internal sealed class OpenVpnWaitResult
{
    public required OpenVpnWaitStatus Status { get; init; }
    public string? Detail { get; init; }
}

internal sealed class OpenVpnVersionResult
{
    public required bool Ok { get; init; }
    public required int ExitCode { get; init; }
    public string? VersionLine { get; init; }
    public string? SafeStdErr { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// Starts exactly one OpenVPN child with --route-nopull so the server cannot push
/// routes, block-outside-dns, or DHCP/DNS into the Windows routing table.
/// The .ovpn file is never rewritten. V0 does not use the management interface.
/// </summary>
internal sealed class OpenVpnProcess : IDisposable
{
    private readonly ConcurrentQueue<string> _rawLog = new();
    private Process? _process;
    private bool _stopping;

    public int? Pid => _process?.Id;

    public IReadOnlyList<string> RawLogLines => _rawLog.ToArray();

    /// <summary>
    /// Reads openvpn.exe --version from both stdout and stderr.
    /// BeginOutputReadLine + WaitForExit races on a process that exits immediately:
    /// the pipe can close before async line events are delivered, so the buffer stays empty.
    /// </summary>
    public static OpenVpnVersionResult ReadVersion(string openVpnPath)
        => ReadVersionAsync(openVpnPath).GetAwaiter().GetResult();

    private static async Task<OpenVpnVersionResult> ReadVersionAsync(string openVpnPath)
    {
        var start = new ProcessStartInfo
        {
            FileName = openVpnPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        start.ArgumentList.Add("--version");

        using var process = new Process { StartInfo = start };
        if (!process.Start())
        {
            return new OpenVpnVersionResult
            {
                Ok = false,
                ExitCode = -1,
                Error = "Failed to start openvpn.exe --version.",
            };
        }

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            await Task.WhenAll(stdoutTask, stderrTask, process.WaitForExitAsync(timeout.Token))
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            return new OpenVpnVersionResult
            {
                Ok = false,
                ExitCode = process.HasExited ? process.ExitCode : -1,
                Error = "Timed out waiting for openvpn.exe --version.",
            };
        }

        string stdout = await stdoutTask.ConfigureAwait(false);
        string stderr = await stderrTask.ConfigureAwait(false);
        int exitCode = process.ExitCode;
        string combined = stdout + stderr;
        string? versionLine = FindVersionLine(combined);
        string safeStdErr = SafePreview(stderr);

        if (exitCode != 0)
        {
            return new OpenVpnVersionResult
            {
                Ok = false,
                ExitCode = exitCode,
                VersionLine = versionLine,
                SafeStdErr = safeStdErr,
                Error = $"openvpn.exe --version exited with code {exitCode}.",
            };
        }

        if (string.IsNullOrWhiteSpace(combined))
        {
            return new OpenVpnVersionResult
            {
                Ok = false,
                ExitCode = exitCode,
                Error = "openvpn.exe --version produced no output.",
            };
        }

        if (versionLine is null)
        {
            return new OpenVpnVersionResult
            {
                Ok = false,
                ExitCode = exitCode,
                SafeStdErr = safeStdErr,
                Error = "openvpn.exe --version did not print a line starting with \"OpenVPN \".",
            };
        }

        return new OpenVpnVersionResult
        {
            Ok = true,
            ExitCode = exitCode,
            VersionLine = versionLine,
        };
    }

    private static string? FindVersionLine(string text)
        => text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(static l => l.Trim())
            .FirstOrDefault(static l => l.StartsWith("OpenVPN ", StringComparison.Ordinal));

    private static string SafePreview(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return string.Join(
            " | ",
            text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(OpenVpnLogRedactor.Redact)
                .Select(static l => l.Trim())
                .Where(static l => l.Length > 0)
                .Take(8));
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception)
        {
        }
    }

    public void Start(
        string openVpnPath,
        string profilePath,
        Action<string> onRedactedLine,
        IPAddress? hostRoute32 = null)
    {
        if (_process is not null)
        {
            throw new InvalidOperationException("OpenVPN process already started.");
        }

        string workingDirectory = Path.GetDirectoryName(profilePath)
            ?? throw new InvalidOperationException("Profile path has no directory.");

        var start = new ProcessStartInfo
        {
            FileName = openVpnPath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        start.ArgumentList.Add("--config");
        start.ArgumentList.Add(profilePath);
        start.ArgumentList.Add("--route-nopull");
        // Local --route is not a pushed server route; --route-nopull does not suppress it
        // (OpenVPN 2.6 man: route-nopull filters pull/push only).
        if (hostRoute32 is not null)
        {
            start.ArgumentList.Add("--route");
            start.ArgumentList.Add(hostRoute32.ToString());
            start.ArgumentList.Add("255.255.255.255");
            start.ArgumentList.Add("vpn_gateway");
        }

        start.ArgumentList.Add("--verb");
        start.ArgumentList.Add("3");

        _process = new Process { StartInfo = start, EnableRaisingEvents = true };
        _process.OutputDataReceived += (_, e) => HandleLine(e.Data, onRedactedLine);
        _process.ErrorDataReceived += (_, e) => HandleLine(e.Data, onRedactedLine);

        if (!_process.Start())
        {
            _process.Dispose();
            _process = null;
            throw new InvalidOperationException("Failed to start openvpn.exe.");
        }

        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    public async Task<OpenVpnWaitResult> WaitUntilConnectedAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (_process is null)
        {
            throw new InvalidOperationException("OpenVPN has not been started.");
        }

        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (LooksLikeAuthPrompt())
            {
                return new OpenVpnWaitResult
                {
                    Status = OpenVpnWaitStatus.AuthInteractionRequired,
                    Detail = "AUTH INTERACTION REQUIRED — not implemented in V0.",
                };
            }

            if (RawLogLines.Any(l => l.Contains("Initialization Sequence Completed", StringComparison.OrdinalIgnoreCase)))
            {
                return new OpenVpnWaitResult { Status = OpenVpnWaitStatus.Connected };
            }

            if (_process.HasExited)
            {
                return new OpenVpnWaitResult
                {
                    Status = OpenVpnWaitStatus.ProcessExited,
                    Detail = $"openvpn.exe exited with code {_process.ExitCode}.",
                };
            }

            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        }

        if (_process.HasExited)
        {
            return new OpenVpnWaitResult
            {
                Status = OpenVpnWaitStatus.ProcessExited,
                Detail = $"openvpn.exe exited with code {_process.ExitCode}.",
            };
        }

        return new OpenVpnWaitResult { Status = OpenVpnWaitStatus.TimedOut, Detail = $"Timed out after {timeout.TotalSeconds:0}s." };
    }

    public async Task StopAsync()
    {
        Process? process = _process;
        if (process is null || _stopping)
        {
            return;
        }

        _stopping = true;
        if (process.HasExited)
        {
            return;
        }

        try
        {
            process.CloseMainWindow();
        }
        catch (InvalidOperationException)
        {
        }

        try
        {
            process.StandardInput.Close();
        }
        catch (Exception)
        {
        }

        bool exited = await WaitForExitAsync(process, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        if (!exited && !process.HasExited)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception)
            {
            }

            await WaitForExitAsync(process, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        try
        {
            StopAsync().GetAwaiter().GetResult();
        }
        catch (Exception)
        {
        }

        _process?.Dispose();
        _process = null;
    }

    private void HandleLine(string? line, Action<string> onRedactedLine)
    {
        if (line is null)
        {
            return;
        }

        _rawLog.Enqueue(line);
        onRedactedLine(OpenVpnLogRedactor.Redact(line));
    }

    private bool LooksLikeAuthPrompt()
    {
        foreach (string line in RawLogLines)
        {
            if (line.Contains("Enter Auth Username", StringComparison.OrdinalIgnoreCase)
                || line.Contains("Enter Auth Password", StringComparison.OrdinalIgnoreCase)
                || line.Contains("Enter Private Key Password", StringComparison.OrdinalIgnoreCase)
                || line.Contains("Enter PEM pass phrase", StringComparison.OrdinalIgnoreCase)
                || line.Contains("Need PEM pass phrase", StringComparison.OrdinalIgnoreCase)
                || line.Contains("CHALLENGE:", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<bool> WaitForExitAsync(Process process, TimeSpan timeout)
    {
        try
        {
            using var cts = new CancellationTokenSource(timeout);
            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return process.HasExited;
        }
    }
}
