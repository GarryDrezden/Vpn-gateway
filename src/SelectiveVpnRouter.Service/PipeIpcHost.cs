using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using SelectiveVpnRouter.Core;

namespace SelectiveVpnRouter.Service;

public sealed class PipeIpcHost : BackgroundService
{
    private readonly RouterEngine _engine;
    private readonly SemaphoreSlim _connectGate = new(1, 1);

    public PipeIpcHost(RouterEngine engine) => _engine = engine;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _engine.Load();
        while (!stoppingToken.IsCancellationRequested)
        {
            NamedPipeServerStream pipe = CreatePipe();
            try
            {
                await pipe.WaitForConnectionAsync(stoppingToken).ConfigureAwait(false);
                _ = ServeClientSafeAsync(pipe, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                pipe.Dispose();
                return;
            }
            catch (Exception ex)
            {
                pipe.Dispose();
                _engine.Log("IPC accept: " + ex.Message);
            }
        }
    }

    private async Task ServeClientSafeAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        try
        {
            await ServeClientAsync(pipe, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _engine.Log("IPC client: " + ex.Message);
        }
        finally
        {
            pipe.Dispose();
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // RouterEngine is a DI singleton; Host disposes IAsyncDisposable singletons once on shutdown.
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task ServeClientAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        using var reader = new BinaryReader(pipe, Encoding.UTF8, leaveOpen: true);
        using var writer = new BinaryWriter(pipe, Encoding.UTF8, leaveOpen: true);
        while (pipe.IsConnected && !ct.IsCancellationRequested)
        {
            int len;
            try
            {
                len = reader.ReadInt32();
            }
            catch (EndOfStreamException)
            {
                return;
            }

            if (len <= 0 || len > 4_000_000)
            {
                return;
            }

            byte[] body = reader.ReadBytes(len);
            IpcRequest? req = JsonSerializer.Deserialize<IpcRequest>(body, ConfigSerializer.JsonOptions);
            IpcResponse resp = await DispatchAsync(req, ct).ConfigureAwait(false);
            byte[] outBody = JsonSerializer.SerializeToUtf8Bytes(resp, ConfigSerializer.JsonOptions);
            writer.Write(outBody.Length);
            writer.Write(outBody);
            writer.Flush();
        }
    }

    private async Task<IpcResponse> DispatchAsync(IpcRequest? req, CancellationToken ct)
    {
        if (req is null || req.Version != IpcProtocol.CurrentVersion)
        {
            return new IpcResponse { Id = req?.Id ?? "", Ok = false, Error = "Invalid IPC request." };
        }

        string? diagnosticName = null;
        if (string.Equals(req.Method, IpcMethods.RunDiagnostic, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(req.PayloadJson))
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(req.PayloadJson);
                if (doc.RootElement.TryGetProperty("name", out JsonElement n))
                {
                    diagnosticName = n.GetString();
                }
            }
            catch (Exception)
            {
            }
        }

        ServiceRuntimeContext.SetIpcActivity(req.Method, diagnosticName);
        try
        {
            string payload = req.Method switch
            {
                IpcMethods.GetStatus => JsonSerializer.Serialize(_engine.Snapshot(), ConfigSerializer.JsonOptions),
                IpcMethods.GetConfig => JsonSerializer.Serialize(_engine.Config, ConfigSerializer.JsonOptions),
                IpcMethods.SetConfig => SetConfig(req.PayloadJson),
                IpcMethods.ConnectVpn => await Connect(req.PayloadJson, ct).ConfigureAwait(false),
                IpcMethods.DisconnectVpn => await Disconnect().ConfigureAwait(false),
                IpcMethods.PauseRouting => Pause(true),
                IpcMethods.ResumeRouting => Pause(false),
                IpcMethods.EmergencyRestore => await Restore().ConfigureAwait(false),
                IpcMethods.RunDiagnostic => await Diag(req.PayloadJson, ct).ConfigureAwait(false),
                IpcMethods.ExportDiagnostics => JsonSerializer.Serialize(new { path = _engine.ExportDiagnosticsZip() }, ConfigSerializer.JsonOptions),
                IpcMethods.GetFlows => JsonSerializer.Serialize(_engine.Snapshot().Flows, ConfigSerializer.JsonOptions),
                IpcMethods.GetTempAppVpnStatus => JsonSerializer.Serialize(_engine.GetTempAppVpnStatus(), ConfigSerializer.JsonOptions),
                IpcMethods.GetTempAppVpnFlows => GetTempAppFlows(req.PayloadJson),
                IpcMethods.ApplyTempAppVpnRoute => await ApplyTempAppRoute(req.PayloadJson).ConfigureAwait(false),
                IpcMethods.RemoveTempAppVpnRoute => await RemoveTempAppRoute().ConfigureAwait(false),
                _ => throw new InvalidOperationException("Unknown method " + req.Method),
            };
            return new IpcResponse { Id = req.Id, Ok = true, PayloadJson = payload };
        }
        catch (Exception ex)
        {
            _engine.Log("IPC dispatch error method=" + req.Method + " diagnostic=" + (diagnosticName ?? "(none)") + " error=" + ex.Message);
            return new IpcResponse { Id = req.Id, Ok = false, Error = ex.Message };
        }
        finally
        {
            ServiceRuntimeContext.ClearIpcActivity();
        }
    }

    private string SetConfig(string? json)
    {
        AppConfiguration cfg = JsonSerializer.Deserialize<AppConfiguration>(json ?? "{}", ConfigSerializer.JsonOptions)
            ?? throw new InvalidOperationException("Invalid config.");
        _engine.SaveConfig(cfg);
        return json ?? "{}";
    }

    private async Task<string> Connect(string? json, CancellationToken ct)
    {
        if (!await _connectGate.WaitAsync(0, ct).ConfigureAwait(false))
        {
            throw new InvalidOperationException("VPN connect is already in progress.");
        }

        try
        {
            ConnectVpnRequest? req = string.IsNullOrWhiteSpace(json)
                ? null
                : JsonSerializer.Deserialize<ConnectVpnRequest>(json, ConfigSerializer.JsonOptions);
            string exe = req?.OpenVpnPath ?? _engine.Config.Vpn.OpenVpnPath;
            string profile = req?.ProfilePath ?? _engine.Config.Vpn.ProfilePath;
            _engine.Log("connect-request-received exe=" + exe + " profile=" + profile
                + " disableDco=" + (req?.DisableDco ?? _engine.Config.Vpn.CompatibilityDisableDco));
            await _engine.ConnectAsync(req, _engine.ServiceCancellationToken).ConfigureAwait(false);
            return JsonSerializer.Serialize(_engine.Snapshot(), ConfigSerializer.JsonOptions);
        }
        finally
        {
            _connectGate.Release();
        }
    }

    private async Task<string> Disconnect()
    {
        await _engine.DisconnectAsync().ConfigureAwait(false);
        return JsonSerializer.Serialize(_engine.Snapshot(), ConfigSerializer.JsonOptions);
    }

    private string Pause(bool pause)
    {
        _engine.PauseRouting(pause);
        return JsonSerializer.Serialize(_engine.Snapshot(), ConfigSerializer.JsonOptions);
    }

    private async Task<string> Restore()
    {
        await _engine.EmergencyRestoreAsync().ConfigureAwait(false);
        return JsonSerializer.Serialize(_engine.Snapshot(), ConfigSerializer.JsonOptions);
    }

    private async Task<string> Diag(string? json, CancellationToken ct)
    {
        string name = "admin";
        bool confirm = false;
        string? requestExePath = null;
        if (!string.IsNullOrWhiteSpace(json))
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("name", out JsonElement n))
            {
                name = n.GetString() ?? name;
            }
            if (doc.RootElement.TryGetProperty("confirm", out JsonElement c) && c.ValueKind is JsonValueKind.True)
            {
                confirm = true;
            }

            if (doc.RootElement.TryGetProperty("exePath", out JsonElement exePathElement))
            {
                requestExePath = exePathElement.GetString();
            }
            else if (doc.RootElement.TryGetProperty("telegramExePath", out JsonElement telegramExePathElement))
            {
                requestExePath = telegramExePathElement.GetString();
            }
        }

        DiagnosticResult r = await _engine.RunDiagnosticAsync(name, ct, confirm, requestExePath).ConfigureAwait(false);
        return JsonSerializer.Serialize(r, ConfigSerializer.JsonOptions);
    }

    private string GetTempAppFlows(string? json)
    {
        TempAppVpnFlowsRequest req = JsonSerializer.Deserialize<TempAppVpnFlowsRequest>(json ?? "{}", ConfigSerializer.JsonOptions)
            ?? throw new InvalidOperationException("Invalid temp app flows request.");
        TempAppVpnFlowsResponse response = _engine.GetTempAppVpnFlows(req.ExePath, req.MaxCount);
        return JsonSerializer.Serialize(response, ConfigSerializer.JsonOptions);
    }

    private async Task<string> ApplyTempAppRoute(string? json)
    {
        TempAppVpnRequest req = JsonSerializer.Deserialize<TempAppVpnRequest>(json ?? "{}", ConfigSerializer.JsonOptions)
            ?? throw new InvalidOperationException("Invalid temp app request.");
        TempAppVpnStatus status = await _engine.ApplyTempAppVpnRouteAsync(req.ExePath, req.IdentityPathMode).ConfigureAwait(false);
        return JsonSerializer.Serialize(status, ConfigSerializer.JsonOptions);
    }

    private async Task<string> RemoveTempAppRoute()
    {
        TempAppVpnStatus status = await _engine.RemoveTempAppVpnRouteAsync().ConfigureAwait(false);
        return JsonSerializer.Serialize(status, ConfigSerializer.JsonOptions);
    }

    private static NamedPipeServerStream CreatePipe()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Deny));

        return NamedPipeServerStreamAcl.Create(
            AppPaths.PipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            0,
            0,
            security);
    }
}
