using System.Text.Json;

namespace SelectiveVpnRouter.Core.Portable;

public sealed class PortableBootstrapEngine
{
    private readonly ISystemBootstrapProbe _probe;
    private readonly WindowsBootstrapMutator _mutator;

    public PortableBootstrapEngine(ISystemBootstrapProbe? probe = null)
    {
        _probe = probe ?? new WindowsSystemBootstrapProbe();
        _mutator = new WindowsBootstrapMutator(_probe);
    }

    public PortableBootstrapStatus GetStatus(string portableRoot, AppConfiguration? config = null)
        => PortableBootstrapStatusEvaluator.Evaluate(portableRoot, _probe, config);

    public PortableBootstrapCommandResult Repair(string portableRoot, AppConfiguration? config = null)
    {
        if (!_probe.IsElevated())
        {
            return Fail(PortableBootstrapExitCodes.ElevationRequired, "Administrator elevation is required for repair.");
        }

        if (!PortableRootValidator.TryNormalizePortableRoot(portableRoot, out string root, out string? err))
        {
            return Fail(PortableBootstrapExitCodes.InvalidArguments, err ?? "Invalid portable root.");
        }

        PortableManifestDocument? manifest = PortableManifestIO.TryRead(root);
        if (manifest is null)
        {
            return Fail(PortableBootstrapExitCodes.Failed, "portable-manifest.json is missing or invalid.");
        }

        try
        {
            _mutator.EnsureProgramData();
            string serviceExe = PortableLayout.ExpectedServiceExePath(root);
            _mutator.InstallOrRepairService(root, serviceExe);
            string driverSys = PortableLayout.ExpectedDriverSysPath(root);
            _mutator.InstallOrRepairDriver(root, driverSys);
            PortableManifestIO.WriteInstalledStamp(manifest, serviceExe);
        }
        catch (PortableDriverSigningBlockedException ex)
        {
            PortableBootstrapStatus status = GetStatus(root, config) with
            {
                BootstrapState = PortableBootstrapState.Broken,
                Message = ex.Message,
                DriverSigningBlocked = true,
            };
            return new PortableBootstrapCommandResult
            {
                Success = false,
                ExitCode = PortableBootstrapExitCodes.DriverSigningBlocked,
                Message = ex.Message,
                Status = status,
            };
        }
        catch (Exception ex)
        {
            PortableBootstrapStatus status = GetStatus(root, config);
            return Fail(PortableBootstrapExitCodes.Failed, ex.Message, status);
        }

        PortableIpcSmokeResult ipc = PortableIpcProbe.WaitForGetStatusReady();
        PortableBootstrapStatus finalStatus = GetStatus(root, config);
        if (!ipc.Ready)
        {
            string message =
                $"Service did not respond to IPC after repair (attempts={ipc.Attempts}, elapsedMs={ipc.ElapsedMs}). "
                + (ipc.LastError ?? "Unknown IPC error.");
            return new PortableBootstrapCommandResult
            {
                Success = false,
                ExitCode = PortableBootstrapExitCodes.IpcFailed,
                Message = message,
                Status = finalStatus,
                IpcAttempts = ipc.Attempts,
                LastIpcError = ipc.LastError,
                IpcElapsedMs = ipc.ElapsedMs,
            };
        }

        return new PortableBootstrapCommandResult
        {
            Success = finalStatus.BootstrapState == PortableBootstrapState.Ready,
            ExitCode = finalStatus.BootstrapState == PortableBootstrapState.Ready
                ? PortableBootstrapExitCodes.Success
                : PortableBootstrapExitCodes.Failed,
            Message = finalStatus.BootstrapState == PortableBootstrapState.Ready
                ? "Bootstrap repair completed."
                : finalStatus.Message,
            Status = finalStatus,
            IpcAttempts = ipc.Attempts,
            IpcElapsedMs = ipc.ElapsedMs,
        };
    }

    public PortableBootstrapCommandResult Remove(string portableRoot, AppConfiguration? config = null)
    {
        if (!_probe.IsElevated())
        {
            return Fail(PortableBootstrapExitCodes.ElevationRequired, "Administrator elevation is required for remove.");
        }

        if (!PortableRootValidator.TryNormalizePortableRoot(portableRoot, out string root, out string? err))
        {
            return Fail(PortableBootstrapExitCodes.InvalidArguments, err ?? "Invalid portable root.");
        }

        try
        {
            if (_probe.ProbeProductService().Running)
            {
                PortableIpcProbe.TryEmergencyRestore();
            }

            _mutator.RemoveService();
            _mutator.RemoveDriver();
        }
        catch (Exception ex)
        {
            return Fail(PortableBootstrapExitCodes.Failed, ex.Message);
        }

        PortableBootstrapStatus status = GetStatus(root, config);
        return new PortableBootstrapCommandResult
        {
            Success = true,
            ExitCode = PortableBootstrapExitCodes.Success,
            Message = "System components removed. User config was preserved.",
            Status = status,
        };
    }

    public static string SerializeStatus(PortableBootstrapStatus status)
        => JsonSerializer.Serialize(status, ConfigSerializer.JsonOptions);

    private static PortableBootstrapCommandResult Fail(int code, string message, PortableBootstrapStatus? status = null) => new()
    {
        Success = false,
        ExitCode = code,
        Message = message,
        Status = status,
    };
}