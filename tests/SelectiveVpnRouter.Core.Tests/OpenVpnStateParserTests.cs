using SelectiveVpnRouter.Core;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public class OpenVpnStateParserTests
{
    [Fact]
    public void Exact_success_is_connected()
    {
        Assert.True(OpenVpnStateParser.IsConnected("Initialization Sequence Completed"));
    }

    [Fact]
    public void Success_with_surrounding_whitespace_is_connected()
    {
        Assert.True(OpenVpnStateParser.IsConnected("  Initialization Sequence Completed  "));
    }

    [Fact]
    public void Success_with_timestamp_prefix_is_connected()
    {
        Assert.True(OpenVpnStateParser.IsConnected("Wed Oct  1 22:00:00 2025 Initialization Sequence Completed"));
    }

    [Fact]
    public void Completed_with_errors_is_not_connected()
    {
        Assert.False(OpenVpnStateParser.IsConnected("Initialization Sequence Completed With Errors"));
    }

    [Fact]
    public void Unrelated_line_containing_substring_is_not_connected()
    {
        Assert.False(OpenVpnStateParser.IsConnected("Note: later Initialization Sequence Completed tomorrow"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_is_not_connected(string? line)
    {
        Assert.False(OpenVpnStateParser.IsConnected(line));
    }
}