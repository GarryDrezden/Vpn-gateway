using System.Text.Json;
using SelectiveVpnRouter.Core.BrowserRouting;
using Xunit;

namespace SelectiveVpnRouter.BrowserRouting.Tests;

/// <summary>
/// Offline contract fixture for Browser Integration Port v1 manifest (Slice 0). Does not assert live Service output.
/// </summary>
public class IntegrationManifestV1ContractTests
{
    private static readonly string[] RequiredCapabilities =
    [
        "browserRoutingState",
        "browserExplicitSocks",
        "vpnEgressReadiness",
        "browserClientHeartbeat",
    ];

    [Fact]
    public void Integration_manifest_v1_example_matches_approved_contract()
    {
        string path = Path.Combine(
            Repo.Root,
            "tests",
            "contracts",
            "browser-routing-v1",
            "integration-manifest-v1.example.json");
        Assert.True(File.Exists(path), path);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = doc.RootElement;
        Assert.Equal(1, root.GetProperty("integrationApiVersion").GetInt32());
        var caps = root.GetProperty("capabilities").EnumerateArray().Select(e => e.GetString()).ToHashSet(StringComparer.Ordinal);
        foreach (string cap in RequiredCapabilities)
            Assert.True(caps.Contains(cap), "missing capability: " + cap);
        Assert.True(root.TryGetProperty("browserProxy", out _));
        Assert.True(root.TryGetProperty("vpnEgress", out _));
        Assert.True(root.TryGetProperty("browserClient", out JsonElement client));
        string status = client.GetProperty("status").GetString() ?? "";
        Assert.Contains(status, new[] { "NeverSeen", "RecentlySeen", "Stale" });
    }

    [Fact]
    public void Browser_client_recently_seen_ttl_target_is_120_seconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(120), BrowserClientRecentlySeenTtl.Value);
    }
}
