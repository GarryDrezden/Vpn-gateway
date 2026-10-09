using System.Net;
using System.Net.Sockets;
using SelectiveVpnRouter.Core.BrowserRouting;
using SelectiveVpnRouter.Proxy.BrowserExplicit;
using SelectiveVpnRouter.Service;
using Xunit;

namespace SelectiveVpnRouter.BrowserRouting.Tests;

public class BrowserExplicitProxyLifecycleTests
{
    [Fact]
    public async Task F_lifecycle_start_publishes_ready()
    {
        var proxy = new FakeRuntimeProxy();
        var readiness = new RuntimeBrowserProxyReadiness();
        using var cts = new CancellationTokenSource();
        Task run = BrowserExplicitProxyLifecycle.RunUntilStoppedAsync(proxy, readiness, _ => { }, cts.Token);
        await WaitUntilAsync(() => readiness.GetStatus().Status == BrowserProxyStatus.Ready, TimeSpan.FromSeconds(5));
        Assert.Equal("127.0.0.1", readiness.GetStatus().EndpointHost);
        Assert.True(readiness.GetStatus().EndpointPort > 0);
        cts.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(BrowserProxyStatus.Unavailable, readiness.GetStatus().Status);
    }

    [Fact]
    public async Task G_real_proxy_endpoint_is_loopback_dynamic_port()
    {
        var egress = new RecordingVpnEgress();
        await using var proxy = new BrowserExplicitSocksProxy(egress);
        var readiness = new RuntimeBrowserProxyReadiness();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Task run = BrowserExplicitProxyLifecycle.RunUntilStoppedAsync(proxy, readiness, _ => { }, cts.Token);
        await WaitUntilAsync(() => readiness.GetStatus().Status == BrowserProxyStatus.Ready, TimeSpan.FromSeconds(5));
        int port = readiness.GetStatus().EndpointPort!.Value;
        Assert.True(port > 0);
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        cts.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task H_vpn_tunnel_down_does_not_clear_proxy_readiness()
    {
        var tunnel = new FakeTunnelReadiness(false);
        var dns = new SelectiveVpnRouter.Network.VpnInterfaceDnsResolver(
            tunnel,
            new SelectiveVpnRouter.Network.VpnSessionDnsStore());
        var egress = new VpnBoundTcpEgress(tunnel, dns);
        await using var proxy = new BrowserExplicitSocksProxy(egress);
        var readiness = new RuntimeBrowserProxyReadiness();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Task run = BrowserExplicitProxyLifecycle.RunUntilStoppedAsync(proxy, readiness, _ => { }, cts.Token);
        await WaitUntilAsync(() => readiness.GetStatus().Status == BrowserProxyStatus.Ready, TimeSpan.FromSeconds(5));
        Assert.False(tunnel.TryGetTunnelInterfaceIndex(out _));
        Assert.Equal(BrowserProxyStatus.Ready, readiness.GetStatus().Status);
        cts.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Slice4_post_start_runtime_failure_recovers_with_new_endpoint()
    {
        var egress = new RecordingVpnEgress { Handler = _ => ConnectToLoopbackEcho() };
        await using var proxy = new BrowserExplicitSocksProxy(egress);
        var readiness = new RuntimeBrowserProxyReadiness();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Task run = BrowserExplicitProxyLifecycle.RunUntilStoppedAsync(proxy, readiness, _ => { }, cts.Token);
        await WaitUntilAsync(() => readiness.GetStatus().Status == BrowserProxyStatus.Ready, TimeSpan.FromSeconds(5));
        int portA = readiness.GetStatus().EndpointPort!.Value;
        Assert.Equal(0, await SocksGreetingAsync(portA));
        await proxy.StopListeningAsync();
        await WaitUntilAsync(() => readiness.GetStatus().Status == BrowserProxyStatus.Unavailable, TimeSpan.FromSeconds(5));
        await WaitUntilAsync(
            () => readiness.GetStatus().Status == BrowserProxyStatus.Ready && readiness.GetStatus().EndpointPort != portA,
            TimeSpan.FromSeconds(20));
        Assert.NotEqual(portA, readiness.GetStatus().EndpointPort);
        Assert.True(proxy.Port > 0);
        cts.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task I_start_failure_sets_unavailable_then_recovers()
    {
        var proxy = new FakeRuntimeProxy { FailStarts = 1 };
        var readiness = new RuntimeBrowserProxyReadiness();
        var logs = new List<string>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Task run = BrowserExplicitProxyLifecycle.RunUntilStoppedAsync(proxy, readiness, logs.Add, cts.Token);
        await WaitUntilAsync(() => logs.Any(l => l.Contains("unavailable reason=")), TimeSpan.FromSeconds(5));
        Assert.Equal(BrowserProxyStatus.Unavailable, readiness.GetStatus().Status);
        await WaitUntilAsync(() => readiness.GetStatus().Status == BrowserProxyStatus.Ready, TimeSpan.FromSeconds(15));
        Assert.True(logs.Any(l => l.Contains("restarting delay=")));
        cts.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task L_restart_cycle_accepts_socks_on_new_port()
    {
        var egress = new RecordingVpnEgress { Handler = _ => ConnectToLoopbackEcho() };
        await using var proxy = new BrowserExplicitSocksProxy(egress);
        var readiness = new RuntimeBrowserProxyReadiness();

        await proxy.StartAsync(CancellationToken.None);
        readiness.SetReady("127.0.0.1", proxy.Port);
        int portA = proxy.Port;
        Assert.Equal(0, await SocksGreetingAsync(portA));

        await proxy.StopListeningAsync();
        readiness.SetUnavailable();
        await proxy.StartAsync(CancellationToken.None);
        readiness.SetReady("127.0.0.1", proxy.Port);
        int portB = proxy.Port;
        Assert.NotEqual(portA, portB);
        Assert.Equal(portB, readiness.GetStatus().EndpointPort);
        Assert.Equal(0, await SocksGreetingAsync(portB));
    }

    [Fact]
    public async Task M_shutdown_sets_unavailable_and_stops_restart()
    {
        var proxy = new FakeRuntimeProxy();
        var readiness = new RuntimeBrowserProxyReadiness();
        using var cts = new CancellationTokenSource();
        Task run = BrowserExplicitProxyLifecycle.RunUntilStoppedAsync(proxy, readiness, _ => { }, cts.Token);
        await WaitUntilAsync(() => proxy.StartCount > 0, TimeSpan.FromSeconds(5));
        cts.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(BrowserProxyStatus.Unavailable, readiness.GetStatus().Status);
        int starts = proxy.StartCount;
        await Task.Delay(100);
        Assert.Equal(starts, proxy.StartCount);
    }

    [Fact]
    public async Task O_stop_during_retry_delay_exits_promptly()
    {
        var proxy = new FakeRuntimeProxy { FailStarts = int.MaxValue };
        var readiness = new RuntimeBrowserProxyReadiness();
        using var cts = new CancellationTokenSource();
        Task run = BrowserExplicitProxyLifecycle.RunUntilStoppedAsync(proxy, readiness, _ => { }, cts.Token);
        await WaitUntilAsync(() => proxy.StartCount > 0, TimeSpan.FromSeconds(5));
        cts.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(BrowserProxyStatus.Unavailable, readiness.GetStatus().Status);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;
            await Task.Delay(20);
        }

        throw new TimeoutException("Condition not met.");
    }

    private static async Task<byte> SocksGreetingAsync(int port)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        using NetworkStream ns = client.GetStream();
        await ns.WriteAsync(new byte[] { 5, 1, 0 });
        var hello = new byte[2];
        await ns.ReadAsync(hello);
        return hello[1];
    }

    private static VpnConnectOutcome ConnectToLoopbackEcho()
    {
        var echo = new TcpListener(IPAddress.Loopback, 0);
        echo.Start();
        var endpoint = (IPEndPoint)echo.LocalEndpoint;
        _ = Task.Run(async () =>
        {
            try
            {
                using TcpClient c = await echo.AcceptTcpClientAsync();
            }
            catch
            {
            }
        });
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Connect(endpoint);
        return new VpnConnectOutcome(true, socket, Socks5ReplyCode.Success);
    }

    private sealed class FakeRuntimeProxy : IBrowserExplicitSocksProxyRuntime
    {
        private TaskCompletionSource _runtimeGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _postStartFailures;

        public int FailStarts { get; set; }
        public int StartCount { get; private set; }
        public int Port { get; private set; } = 19000;

        public void EnqueuePostStartFailure() => Interlocked.Increment(ref _postStartFailures);

        public Task StartAsync(CancellationToken cancellationToken)
        {
            StartCount++;
            if (FailStarts > 0)
            {
                FailStarts--;
                throw new InvalidOperationException("start failed");
            }

            Port = 20000 + StartCount;
            _runtimeGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (Interlocked.Decrement(ref _postStartFailures) >= 0)
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(30, cancellationToken);
                    _runtimeGate.TrySetException(new InvalidOperationException("accept loop died"));
                }, cancellationToken);
            }

            return Task.CompletedTask;
        }

        public Task WaitForRuntimeAsync(CancellationToken stoppingToken) => _runtimeGate.Task.WaitAsync(stoppingToken);

        public Task StopListeningAsync()
        {
            _runtimeGate.TrySetCanceled();
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTunnelReadiness(bool ready) : Core.BrowserRouting.IVpnTunnelEgressReadiness
    {
        public bool TryGetTunnelInterfaceIndex(out int interfaceIndex)
        {
            interfaceIndex = 0;
            return ready;
        }
    }

    private sealed class RecordingVpnEgress : IVpnTcpEgress
    {
        public Func<VpnConnectTarget, VpnConnectOutcome>? Handler { get; init; }

        public ValueTask<VpnConnectOutcome> ConnectAsync(VpnConnectTarget target, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Handler?.Invoke(target) ?? new VpnConnectOutcome(false, null, Socks5ReplyCode.NetworkUnreachable));
    }
}
