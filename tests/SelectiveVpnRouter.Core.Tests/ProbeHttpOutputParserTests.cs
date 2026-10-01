using SelectiveVpnRouter.Core;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public class ProbeHttpOutputParserTests
{
    [Fact]
    public void TryParsePublicIpv4_parses_standalone_body_line()
    {
        string output = "http OK 376ms local 192.168.50.200:58234 bytes=223\r\n\r\n83.143.157.1\r\n";
        Assert.Equal("83.143.157.1", ProbeHttpOutputParser.TryParsePublicIpv4(output));
    }

    [Fact]
    public void TryParsePublicIpv4_parses_legacy_public_IP_prefix()
    {
        Assert.Equal("91.184.250.53", ProbeHttpOutputParser.TryParsePublicIpv4("public IP 91.184.250.53"));
    }

    [Fact]
    public void TryParsePublicIpv4_ignores_http_ok_local_line()
    {
        string line = "http OK 10ms local 192.168.1.2:12345 bytes=200";
        Assert.Null(ProbeHttpOutputParser.TryParsePublicIpv4(line));
    }
}