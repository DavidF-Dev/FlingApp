namespace Fling.Net;

/// <summary>
/// In-memory cache of discovered device addresses with a configurable TTL.
/// </summary>
public sealed class DiscoveryCache
{
    private readonly TimeSpan _ttl;
    private readonly Func<long> _clock;
    private readonly Dictionary<string, CacheEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public DiscoveryCache(TimeSpan? ttl = null, Func<long>? clock = null)
    {
        _ttl = ttl ?? TimeSpan.FromSeconds(60);
        _clock = clock ?? (() => Environment.TickCount64);
    }

    public bool TryGet(string deviceName, out string host, out int port)
    {
        if (_entries.TryGetValue(deviceName, out var entry) && _clock() - entry.Timestamp < _ttl.TotalMilliseconds)
        {
            host = entry.Host;
            port = entry.Port;
            return true;
        }

        host = "";
        port = 0;
        return false;
    }

    public void Set(string deviceName, string host, int port)
    {
        _entries[deviceName] = new CacheEntry(host, port, _clock());
    }

    private sealed record CacheEntry(string Host, int Port, long Timestamp);
}
