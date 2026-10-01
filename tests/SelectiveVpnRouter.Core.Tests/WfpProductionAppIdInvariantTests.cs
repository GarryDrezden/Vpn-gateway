using SelectiveVpnRouter.Network;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public class WfpProductionAppIdInvariantTests
{
    [Fact]
    public void InstallAppFilter_source_uses_WfpAleAppIdBuilder_not_raw_fwpm_blob()
    {
        string path = LocateRepoFile(@"src\SelectiveVpnRouter.Network\WfpSession.cs");
        string source = File.ReadAllText(path);
        Assert.Contains("WfpAleAppIdBuilder.TryBuildNormalizedForFilePath", source, StringComparison.Ordinal);
        int installIdx = source.IndexOf("private WfpFilterInstallResult InstallAppFilter", StringComparison.Ordinal);
        Assert.True(installIdx >= 0, "InstallAppFilter not found");
        string installBody = source[installIdx..];
        Assert.DoesNotContain("TryAcquireAleAppIdBlob", installBody, StringComparison.Ordinal);
        Assert.DoesNotContain("FwpmGetAppIdFromFileName0", installBody, StringComparison.Ordinal);
    }

    [Fact]
    public void WfpAleAppIdBuilder_lowercases_latin_umlaut_for_runtime_match()
    {
        const string fwpm = @"\device\harddiskvolume4\programdata\vpn route wfp tests\Äpp Über\selectivevpnrouter.probe.exe";
        string expected = fwpm.ToLowerInvariant();
        Assert.True(WfpAleAppIdBuilder.TryBuildNormalizedFromFwpmCanonical(fwpm, out WfpOwnedAleAppIdBlob? blob, out string? err), err);
        using (blob!)
        {
            Assert.Equal(expected, blob.NormalizedAppId, StringComparer.Ordinal);
            byte[] bytes = WfpAleAppIdBuilder.GetUtf16LeBytesIncludingTerminator(expected);
            Assert.Equal((uint)bytes.Length, blob.ByteSize);
            Assert.True(bytes.Length >= 2 && bytes[^2] == 0 && bytes[^1] == 0);
        }
    }

    private static string LocateRepoFile(string relativePath)
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SelectiveVpnRouter.sln")))
            {
                string candidate = Path.Combine(dir.FullName, relativePath);
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                break;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Repo file not found: " + relativePath);
    }
}