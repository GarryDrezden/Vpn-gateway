using System.ComponentModel;
using System.Diagnostics;
using SelectiveVpnRouter.Core.Portable;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public class PortableBootstrapLauncherTests
{
    [Fact]
    public void BuildRepairArguments_strips_trailing_backslash_before_quote()
    {
        string args = PortableBootstrapCommandLine.BuildRepairArguments(@"C:\Temp\VPN Route Portable Test\");
        Assert.Equal("repair --root \"C:\\Temp\\VPN Route Portable Test\"", args);
    }

    [Fact]
    public void CreateElevatedStartInfo_uses_runas_and_shell_execute()
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "vpn-launcher-test"));
        ProcessStartInfo psi = PortableBootstrapProcessLauncher.CreateElevatedStartInfo(
            root,
            PortableBootstrapCommandLine.BuildRepairArguments(root));
        Assert.True(psi.UseShellExecute);
        Assert.Equal("runas", psi.Verb);
        Assert.Equal(root, psi.WorkingDirectory);
        Assert.EndsWith(PortableLayout.BootstrapExeName, psi.FileName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LaunchRepair_uac_cancel_returns_elevation_cancelled()
    {
        string portableRoot = CreateFakePortableRoot();
        File.WriteAllText(Path.Combine(portableRoot, PortableLayout.BootstrapExeName), "fake");
        var launcher = new PortableBootstrapProcessLauncher(_ => throw new Win32Exception(1223));
        PortableBootstrapLaunchResult result = launcher.LaunchElevatedRepair(portableRoot);
        Assert.Equal(PortableBootstrapLaunchOutcome.ElevationCancelled, result.Outcome);
        Assert.Contains("отменена", result.UserMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LaunchRepair_process_start_null_is_launch_failed()
    {
        string portableRoot = CreateFakePortableRoot();
        File.WriteAllText(Path.Combine(portableRoot, PortableLayout.BootstrapExeName), "fake");
        var launcher = new PortableBootstrapProcessLauncher(_ => null);
        PortableBootstrapLaunchResult result = launcher.LaunchElevatedRepair(portableRoot);
        Assert.Equal(PortableBootstrapLaunchOutcome.BootstrapLaunchFailed, result.Outcome);
    }

    [Fact]
    public void InterpretPostLaunch_driver_signing_blocked_from_last_result()
    {
        string portableRoot = CreateFakePortableRoot();
        string resultFile = Path.Combine(Path.GetTempPath(), "bootstrap-last-result-" + Guid.NewGuid().ToString("N") + ".json");
        string? prior = Environment.GetEnvironmentVariable("VPN_ROUTE_BOOTSTRAP_LAST_RESULT_PATH");
        Environment.SetEnvironmentVariable("VPN_ROUTE_BOOTSTRAP_LAST_RESULT_PATH", resultFile);
        try
        {
        PortableBootstrapResultIO.WriteLastResult(
            "repair",
            new PortableBootstrapCommandResult
            {
                Success = false,
                ExitCode = PortableBootstrapExitCodes.DriverSigningBlocked,
                Message = "Driver signature is not trusted.",
                Status = new PortableBootstrapStatus
                {
                    BootstrapState = PortableBootstrapState.Broken,
                    DriverSigningBlocked = true,
                    Message = "Driver signature is not trusted.",
                    PortableRoot = portableRoot,
                },
            });

        var launcher = new PortableBootstrapProcessLauncher(_ => null);
        PortableBootstrapLaunchResult result = launcher.InterpretPostLaunch(portableRoot, 4);
        Assert.Equal(PortableBootstrapLaunchOutcome.DriverSigningBlocked, result.Outcome);
        Assert.Contains("signature", result.UserMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable("VPN_ROUTE_BOOTSTRAP_LAST_RESULT_PATH", prior);
            try { File.Delete(resultFile); } catch { }
        }
    }

    [Fact]

    public void Last_result_contains_final_status_and_ipc_metadata()
    {
        string portableRoot = CreateFakePortableRoot();
        string service = PortableLayout.ExpectedServiceExePath(portableRoot);
        string resultFile = Path.Combine(Path.GetTempPath(), "bootstrap-last-result-" + Guid.NewGuid().ToString("N") + ".json");
        string? prior = Environment.GetEnvironmentVariable("VPN_ROUTE_BOOTSTRAP_LAST_RESULT_PATH");
        Environment.SetEnvironmentVariable("VPN_ROUTE_BOOTSTRAP_LAST_RESULT_PATH", resultFile);
        try
        {
            var finalStatus = new PortableBootstrapStatus
            {
                BootstrapState = PortableBootstrapState.NeedsRepair,
                Message = "Service did not respond to IPC after repair.",
                PortableRoot = portableRoot,
                RegisteredServicePath = service,
                ExpectedServicePath = service,
                ServicePathMatches = true,
                ServiceRunning = true,
            };
            PortableBootstrapResultIO.WriteLastResult(
                "repair",
                new PortableBootstrapCommandResult
                {
                    Success = false,
                    ExitCode = PortableBootstrapExitCodes.IpcFailed,
                    Message = finalStatus.Message,
                    Status = finalStatus,
                    IpcAttempts = 12,
                    LastIpcError = "Unable to connect",
                    IpcElapsedMs = 15000,
                });

            PortableBootstrapLastResult? last = PortableBootstrapResultIO.TryReadLastResult();
            Assert.NotNull(last);
            Assert.Equal(PortableBootstrapExitCodes.IpcFailed, last!.ExitCode);
            Assert.True(last.Status?.ServicePathMatches);
            Assert.Equal(service, last.Status?.RegisteredServicePath);
            Assert.Equal(12, last.IpcAttempts);
            Assert.Equal("Unable to connect", last.LastIpcError);
        }
        finally
        {
            Environment.SetEnvironmentVariable("VPN_ROUTE_BOOTSTRAP_LAST_RESULT_PATH", prior);
            try { File.Delete(resultFile); } catch { }
        }
    }

    [Fact]
    public void Repair_non_elevated_returns_elevation_required()
    {
        string portableRoot = CreateFakePortableRoot();
        var engine = new PortableBootstrapEngine(new FakeBootstrapProbe { Elevated = false });
        PortableBootstrapCommandResult repair = engine.Repair(portableRoot);
        Assert.Equal(PortableBootstrapExitCodes.ElevationRequired, repair.ExitCode);
    }

    private static string CreateFakePortableRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "vpn-portable-launcher-" + Guid.NewGuid().ToString("N"));
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

    private sealed class FakeBootstrapProbe : ISystemBootstrapProbe
    {
        public bool Elevated { get; init; } = true;
        public ServiceProbeSnapshot Service { get; init; } = new();
        public DriverProbeSnapshot Driver { get; init; } = new();
        public ServiceProbeSnapshot ProbeProductService() => Service;
        public DriverProbeSnapshot ProbeCalloutDriver() => Driver;
        public bool IsElevated() => Elevated;
        public string? TryReadAuthenticodeStatus(string filePath) => "Valid";
    }
}