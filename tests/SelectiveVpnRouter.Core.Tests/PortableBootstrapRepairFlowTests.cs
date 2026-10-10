using SelectiveVpnRouter.Core.Portable;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

[Collection("PortableBootstrapSerial")]
public class PortableBootstrapRepairFlowTests
{
    [Fact]
    public void Repair_waits_for_running_service_before_ipc()
    {
        using IsolatedStampScope stampScope = BeginIsolatedStampScope();
        int serviceWaits = 0;
        int ipcCalls = 0;
        PortableBootstrapPostRepairProbe.WaitForProductServiceRunningOverrideForTests = _ =>
        {
            serviceWaits++;
            return new WindowsServiceScmSync.WaitOutcome(true, null);
        };
        PortableIpcProbe.GetStatusAttemptOverrideForTests = _ =>
        {
            ipcCalls++;
            return ipcCalls < 2
                ? new PortableIpcProbe.GetStatusAttemptResult(false, true, "Unable to connect", false)
                : new PortableIpcProbe.GetStatusAttemptResult(true, true, null, false);
        };

        string root = CreateFakePortableRoot();
        string service = PortableLayout.ExpectedServiceExePath(root);
        string driverSys = PortableLayout.ExpectedDriverSysPath(root);
        var engine = new PortableBootstrapEngine(
            new FakeBootstrapProbe
            {
                Elevated = true,
                Service = new ServiceProbeSnapshot { Installed = true, Running = true, ImagePath = service },
                Driver = new DriverProbeSnapshot { Installed = true, Running = true, BinaryPath = driverSys },
            },
            new NoOpBootstrapMutator());
        try
        {
            PortableBootstrapCommandResult result = engine.Repair(root);
            Assert.True(result.Success);
            Assert.Equal(1, serviceWaits);
            Assert.True(ipcCalls >= 2);
        }
        finally
        {
            PortableBootstrapPostRepairProbe.WaitForProductServiceRunningOverrideForTests = null;
            PortableIpcProbe.GetStatusAttemptOverrideForTests = null;
        }
    }

    [Fact]
    public void Repair_fails_when_service_never_reaches_running()
    {
        using IsolatedStampScope stampScope = BeginIsolatedStampScope();
        PortableBootstrapPostRepairProbe.WaitForProductServiceRunningOverrideForTests = _ =>
            new WindowsServiceScmSync.WaitOutcome(false, "SCM state=Stopped");
        string root = CreateFakePortableRoot();
        var engine = new PortableBootstrapEngine(
            new FakeBootstrapProbe { Elevated = true },
            new NoOpBootstrapMutator());
        try
        {
            PortableBootstrapCommandResult result = engine.Repair(root);
            Assert.False(result.Success);
            Assert.Equal(PortableBootstrapExitCodes.ServiceRepairFailed, result.ExitCode);
            Assert.Contains("Running", result.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            PortableBootstrapPostRepairProbe.WaitForProductServiceRunningOverrideForTests = null;
        }
    }

    [Fact]
    public void Repair_fails_with_ipc_message_when_pipe_never_ready()
    {
        using IsolatedStampScope stampScope = BeginIsolatedStampScope();
        PortableBootstrapPostRepairProbe.WaitForProductServiceRunningOverrideForTests = _ =>
            new WindowsServiceScmSync.WaitOutcome(true, null);
        PortableIpcProbe.GetStatusAttemptOverrideForTests = _ =>
            new PortableIpcProbe.GetStatusAttemptResult(false, true, "Unable to connect", false);
        PortableBootstrapPostRepairProbe.IpcReadinessWaitMsOverrideForTests = 400;
        string root = CreateFakePortableRoot();
        var engine = new PortableBootstrapEngine(
            new FakeBootstrapProbe { Elevated = true },
            new NoOpBootstrapMutator());
        try
        {
            PortableBootstrapCommandResult result = engine.Repair(
                root);
            Assert.False(result.Success);
            Assert.Equal(PortableBootstrapExitCodes.IpcFailed, result.ExitCode);
            Assert.Contains("running but did not respond to IPC", result.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            PortableBootstrapPostRepairProbe.WaitForProductServiceRunningOverrideForTests = null;
            PortableBootstrapPostRepairProbe.IpcReadinessWaitMsOverrideForTests = null;
            PortableIpcProbe.GetStatusAttemptOverrideForTests = null;
        }
    }

    [Fact]
    public void Create_retry_policy_treats_1072_as_transitional()
    {
        Assert.True(WindowsServiceScmSync.IsMarkedForDeleteScFailure(1072, "ERROR_SERVICE_MARKED_FOR_DELETE"));
    }

    private static IsolatedStampScope BeginIsolatedStampScope()
    {
        string stampDir = Path.Combine(Path.GetTempPath(), "vpn-portable-stamp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stampDir);
        string stampPath = Path.Combine(stampDir, PortableLayout.InstalledPackageStampFile);
        string? priorStampEnv = Environment.GetEnvironmentVariable("VPN_ROUTE_PORTABLE_STAMP_PATH");
        Environment.SetEnvironmentVariable("VPN_ROUTE_PORTABLE_STAMP_PATH", stampPath);
        return new IsolatedStampScope(priorStampEnv, stampDir);
    }

    private sealed class IsolatedStampScope : IDisposable
    {
        private readonly string? _priorStampEnv;
        private readonly string _stampDir;

        public IsolatedStampScope(string? priorStampEnv, string stampDir)
        {
            _priorStampEnv = priorStampEnv;
            _stampDir = stampDir;
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("VPN_ROUTE_PORTABLE_STAMP_PATH", _priorStampEnv);
            try { Directory.Delete(_stampDir, recursive: true); } catch { }
        }
    }

    private static string CreateFakePortableRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "vpn-portable-repair-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, PortableLayout.DriverSubfolder));
        File.WriteAllText(PortableLayout.ExpectedServiceExePath(root), "fake");
        File.WriteAllText(Path.Combine(root, PortableLayout.BootstrapExeName), "fake");
        File.WriteAllText(PortableLayout.ExpectedDriverSysPath(root), "fake");
        var manifest = new PortableManifestDocument
        {
            ProductVersion = "0.2.0",
            BuildCommit = "test",
            DriverVersion = "10.0.2.0",
        };
        File.WriteAllText(
            PortableLayout.ExpectedManifestPath(root),
            System.Text.Json.JsonSerializer.Serialize(manifest, ConfigSerializer.JsonOptions));
        return root;
    }

    private sealed class NoOpBootstrapMutator : IBootstrapSystemMutator
    {
        public void EnsureProgramData()
        {
        }

        public void InstallOrRepairService(string portableRoot, string serviceExePath)
        {
        }

        public void InstallOrRepairDriver(string portableRoot, string sysPath)
        {
        }

        public void RemoveService()
        {
        }

        public void RemoveDriver()
        {
        }
    }

    private sealed class FakeBootstrapProbe : ISystemBootstrapProbe
    {
        public bool Elevated { get; init; }
        public ServiceProbeSnapshot Service { get; init; } = new() { Installed = true, Running = true };
        public DriverProbeSnapshot Driver { get; init; } = new() { Installed = true, Running = true };

        public ServiceProbeSnapshot ProbeProductService() => Service;
        public DriverProbeSnapshot ProbeCalloutDriver() => Driver;
        public bool IsElevated() => Elevated;
        public string? TryReadAuthenticodeStatus(string filePath) => "Valid";
        public bool IsTestSigningEnabled() => false;
    }
}
