using SelectiveVpnRouter.Core.Portable;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public class PortableBootstrapTests
{
    [Fact]
    public void UNC_root_rejected()
    {
        Assert.False(PortableRootValidator.TryNormalizePortableRoot(@"\\server\share\vpn", out _, out string? err));
        Assert.Contains("UNC", err ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Fresh_machine_needs_service_registration()
    {
        string root = CreateFakePortableRoot();
        var probe = new FakeBootstrapProbe();
        PortableBootstrapStatus status = PortableBootstrapStatusEvaluator.Evaluate(root, probe);
        Assert.Equal(PortableBootstrapState.NeedsServiceRegistration, status.BootstrapState);
    }

    [Fact]
    public void Moved_folder_needs_repair()
    {
        string root = CreateFakePortableRoot();
        var probe = new FakeBootstrapProbe
        {
            Service = new ServiceProbeSnapshot
            {
                Installed = true,
                Running = true,
                ImagePath = @"C:\Old\SelectiveVpnRouter.Service.exe",
            },
            Driver = new DriverProbeSnapshot { Installed = true, Running = true },
        };
        PortableBootstrapStatus status = PortableBootstrapStatusEvaluator.Evaluate(root, probe);
        Assert.Equal(PortableBootstrapState.NeedsRepair, status.BootstrapState);
        Assert.False(status.ServicePathMatches);
    }

    [Fact]
    public void Ready_when_service_and_driver_match()
    {
        using var stampScope = BeginIsolatedStampScope();
        string root = CreateFakePortableRoot();
        string service = PortableLayout.ExpectedServiceExePath(root);
        WriteMatchingStamp(stampScope.StampPath, root, service);
        var probe = new FakeBootstrapProbe
        {
            Service = new ServiceProbeSnapshot
            {
                Installed = true,
                Running = true,
                ImagePath = "\"" + service + "\"",
            },
            Driver = new DriverProbeSnapshot { Installed = true, Running = true },
        };
        PortableBootstrapStatus status = PortableBootstrapStatusEvaluator.Evaluate(root, probe);
        Assert.Equal(PortableBootstrapState.Ready, status.BootstrapState);
        Assert.True(status.ServicePathMatches);
    }

    [Fact]
    public void Version_mismatch_when_stamp_differs()
    {
        string root = CreateFakePortableRoot();
        string service = PortableLayout.ExpectedServiceExePath(root);
        string stampDir = Path.Combine(Path.GetTempPath(), "vpn-portable-stamp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stampDir);
        string stampPath = Path.Combine(stampDir, PortableLayout.InstalledPackageStampFile);
        string? priorStampEnv = Environment.GetEnvironmentVariable("VPN_ROUTE_PORTABLE_STAMP_PATH");
        Environment.SetEnvironmentVariable("VPN_ROUTE_PORTABLE_STAMP_PATH", stampPath);
        try
        {
            File.WriteAllText(
                stampPath,
                "{\"productVersion\":\"0.1.0\",\"buildCommit\":\"abc\",\"servicePath\":\"" + service.Replace("\\", "\\\\") + "\"}");
            var probe = new FakeBootstrapProbe
            {
                Service = new ServiceProbeSnapshot
                {
                    Installed = true,
                    Running = true,
                    ImagePath = service,
                },
                Driver = new DriverProbeSnapshot { Installed = true, Running = true },
            };
            PortableBootstrapStatus status = PortableBootstrapStatusEvaluator.Evaluate(root, probe);
            Assert.Equal(PortableBootstrapState.VersionMismatch, status.BootstrapState);
        }
        finally
        {
            Environment.SetEnvironmentVariable("VPN_ROUTE_PORTABLE_STAMP_PATH", priorStampEnv);
            try { Directory.Delete(stampDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Driver_missing_needs_driver_registration()
    {
        using var stampScope = BeginIsolatedStampScope();
        string root = CreateFakePortableRoot();
        string service = PortableLayout.ExpectedServiceExePath(root);
        WriteMatchingStamp(stampScope.StampPath, root, service);
        var probe = new FakeBootstrapProbe
        {
            Service = new ServiceProbeSnapshot
            {
                Installed = true,
                Running = true,
                ImagePath = service,
            },
            Driver = new DriverProbeSnapshot { Installed = false },
        };
        PortableBootstrapStatus status = PortableBootstrapStatusEvaluator.Evaluate(root, probe);
        Assert.Equal(PortableBootstrapState.NeedsDriverRegistration, status.BootstrapState);
    }

    [Fact]
    public void Service_path_outside_root_rejected_by_mutator()
    {
        string root = CreateFakePortableRoot();
        var mutator = new WindowsBootstrapMutator(new FakeBootstrapProbe());
        Assert.Throws<InvalidOperationException>(() =>
            mutator.InstallOrRepairService(root, @"C:\Windows\System32\cmd.exe"));
    }

    [Fact]
    public void Work_vpn_flag_still_disabled_by_default()
    {
        Assert.False(FeatureFlags.WorkVpn);
    }


    private static IsolatedStampScope BeginIsolatedStampScope()
    {
        string stampDir = Path.Combine(Path.GetTempPath(), "vpn-portable-stamp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stampDir);
        string stampPath = Path.Combine(stampDir, PortableLayout.InstalledPackageStampFile);
        string? priorStampEnv = Environment.GetEnvironmentVariable("VPN_ROUTE_PORTABLE_STAMP_PATH");
        Environment.SetEnvironmentVariable("VPN_ROUTE_PORTABLE_STAMP_PATH", stampPath);
        return new IsolatedStampScope(priorStampEnv, stampDir, stampPath);
    }

    private static void WriteMatchingStamp(string stampPath, string root, string servicePath)
    {
        PortableManifestDocument? manifest = PortableManifestIO.TryRead(root);
        var stamp = new PortablePackageStamp
        {
            ProductVersion = manifest?.ProductVersion ?? "0.2.0",
            BuildCommit = manifest?.BuildCommit ?? "test",
            ServicePath = servicePath,
            InstalledAt = DateTimeOffset.UtcNow,
        };
        File.WriteAllText(stampPath, System.Text.Json.JsonSerializer.Serialize(stamp, ConfigSerializer.JsonOptions));
    }

    private sealed class IsolatedStampScope : IDisposable
    {
        private readonly string? _priorStampEnv;
        private readonly string _stampDir;

        public IsolatedStampScope(string? priorStampEnv, string stampDir, string stampPath)
        {
            _priorStampEnv = priorStampEnv;
            _stampDir = stampDir;
            StampPath = stampPath;
        }

        public string StampPath { get; }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("VPN_ROUTE_PORTABLE_STAMP_PATH", _priorStampEnv);
            try { Directory.Delete(_stampDir, recursive: true); } catch { }
        }
    }
    private static string CreateFakePortableRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "vpn-portable-test-" + Guid.NewGuid().ToString("N"));
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


    [Fact]
    public void Post_repair_status_uses_fresh_probe_path()
    {
        using var stampScope = BeginIsolatedStampScope();
        string root = CreateFakePortableRoot();
        string service = PortableLayout.ExpectedServiceExePath(root);
        WriteMatchingStamp(stampScope.StampPath, root, service);
        var probe = new MutableFakeBootstrapProbe
        {
            Service = new ServiceProbeSnapshot
            {
                Installed = true,
                Running = true,
                ImagePath = @"C:\Old\SelectiveVpnRouter.Service.exe",
            },
            Driver = new DriverProbeSnapshot { Installed = true, Running = true },
        };
        PortableBootstrapStatus before = PortableBootstrapStatusEvaluator.Evaluate(root, probe);
        Assert.False(before.ServicePathMatches);
        probe.Service = probe.Service with { ImagePath = service };
        PortableBootstrapStatus after = PortableBootstrapStatusEvaluator.Evaluate(root, probe);
        Assert.True(after.ServicePathMatches);
        Assert.Equal(PortableBootstrapState.Ready, after.BootstrapState);
    }
    private sealed class MutableFakeBootstrapProbe : ISystemBootstrapProbe
    {
        public ServiceProbeSnapshot Service { get; set; } = new();
        public DriverProbeSnapshot Driver { get; init; } = new();
        public bool Elevated { get; init; }

        public ServiceProbeSnapshot ProbeProductService() => Service;
        public DriverProbeSnapshot ProbeCalloutDriver() => Driver;
        public bool IsElevated() => Elevated;
        public string? TryReadAuthenticodeStatus(string filePath) => "Valid";
        public bool IsTestSigningEnabled() => false;
    }

    private sealed class FakeBootstrapProbe : ISystemBootstrapProbe
    {
        public ServiceProbeSnapshot Service { get; set; } = new();
        public DriverProbeSnapshot Driver { get; init; } = new();
        public bool Elevated { get; init; }

        public ServiceProbeSnapshot ProbeProductService() => Service;
        public DriverProbeSnapshot ProbeCalloutDriver() => Driver;
        public bool IsElevated() => Elevated;
        public string? TryReadAuthenticodeStatus(string filePath) => "Valid";
        public bool IsTestSigningEnabled() => false;
    }
}
