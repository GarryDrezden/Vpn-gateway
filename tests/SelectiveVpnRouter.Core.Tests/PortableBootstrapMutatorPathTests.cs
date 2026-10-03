using SelectiveVpnRouter.Core.Portable;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public class PortableBootstrapMutatorPathTests
{
    [Fact]
    public void InstallOrRepairDriver_rejects_sys_outside_portable_package()
    {
        string root = CreatePortableRootWithSpaces();
        string sysInRoot = PortableLayout.ExpectedDriverSysPath(root);
        File.WriteAllBytes(sysInRoot, [0]);

        var mutator = new WindowsBootstrapMutator(new FakeBootstrapProbe());
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            mutator.InstallOrRepairDriver(root, @"C:\Other\SelectiveVpnCallout.sys"));
        Assert.Contains("outside the validated portable package", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InstallOrRepairDriver_passes_path_validation_reaches_signing_policy()
    {
        string root = CreatePortableRootWithSpaces();
        string sysPath = PortableLayout.ExpectedDriverSysPath(root);
        File.WriteAllBytes(sysPath, [0]);

        var mutator = new WindowsBootstrapMutator(new FakeBootstrapProbe());
        PortableDriverSigningBlockedException ex = Assert.Throws<PortableDriverSigningBlockedException>(() =>
            mutator.InstallOrRepairDriver(root, sysPath));
        Assert.Contains("signature", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static string CreatePortableRootWithSpaces()
    {
        string root = Path.Combine(Path.GetTempPath(), "VPN Route Portable UAC Test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, PortableLayout.DriverSubfolder));
        return root;
    }

    private sealed class FakeBootstrapProbe : ISystemBootstrapProbe
    {
        public ServiceProbeSnapshot ProbeProductService() => new();
        public DriverProbeSnapshot ProbeCalloutDriver() => new();
        public bool IsElevated() => true;
        public string? TryReadAuthenticodeStatus(string filePath) => "NotSigned";
    }
}