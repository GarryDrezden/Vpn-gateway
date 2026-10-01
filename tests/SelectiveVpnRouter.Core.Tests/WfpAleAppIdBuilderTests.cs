using SelectiveVpnRouter.Network;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public class WfpAleAppIdBuilderTests
{
    [Fact]
    public void Normalized_utf16_includes_nul_and_lowercases_cyrillic()
    {
        const string fwpm =
            @"\device\harddiskvolume4\programdata\vpn route wfp tests\"
            + "\u0422\u0435\u0441\u0442 vpn route\\selectivevpnrouter.probe.exe";
        const string runtime =
            @"\device\harddiskvolume4\programdata\vpn route wfp tests\"
            + "\u0442\u0435\u0441\u0442 vpn route\\selectivevpnrouter.probe.exe";

        Assert.True(WfpAleAppIdBuilder.TryBuildNormalizedFromFwpmCanonical(fwpm, out WfpOwnedAleAppIdBlob? blob, out string? error), error);
        Assert.NotNull(blob);
        using (blob!)
        {
            Assert.Equal(runtime, blob.NormalizedAppId, StringComparer.Ordinal);
            Assert.Equal(fwpm, blob.FwpmCanonicalAppId, StringComparer.Ordinal);
            byte[] bytes = WfpAleAppIdBuilder.GetUtf16LeBytesIncludingTerminator(runtime);
            Assert.Equal((uint)bytes.Length, blob.ByteSize);
            Assert.True(WfpDiagnosticAleAppIdBlob.TryCopyBlobBytes(blob.BlobPointer, out byte[]? copied));
            Assert.True(WfpDiagnosticAleAppIdBlob.BytesEqual(bytes, copied));
        }
    }

    [Fact]
    public void Ascii_canonical_unchanged_by_invariant_lower()
    {
        const string ascii =
            @"\device\harddiskvolume4\programdata\svrprobeascii\selectivevpnrouter.probe.exe";
        Assert.True(WfpAleAppIdBuilder.TryBuildNormalizedFromFwpmCanonical(ascii, out WfpOwnedAleAppIdBlob? blob, out string? error), error);
        using (blob!)
        {
            Assert.Equal(ascii, blob.NormalizedAppId, StringComparer.Ordinal);
            byte[] expected = WfpAleAppIdBuilder.GetUtf16LeBytesIncludingTerminator(ascii);
            Assert.True(WfpDiagnosticAleAppIdBlob.TryCopyBlobBytes(blob.BlobPointer, out byte[]? copied));
            Assert.True(WfpDiagnosticAleAppIdBlob.BytesEqual(expected, copied));
        }
    }

    [Fact]
    public void Builder_dispose_is_idempotent()
    {
        Assert.True(WfpAleAppIdBuilder.TryBuildNormalizedFromFwpmCanonical("abc", out WfpOwnedAleAppIdBlob? blob, out _), "build");
        blob!.Dispose();
        blob.Dispose();
        Assert.Equal(IntPtr.Zero, blob.BlobPointer);
    }

    [Fact]
    public void Builder_dispose_frees_native_memory()
    {
        Assert.True(WfpAleAppIdBuilder.TryBuildNormalizedFromFwpmCanonical("abc", out WfpOwnedAleAppIdBlob? blob, out _), "build");
        IntPtr ptr = blob!.BlobPointer;
        Assert.NotEqual(IntPtr.Zero, ptr);
        blob.Dispose();
        Assert.Equal(IntPtr.Zero, blob.BlobPointer);
    }

    [Fact]
    public void TryBuild_for_existing_file_matches_fwpm_pipeline()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string temp = Path.Combine(Path.GetTempPath(), "svr-appid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            string exe = Path.Combine(temp, "Probe.exe");
            File.WriteAllText(exe, "x");
            Assert.True(WfpAppIdentity.TryResolveAleAppIdFromFileName(exe, out string fwpm, out _), "fwpm");
            Assert.True(WfpAleAppIdBuilder.TryBuildNormalizedForFilePath(exe, out WfpOwnedAleAppIdBlob? blob, out string? err), err);
            using (blob!)
            {
                Assert.Equal(fwpm.ToLowerInvariant(), blob.NormalizedAppId, StringComparer.Ordinal);
            }
        }
        finally
        {
            try { Directory.Delete(temp, true); } catch { }
        }
    }
}