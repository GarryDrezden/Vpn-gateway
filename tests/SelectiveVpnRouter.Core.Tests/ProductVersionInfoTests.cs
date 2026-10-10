using SelectiveVpnRouter.Core;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public class ProductVersionInfoTests
{
    [Fact]
    public void Desktop_file_version_is_1_0_0_15()
    {
        Assert.Equal("1.0.0.15", ProductVersionInfo.FileVersion);
    }

    [Fact]
    public void Desktop_display_version_is_1_0_0_RC15()
    {
        Assert.Equal("1.0.0 RC15", ProductVersionInfo.DisplayVersion);
    }

    [Fact]
    public void Product_version_base_is_1_0_0_not_1_0_14()
    {
        Assert.Equal("1.0.0", ProductVersionInfo.ProductVersion);
        Assert.Equal(15, ProductVersionInfo.ReleaseRevision);
        Assert.Equal("RC", ProductVersionInfo.ReleaseChannel);
        Assert.NotEqual("1.0.14", ProductVersionInfo.ProductVersion);
    }

    [Fact]
    public void Snapshot_carries_rc_identity_fields()
    {
        ProductReleaseSnapshot snap = ProductVersionInfo.ToSnapshot();
        Assert.Equal("1.0.0", snap.ProductVersion);
        Assert.Equal("RC", snap.ReleaseChannel);
        Assert.Equal(15, snap.ReleaseRevision);
        Assert.Equal("1.0.0 RC15", snap.DisplayVersion);
        Assert.Equal("1.0.0.15", snap.FileVersion);
    }

    [Theory]
    [InlineData("1.0.0", "RC", 15, "1.0.0 RC15")]
    [InlineData("1.0.0", "stable", 0, "1.0.0")]
    public void BuildDisplayVersion_follows_channel_model(string product, string channel, int revision, string expected)
    {
        Assert.Equal(expected, ProductVersionInfo.BuildDisplayVersion(product, channel, revision, informationalVersion: null));
    }
}
