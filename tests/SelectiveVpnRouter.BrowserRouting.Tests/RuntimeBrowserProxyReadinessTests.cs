using SelectiveVpnRouter.Core.BrowserRouting;
using Xunit;

namespace SelectiveVpnRouter.BrowserRouting.Tests;

public class RuntimeBrowserProxyReadinessTests
{
    [Fact]
    public void A_initial_state_unavailable()
    {
        var readiness = new RuntimeBrowserProxyReadiness();
        BrowserProxyStatus status = readiness.GetStatus();
        Assert.Equal(BrowserProxyStatus.Unavailable, status.Status);
        Assert.Null(status.EndpointHost);
        Assert.Null(status.EndpointPort);
    }

    [Fact]
    public void B_set_ready_reports_exact_endpoint()
    {
        var readiness = new RuntimeBrowserProxyReadiness();
        readiness.SetReady("127.0.0.1", 18080);
        BrowserProxyStatus status = readiness.GetStatus();
        Assert.Equal(BrowserProxyStatus.Ready, status.Status);
        Assert.Equal("127.0.0.1", status.EndpointHost);
        Assert.Equal(18080, status.EndpointPort);
    }

    [Fact]
    public void C_set_unavailable_clears_endpoint()
    {
        var readiness = new RuntimeBrowserProxyReadiness();
        readiness.SetReady(1080);
        readiness.SetUnavailable();
        BrowserProxyStatus status = readiness.GetStatus();
        Assert.Equal(BrowserProxyStatus.Unavailable, status.Status);
        Assert.Null(status.EndpointPort);
    }

    [Fact]
    public void D_concurrent_reads_and_updates_are_safe()
    {
        var readiness = new RuntimeBrowserProxyReadiness();
        Parallel.For(0, 200, i =>
        {
            if (i % 2 == 0)
                readiness.SetReady(10000 + i);
            else
                readiness.SetUnavailable();
            _ = readiness.GetStatus();
        });
    }

    [Fact]
    public void E_endpoint_change_reflected_immediately()
    {
        var readiness = new RuntimeBrowserProxyReadiness();
        readiness.SetReady(50001);
        Assert.Equal(50001, readiness.GetStatus().EndpointPort);
        readiness.SetReady(50042);
        Assert.Equal(50042, readiness.GetStatus().EndpointPort);
    }
}
