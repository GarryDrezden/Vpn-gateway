using System.Collections.Concurrent;
using System.Net;

namespace SelectiveVpnRouter.Core;

public sealed class DnsCache
{
    private readonly ConcurrentDictionary<string, DnsCacheEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeSpan _defaultTtl;

    public DnsCache(TimeSpan? defaultTtl = null)
    {
        _defaultTtl = defaultTtl ?? TimeSpan.FromMinutes(5);
    }

    public void Set(string host, IReadOnlyList<IPAddress> addresses, TimeSpan? ttl = null)
    {
        _entries[DomainWildcard.NormalizeHost(host)] = new DnsCacheEntry(
            addresses.ToArray(),
            DateTimeOffset.UtcNow + (ttl ?? _defaultTtl));
    }

    public IReadOnlyList<IPAddress>? Get(string host, DateTimeOffset? now = null)
    {
        DateTimeOffset t = now ?? DateTimeOffset.UtcNow;
        if (!_entries.TryGetValue(DomainWildcard.NormalizeHost(host), out DnsCacheEntry? entry))
        {
            return null;
        }

        if (entry.ExpiresUtc <= t)
        {
            _entries.TryRemove(DomainWildcard.NormalizeHost(host), out _);
            return null;
        }

        return entry.Addresses;
    }

    public IReadOnlyList<string> ExpiredHosts(DateTimeOffset? now = null)
    {
        DateTimeOffset t = now ?? DateTimeOffset.UtcNow;
        return _entries.Where(kv => kv.Value.ExpiresUtc <= t).Select(kv => kv.Key).ToList();
    }

    public void Remove(string host) => _entries.TryRemove(DomainWildcard.NormalizeHost(host), out _);

    public IReadOnlyDictionary<string, IReadOnlyList<IPAddress>> Snapshot(DateTimeOffset? now = null)
    {
        DateTimeOffset t = now ?? DateTimeOffset.UtcNow;
        return _entries
            .Where(kv => kv.Value.ExpiresUtc > t)
            .ToDictionary(kv => kv.Key, kv => (IReadOnlyList<IPAddress>)kv.Value.Addresses, StringComparer.OrdinalIgnoreCase);
    }

    private sealed record DnsCacheEntry(IReadOnlyList<IPAddress> Addresses, DateTimeOffset ExpiresUtc);
}
