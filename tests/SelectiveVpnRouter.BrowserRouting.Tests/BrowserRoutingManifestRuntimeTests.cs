using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using SelectiveVpnRouter.Core.BrowserRouting;
using SelectiveVpnRouter.Proxy.BrowserExplicit;
using SelectiveVpnRouter.Service;
using Xunit;

namespace SelectiveVpnRouter.BrowserRouting.Tests;

public class BrowserRoutingManifestRuntimeTests
{
    [Fact]
    public async Task Manifest_reports_runtime_ready_endpoint_matching_listener()
    {
        var egress = new RecordingVpnEgress();
        await using var proxy = new BrowserExplicitSocksProxy(egress);
        var readiness = new RuntimeBrowserProxyReadiness();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Task lifecycle = BrowserExplicitProxyLifecycle.RunUntilStoppedAsync(proxy, readiness, _ => { }, cts.Token);
        await WaitReadyAsync(readiness);

        var snapshot = Rules.Snapshot([], revision: 7);
        var dispatcher = DispatcherTestFactory.Create(snapshot, proxyReadiness: readiness);
        JsonElement manifest = Ipc.Parse(dispatcher.Dispatch(Ipc.Manifest()).Response).GetProperty("result");
        JsonElement browserProxy = manifest.GetProperty("browserProxy");
        Assert.Equal("Ready", browserProxy.GetProperty("status").GetString());
        int port = browserProxy.GetProperty("endpoint").GetProperty("port").GetInt32();
        Assert.Equal("127.0.0.1", browserProxy.GetProperty("endpoint").GetProperty("host").GetString());
        Assert.Equal(readiness.GetStatus().EndpointPort, port);
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);

        cts.Cancel();
        await lifecycle.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Manifest_keeps_revision_while_endpoint_changes_after_restart()
    {
        var egress = new RecordingVpnEgress { Handler = _ => ConnectOutcome() };
        await using var proxy = new BrowserExplicitSocksProxy(egress);
        var readiness = new RuntimeBrowserProxyReadiness();
        string generation = StateGenerationFormat.New();
        var snapshot = Rules.Snapshot([], revision: 99, generation: generation);
        var dispatcher = DispatcherTestFactory.Create(snapshot, proxyReadiness: readiness);

        await proxy.StartAsync(CancellationToken.None);
        readiness.SetReady(proxy.Port);
        int portA = proxy.Port;
        JsonElement first = Ipc.Parse(dispatcher.Dispatch(Ipc.Manifest()).Response).GetProperty("result");
        Assert.Equal(99, first.GetProperty("revision").GetInt64());
        Assert.Equal(generation, first.GetProperty("stateGeneration").GetString());
        Assert.Equal(portA, first.GetProperty("browserProxy").GetProperty("endpoint").GetProperty("port").GetInt32());

        await proxy.StopListeningAsync();
        readiness.SetUnavailable();
        await proxy.StartAsync(CancellationToken.None);
        readiness.SetReady(proxy.Port);
        int portB = proxy.Port;
        Assert.NotEqual(portA, portB);

        JsonElement second = Ipc.Parse(dispatcher.Dispatch(Ipc.Manifest()).Response).GetProperty("result");
        Assert.Equal(99, second.GetProperty("revision").GetInt64());
        Assert.Equal(generation, second.GetProperty("stateGeneration").GetString());
        Assert.Equal(portB, second.GetProperty("browserProxy").GetProperty("endpoint").GetProperty("port").GetInt32());
    }

    private static async Task WaitReadyAsync(RuntimeBrowserProxyReadiness readiness)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (readiness.GetStatus().Status == BrowserProxyStatus.Ready)
                return;
            await Task.Delay(20);
        }

        throw new TimeoutException("Proxy readiness not Ready.");
    }

    private static VpnConnectOutcome ConnectOutcome()
    {
        var echo = new TcpListener(IPAddress.Loopback, 0);
        echo.Start();
        var endpoint = (IPEndPoint)echo.LocalEndpoint;
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Connect(endpoint);
        return new VpnConnectOutcome(true, socket, Socks5ReplyCode.Success);
    }

    private sealed class RecordingVpnEgress : IVpnTcpEgress
    {
        public Func<VpnConnectTarget, VpnConnectOutcome>? Handler { get; init; }

        public ValueTask<VpnConnectOutcome> ConnectAsync(VpnConnectTarget target, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Handler?.Invoke(target) ?? new VpnConnectOutcome(false, null, Socks5ReplyCode.NetworkUnreachable));
    }
}
