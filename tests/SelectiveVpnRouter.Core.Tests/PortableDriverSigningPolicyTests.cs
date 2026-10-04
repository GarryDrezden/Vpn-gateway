using SelectiveVpnRouter.Core.Portable;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public class PortableDriverSigningPolicyTests
{
    [Theory]
    [InlineData("Valid", false, false)]
    [InlineData("Valid", true, false)]
    [InlineData("NotSigned", false, true)]
    [InlineData("NotSigned", true, false)]
    public void BlocksUnsignedInstall_matches_expected_policy(
        string authenticodeStatus,
        bool testSigningEnabled,
        bool expectedBlocks)
    {
        bool blocks = PortableDriverSigningPolicy.BlocksUnsignedInstall(authenticodeStatus, testSigningEnabled);
        Assert.Equal(expectedBlocks, blocks);
    }
}