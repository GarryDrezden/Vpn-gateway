using System.Buffers.Binary;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using SelectiveVpnRouter.Core.BrowserRouting;
using Xunit;

namespace SelectiveVpnRouter.BrowserRouting.Tests;

public sealed class PipeServerTests : IAsyncLifetime
{
    private readonly string _pipeName = "SelectiveVpnRouter.BrowserRouting.Test." + Guid.NewGuid().ToString("N");
    private readonly CancellationTokenSource _stop = new();
    private readonly List<string> _log = [];
    private BrowserRoutingSnapshot _snapshot = Rules.Snapshot(Rules.Many(3000));
    private Task? _server;

    public Task InitializeAsync()
    {
        var dispatcher = DispatcherTestFactory.Create(() => _snapshot);
        var server = new BrowserRoutingPipeServer(_pipeName, dispatcher, line => { lock (_log) _log.Add(line); });
        _server = Task.Run(() => server.RunAsync(_stop.Token));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _stop.CancelAsync();
        try
        {
            await _server!;
        }
        catch (OperationCanceledException)
        {
        }
        _stop.Dispose();
    }

    private async Task<NamedPipeClientStream> ConnectAsync()
    {
        var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
        await pipe.ConnectAsync(5000);
        return pipe;
    }

    private async Task<byte[]> ExchangeAsync(byte[] request)
    {
        await using var pipe = await ConnectAsync();
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, request.Length);
        await pipe.WriteAsync(header);
        await pipe.WriteAsync(request);
        await pipe.FlushAsync();
        return await ReadFrameAsync(pipe);
    }

    private static async Task<byte[]> ReadFrameAsync(Stream pipe)
    {
        var header = new byte[4];
        await pipe.ReadExactlyAsync(header);
        var body = new byte[BinaryPrimitives.ReadInt32LittleEndian(header)];
        await pipe.ReadExactlyAsync(body);
        return body;
    }

    [Fact]
    public async Task Serves_manifest_and_all_pages_over_a_real_pipe()
    {
        var manifest = Ipc.Parse(await ExchangeAsync(Ipc.Manifest())).GetProperty("result");
        Assert.Equal(3000, manifest.GetProperty("ruleCount").GetInt32());
        var generation = manifest.GetProperty("stateGeneration").GetString()!;

        var received = 0;
        var next = 0;
        while (next < 3000)
        {
            var bytes = await ExchangeAsync(Ipc.Page(generation, 42, next));
            Assert.True(bytes.Length <= BrowserRoutingIpcProtocol.MaxResponseBytes);
            var page = Ipc.Parse(bytes).GetProperty("result");
            received += page.GetProperty("rules").GetArrayLength();
            var nextIndex = page.GetProperty("nextIndex");
            next = nextIndex.ValueKind == System.Text.Json.JsonValueKind.Null ? 3000 : nextIndex.GetInt32();
        }
        Assert.Equal(3000, received);
    }

    [Fact]
    public async Task Snapshot_change_between_pages_is_reported_over_the_pipe()
    {
        var generation = _snapshot.StateGeneration;
        Assert.Null(Ipc.ErrorCode(await ExchangeAsync(Ipc.Page(generation, 42, 0))));
        _snapshot = Rules.Snapshot(Rules.Many(3000), revision: 43, generation: generation);
        Assert.Equal("snapshot_changed", Ipc.ErrorCode(await ExchangeAsync(Ipc.Page(generation, 42, 1000))));
    }

    [Fact]
    public async Task Oversized_frame_header_gets_an_error_and_disconnect()
    {
        await using var pipe = await ConnectAsync();
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, 64 * 1024 * 1024);
        await pipe.WriteAsync(header);
        await pipe.FlushAsync();
        Assert.Equal("invalid_request", Ipc.ErrorCode(await ReadFrameAsync(pipe)));
        Assert.Equal(0, await pipe.ReadAsync(new byte[1]));
    }

    [Fact]
    public async Task Silent_client_is_disconnected_after_the_timeout()
    {
        await using var pipe = await ConnectAsync();
        using var guard = new CancellationTokenSource(BrowserRoutingPipeServer.ClientTimeout + TimeSpan.FromSeconds(5));
        Assert.Equal(0, await pipe.ReadAsync(new byte[1], guard.Token));
    }

    [Fact]
    public async Task Parallel_clients_are_all_served()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(i => ExchangeAsync(Ipc.Manifest("c" + i))));
        Assert.All(results, r => Assert.Null(Ipc.ErrorCode(r)));
    }

    [Fact]
    public async Task Second_server_on_the_same_name_refuses_to_start()
    {
        await ExchangeAsync(Ipc.Manifest());
        var dispatcher = DispatcherTestFactory.Create(() => _snapshot);
        var squatter = new BrowserRoutingPipeServer(_pipeName, dispatcher, _ => { });
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAnyAsync<Exception>(() => squatter.RunAsync(cts.Token));
        Assert.False(cts.IsCancellationRequested);
    }

    [Fact]
    public async Task Live_pipe_dacl_grants_interactive_read_write_only_and_denies_network()
    {
        await using var pipe = await ConnectAsync();
        var rules = pipe.GetAccessControl().GetAccessRules(true, false, typeof(SecurityIdentifier)).Cast<PipeAccessRule>().ToList();
        AssertDacl(rules);
    }

    [Fact]
    public void Pipe_security_never_grants_broad_principals()
    {
        var security = BrowserRoutingPipeServer.CreatePipeSecurity(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null));
        var rules = security.GetAccessRules(true, false, typeof(SecurityIdentifier)).Cast<PipeAccessRule>().ToList();
        AssertDacl(rules);
        Assert.Equal(4, rules.Count);
        Assert.True(security.AreAccessRulesProtected);
    }

    private static void AssertDacl(List<PipeAccessRule> rules)
    {
        static SecurityIdentifier Sid(WellKnownSidType type) => new(type, null);
        foreach (var broad in new[] { WellKnownSidType.WorldSid, WellKnownSidType.AuthenticatedUserSid,
                     WellKnownSidType.AnonymousSid, WellKnownSidType.BuiltinUsersSid, WellKnownSidType.BuiltinGuestsSid })
            Assert.DoesNotContain(rules, r => r.IdentityReference.Equals(Sid(broad)) && r.AccessControlType == AccessControlType.Allow);

        var interactive = Assert.Single(rules, r => r.IdentityReference.Equals(Sid(WellKnownSidType.InteractiveSid)));
        Assert.Equal(AccessControlType.Allow, interactive.AccessControlType);
        Assert.Equal(0, (int)(interactive.PipeAccessRights & PipeAccessRights.CreateNewInstance));
        Assert.Equal(0, (int)(interactive.PipeAccessRights & (PipeAccessRights.ChangePermissions | PipeAccessRights.TakeOwnership)));
        Assert.True(interactive.PipeAccessRights.HasFlag(PipeAccessRights.ReadData));
        Assert.True(interactive.PipeAccessRights.HasFlag(PipeAccessRights.WriteData));

        Assert.Contains(rules, r => r.IdentityReference.Equals(Sid(WellKnownSidType.NetworkSid)) && r.AccessControlType == AccessControlType.Deny);
    }
}
