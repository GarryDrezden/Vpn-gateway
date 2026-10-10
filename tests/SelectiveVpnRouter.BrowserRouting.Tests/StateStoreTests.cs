using System.Text;
using SelectiveVpnRouter.Core.BrowserRouting;
using Xunit;

namespace SelectiveVpnRouter.BrowserRouting.Tests;

public class StateStoreTests
{
    [Fact]
    public void First_start_creates_and_persists_empty_direct_state()
    {
        using var dir = new TempDir();
        var store = new BrowserRoutingStateStore(dir.File("state.json"));

        Assert.Equal(BrowserRoutingStoreStatus.Available, store.Load());
        var current = store.Current!;
        Assert.True(StateGenerationFormat.IsValid(current.StateGeneration));
        Assert.Equal(0, current.Revision);
        Assert.Equal("Direct", current.State.DefaultRoute);
        Assert.Equal(0, current.RuleCount);
        Assert.True(File.Exists(dir.File("state.json")));
        Assert.Contains("\"documentVersion\": 1", File.ReadAllText(dir.File("state.json")));
    }

    [Fact]
    public void Restart_preserves_generation_revision_and_rules()
    {
        using var dir = new TempDir();
        var path = dir.File("state.json");
        var first = new BrowserRoutingStateStore(path);
        first.Load();
        var generation = first.Current!.StateGeneration;
        first.Update(0, "VPN", Rules.Many(3));

        var second = new BrowserRoutingStateStore(path);
        Assert.Equal(BrowserRoutingStoreStatus.Available, second.Load());
        Assert.Equal(generation, second.Current!.StateGeneration);
        Assert.Equal(1, second.Current.Revision);
        Assert.Equal("VPN", second.Current.State.DefaultRoute);
        Assert.Equal(first.Current.State.Rules, second.Current.State.Rules);

        var third = new BrowserRoutingStateStore(path);
        third.Load();
        Assert.Equal(generation, third.Current!.StateGeneration);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("{\"documentType\":\"VpnRoute.BrowserRoutingState\",\"documentVersion\":1,\"stateGeneration\":\"bad\",\"schemaVersion\":1,\"revision\":0,\"defaultRoute\":\"Direct\",\"rules\":[]}")]
    [InlineData("{\"documentType\":\"VpnRoute.BrowserRoutingState\",\"documentVersion\":1,\"stateGeneration\":\"9b2f6c1e-1d2a-4f57-9a43-3f2a9d7c1b10\",\"schemaVersion\":1,\"revision\":0,\"defaultRoute\":\"Direct\",\"rules\":[{\"id\":\"a\",\"name\":\"A\",\"host\":\"NOT A HOST!!!\",\"matchType\":\"ExactHost\",\"routeMode\":\"VPN\",\"enabled\":true,\"source\":\"User\",\"notes\":null}]}")]
    [InlineData("{\"documentType\":\"VpnRoute.BrowserRoutingState\",\"documentVersion\":1,\"stateGeneration\":\"9b2f6c1e-1d2a-4f57-9a43-3f2a9d7c1b10\",\"schemaVersion\":1,\"revision\":0,\"defaultRoute\":\"Direct\",\"rules\":[],\"extra\":1}")]
    public void Corrupt_file_makes_state_unavailable_and_is_not_rewritten(string content)
    {
        using var dir = new TempDir();
        var path = dir.File("state.json");
        File.WriteAllText(path, content, new UTF8Encoding(false));
        var store = new BrowserRoutingStateStore(path);

        Assert.Equal(BrowserRoutingStoreStatus.Unavailable, store.Load());
        Assert.Null(store.Current);
        Assert.Equal(BrowserRoutingUnavailableReason.Corrupt, store.UnavailableReason);
        Assert.Equal(content, File.ReadAllText(path));
        Assert.Throws<BrowserRoutingUnavailableException>(() => store.Update(0, "Direct", []));
    }

    [Fact]
    public void Unsupported_document_version_is_unavailable()
    {
        using var dir = new TempDir();
        var path = dir.File("state.json");
        File.WriteAllText(path, "{\"documentType\":\"VpnRoute.BrowserRoutingState\",\"documentVersion\":2}");
        var store = new BrowserRoutingStateStore(path);
        Assert.Equal(BrowserRoutingStoreStatus.Unavailable, store.Load());
        Assert.Equal(BrowserRoutingUnavailableReason.UnsupportedDocument, store.UnavailableReason);
    }

    [Fact]
    public void Missing_primary_with_backup_is_not_silently_reinitialized()
    {
        using var dir = new TempDir();
        var path = dir.File("state.json");
        var store = new BrowserRoutingStateStore(path);
        store.Load();
        store.Update(0, "Direct", Rules.Many(2));
        Assert.True(File.Exists(dir.File("state.bak.json")));
        File.Delete(path);

        var restarted = new BrowserRoutingStateStore(path);
        Assert.Equal(BrowserRoutingStoreStatus.Unavailable, restarted.Load());
        Assert.Equal(BrowserRoutingUnavailableReason.PrimaryMissing, restarted.UnavailableReason);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Failed_write_keeps_the_previous_state_in_memory_and_on_disk()
    {
        using var dir = new TempDir();
        var path = dir.File("state.json");
        var store = new BrowserRoutingStateStore(path);
        store.Load();
        store.Update(0, "Direct", Rules.Many(2));
        var before = File.ReadAllBytes(path);
        var current = store.Current;

        store.BeforeCommitForTests = _ => throw new IOException("disk full");
        Assert.Throws<BrowserRoutingPersistenceException>(() => store.Update(1, "VPN", Rules.Many(5)));
        Assert.Throws<BrowserRoutingPersistenceException>(() => store.Reset());

        Assert.Same(current, store.Current);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.False(File.Exists(path + ".tmp"));
        store.BeforeCommitForTests = null;
        Assert.Equal(2, store.Update(1, "VPN", Rules.Many(5)).Revision);
    }

    [Fact]
    public void Initial_creation_failure_is_unavailable_not_an_unpersisted_generation()
    {
        using var dir = new TempDir();
        var store = new BrowserRoutingStateStore(dir.File("state.json")) { BeforeCommitForTests = _ => throw new IOException("denied") };
        Assert.Equal(BrowserRoutingStoreStatus.Unavailable, store.Load());
        Assert.Equal(BrowserRoutingUnavailableReason.PersistenceError, store.UnavailableReason);
    }

    [Fact]
    public void Reset_creates_a_new_generation_at_revision_zero()
    {
        using var dir = new TempDir();
        var store = new BrowserRoutingStateStore(dir.File("state.json"));
        store.Load();
        var old = store.Current!.StateGeneration;
        store.Update(0, "VPN", Rules.Many(4));

        var fresh = store.Reset();
        Assert.NotEqual(old, fresh.StateGeneration);
        Assert.Equal(0, fresh.Revision);
        Assert.Equal(0, fresh.RuleCount);
        Assert.Equal("Direct", fresh.State.DefaultRoute);

        var restarted = new BrowserRoutingStateStore(dir.File("state.json"));
        restarted.Load();
        Assert.Equal(fresh.StateGeneration, restarted.Current!.StateGeneration);
    }

    [Fact]
    public void Corrupt_state_recovers_only_through_explicit_reset()
    {
        using var dir = new TempDir();
        var path = dir.File("state.json");
        File.WriteAllText(path, "garbage");
        var store = new BrowserRoutingStateStore(path);
        Assert.Equal(BrowserRoutingStoreStatus.Unavailable, store.Load());

        var fresh = store.Reset();
        Assert.Equal(BrowserRoutingStoreStatus.Available, store.Status);
        Assert.Equal("garbage", File.ReadAllText(dir.File("state.bak.json")));
        Assert.Equal(fresh.StateGeneration, store.Current!.StateGeneration);
    }

    [Fact]
    public void Legacy_document_is_migrated_once_keeping_revision_and_rules()
    {
        using var dir = new TempDir();
        var path = dir.File("state.json");
        File.WriteAllText(path, "{\"schemaVersion\":1,\"revision\":17,\"defaultRoute\":\"VPN\",\"rules\":[" +
            "{\"id\":\"a\",\"name\":\"A\",\"host\":\"a.example\",\"matchType\":\"ExactHost\",\"routeMode\":\"Direct\",\"enabled\":true,\"source\":\"User\"}]}");

        var store = new BrowserRoutingStateStore(path);
        Assert.Equal(BrowserRoutingStoreStatus.Available, store.Load());
        Assert.Equal(17, store.Current!.Revision);
        Assert.Equal("a.example", Assert.Single(store.Current.State.Rules).Host);
        var generation = store.Current.StateGeneration;
        Assert.Contains("\"stateGeneration\": \"" + generation + "\"", File.ReadAllText(path));

        var restarted = new BrowserRoutingStateStore(path);
        restarted.Load();
        Assert.Equal(generation, restarted.Current!.StateGeneration);
        Assert.Equal(17, restarted.Current.Revision);
    }

    [Fact]
    public void Update_requires_the_current_revision_and_bumps_by_one()
    {
        using var dir = new TempDir();
        var store = new BrowserRoutingStateStore(dir.File("state.json"));
        store.Load();
        var generation = store.Current!.StateGeneration;

        var next = store.Update(0, "Direct", Rules.Many(1));
        Assert.Equal(1, next.Revision);
        Assert.Equal(generation, next.StateGeneration);
        var stale = Assert.Throws<BrowserRoutingConcurrencyException>(() => store.Update(0, "Direct", Rules.Many(2)));
        Assert.Equal(1, stale.CurrentRevision);
        Assert.Throws<BrowserRoutingValidationException>(() => store.Update(1, "Direct", [Rules.Make(1, "Bad Host")]));
        Assert.Equal(1, store.Current!.Revision);
    }

    [Fact]
    public async Task Concurrent_readers_always_see_a_coherent_snapshot()
    {
        using var dir = new TempDir();
        var store = new BrowserRoutingStateStore(dir.File("state.json"));
        store.Load();

        var writer = Task.Run(() =>
        {
            for (var i = 0; i < 40; i++)
                store.Update(i, "Direct", Rules.Many(i + 1));
        });
        var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            var checks = 0;
            while (!writer.IsCompleted || checks < 10)
            {
                var snapshot = store.Current!;
                Assert.Equal(snapshot.Revision, snapshot.RuleCount);
                checks++;
            }
        })).ToArray();
        await writer;
        await Task.WhenAll(readers);
        Assert.Equal(40, store.Current!.Revision);
    }
}
