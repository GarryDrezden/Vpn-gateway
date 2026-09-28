using SelectiveVpnRouter.Core;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public class ProbeCopyHelperTests
{
    [Fact]
    public void PrepareProbeCopy_creates_full_payload_in_separate_directories()
    {
        string? source = ProbeCopyHelper.FindProbeSourceDirectory();
        if (source is null)
        {
            return;
        }

        string root = Path.Combine(Path.GetTempPath(), "svr-probe-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            string vpnExe = ProbeCopyHelper.PrepareProbeCopy(Path.Combine(root, "vpn"));
            string directExe = ProbeCopyHelper.PrepareProbeCopy(Path.Combine(root, "direct"));

            Assert.NotEqual(Path.GetDirectoryName(vpnExe), Path.GetDirectoryName(directExe));
            Assert.True(File.Exists(vpnExe));
            Assert.True(File.Exists(directExe));

            foreach (string dir in new[] { Path.GetDirectoryName(vpnExe)!, Path.GetDirectoryName(directExe)! })
            {
                Assert.True(ProbeCopyHelper.HasMinimumPayload(dir));
                Assert.True(File.Exists(Path.Combine(dir, ProbeCopyHelper.ProbeDllName)));
                Assert.True(File.Exists(Path.Combine(dir, ProbeCopyHelper.ProbeDepsName)));
                Assert.True(File.Exists(Path.Combine(dir, ProbeCopyHelper.ProbeRuntimeConfigName)));
            }
        }
        finally
        {
            ProbeCopyHelper.Cleanup(root);
        }
    }
}