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

    public PipeIpcHost(RouterEngine engine) => _engine = engine;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _engine.Load();
        while (!stoppingToken.IsCancellationRequested)
        {
            using NamedPipeServerStream pipe = CreatePipe();
            try
            {
                await pipe.WaitForConnectionAsync(stoppingToken).ConfigureAwait(false);
                await ServeClientAsync(pipe, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _engine.Log("IPC: " + ex.Message);
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await _engine.DisposeAsync().ConfigureAwait(false);
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
                _ => throw new InvalidOperationException("Unknown method " + req.Method),
            };
            return new IpcResponse { Id = req.Id, Ok = true, PayloadJson = payload };
        }
        catch (Exception ex)
        {
            return new IpcResponse { Id = req.Id, Ok = false, Error = ex.Message };
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
        ConnectVpnRequest? req = string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<ConnectVpnRequest>(json, ConfigSerializer.JsonOptions);
        await _engine.ConnectAsync(req, ct).ConfigureAwait(false);
        return JsonSerializer.Serialize(_engine.Snapshot(), ConfigSerializer.JsonOptions);
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
        if (!string.IsNullOrWhiteSpace(json))
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("name", out JsonElement n))
            {
                name = n.GetString() ?? name;
            }
        }

        DiagnosticResult r = await _engine.RunDiagnosticAsync(name, ct).ConfigureAwait(false);
        return JsonSerializer.Serialize(r, ConfigSerializer.JsonOptions);
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
