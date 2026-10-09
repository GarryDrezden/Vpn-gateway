using System.Text.RegularExpressions;
using SelectiveVpnRouter.Core.BrowserRouting;
using Xunit;

namespace SelectiveVpnRouter.BrowserRouting.Tests;

/// <summary>Static guards on the browser routing sources: they define the privilege boundary.</summary>
public class SourceSecurityTests
{
    private static readonly string CoreDir = Path.Combine(Repo.Root, "src", "SelectiveVpnRouter.Core", "BrowserRouting");
    private static readonly string HostFile = Path.Combine(Repo.Root, "src", "SelectiveVpnRouter.Service", "BrowserRoutingPipeHost.cs");

    private static IEnumerable<(string File, string Text)> Sources() =>
        Directory.GetFiles(CoreDir, "*.cs").Append(HostFile).Select(f => (Path.GetFileName(f), File.ReadAllText(f)));

    [Theory]
    [InlineData(@"\bTcpListener\b")]
    [InlineData(@"\bSocket\b")]
    [InlineData(@"\bHttpListener\b")]
    [InlineData(@"\bUdpClient\b")]
    [InlineData(@"\bProcess\.Start\b")]
    [InlineData(@"\bRunAsClient\b")]
    [InlineData(@"\bImpersonateNamedPipeClient\b|\bGetImpersonationUserName\b")]
    [InlineData(@"WellKnownSidType\.WorldSid")]
    [InlineData(@"WellKnownSidType\.AuthenticatedUserSid")]
    [InlineData(@"WellKnownSidType\.BuiltinUsersSid")]
    [InlineData(@"\bConnectVpn|DisconnectVpn|SetConfig|EmergencyRestore|PauseRouting")]
    [InlineData(@"\bRegistry\b")]
    public void Browser_routing_code_does_not_use(string pattern)
    {
        foreach (var (file, text) in Sources())
            Assert.False(Regex.IsMatch(text, pattern), $"{file} matches {pattern}");
    }

    [Fact]
    public void Dispatcher_allowlist_includes_read_and_write_methods()
    {
        var methods = typeof(BrowserRoutingIpcProtocol.Methods).GetFields().Select(f => (string)f.GetValue(null)!).ToArray();
        Assert.Equal(
            ["deleteRule", "getManifest", "getPage", "resetRules", "upsertRule"],
            methods.Order(StringComparer.Ordinal));
        var sources = Directory.GetFiles(CoreDir, "BrowserRoutingIpcProtocol*.cs")
            .Select(f => File.ReadAllText(f))
            .Aggregate((a, b) => a + b);
        Assert.Equal(5, Regex.Matches(sources, @"BrowserRoutingIpcProtocol\.Methods\.\w+ =>").Count);
    }

    [Fact]
    public void Browser_endpoint_has_its_own_pipe_and_the_service_registers_it()
    {
        Assert.NotEqual(BrowserRoutingIpcProtocol.PipeName, SelectiveVpnRouter.Core.AppPaths.PipeName);
        var program = File.ReadAllText(Path.Combine(Repo.Root, "src", "SelectiveVpnRouter.Service", "Program.cs"));
        Assert.Contains("AddHostedService<BrowserRoutingPipeHost>()", program);
        Assert.Contains("AddBrowserExplicitProxyRuntime()", program);
        var host = File.ReadAllText(HostFile);
        Assert.Contains("IBrowserProxyReadiness proxyReadiness", host);
        Assert.DoesNotContain("new UnavailableBrowserProxyReadiness()", host);
    }

    [Fact]
    public void Runtime_proxy_readiness_starts_unavailable()
    {
        var status = new RuntimeBrowserProxyReadiness().GetStatus();
        Assert.Equal(BrowserProxyStatus.Unavailable, status.Status);
        Assert.Null(status.EndpointHost);
        Assert.Null(status.EndpointPort);
    }
}
