using SelectiveVpnRouter.Core.Portable;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public class PortableRuntimeLayoutValidatorTests
{
    [Fact]
    public void Validate_fails_when_bootstrap_missing()
    {
        string root = CreateMinimalLayout(includeBootstrap: false);
        try
        {
            bool ok = PortableRuntimeLayoutValidator.TryValidate(root, out IReadOnlyList<string> missing);
            Assert.False(ok);
            Assert.Contains(PortableLayout.BootstrapExeName, missing);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Validate_passes_when_required_dev_layout_present()
    {
        string root = CreateMinimalLayout(includeBootstrap: true);
        try
        {
            bool ok = PortableRuntimeLayoutValidator.TryValidate(root, out IReadOnlyList<string> missing);
            Assert.True(ok);
            Assert.Empty(missing);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Missing_bootstrap_yields_broken_bootstrap_status()
    {
        string root = CreateMinimalLayout(includeBootstrap: false);
        try
        {
            PortableBootstrapStatus status = PortableBootstrapStatusEvaluator.Evaluate(
                root,
                new LayoutFakeBootstrapProbe());
            Assert.Equal(PortableBootstrapState.Broken, status.BootstrapState);
            Assert.Contains(PortableLayout.BootstrapExeName, status.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Bootstrap_present_not_broken_for_missing_files_only()
    {
        string root = CreateMinimalLayout(includeBootstrap: true);
        try
        {
            PortableBootstrapStatus status = PortableBootstrapStatusEvaluator.Evaluate(
                root,
                new LayoutFakeBootstrapProbe());
            Assert.NotEqual(PortableBootstrapState.Broken, status.BootstrapState);
            Assert.Equal(PortableBootstrapState.NeedsServiceRegistration, status.BootstrapState);
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static string CreateMinimalLayout(bool includeBootstrap)
    {
        string root = Path.Combine(Path.GetTempPath(), "vpn-dev-layout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, PortableLayout.DriverSubfolder));
        File.WriteAllText(PortableLayout.ExpectedServiceExePath(root), "fake");
        if (includeBootstrap)
        {
            File.WriteAllText(Path.Combine(root, PortableLayout.BootstrapExeName), "fake");
        }

        File.WriteAllText(Path.Combine(root, PortableLayout.AppExeName), "fake");
        File.WriteAllText(Path.Combine(root, PortableLayout.ProbeExeName), "fake");
        File.WriteAllText(PortableLayout.ExpectedDriverSysPath(root), "fake");
        File.WriteAllText(Path.Combine(root, PortableLayout.DriverSubfolder, PortableLayout.DriverInfName), "fake");
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

    private static void TryDelete(string root)
    {
        try { Directory.Delete(root, recursive: true); } catch { }
    }

    private sealed class LayoutFakeBootstrapProbe : ISystemBootstrapProbe
    {
        public ServiceProbeSnapshot ProbeProductService() => new();
        public DriverProbeSnapshot ProbeCalloutDriver() => new();
        public bool IsElevated() => true;
        public string? TryReadAuthenticodeStatus(string filePath) => "Valid";
        public bool IsTestSigningEnabled() => false;
    }
}