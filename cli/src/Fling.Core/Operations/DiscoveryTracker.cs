using Fling.Net;

namespace Fling.Operations;

public sealed record TrackedDevice(DiscoveredDevice Device, bool IsStale);

/// <summary>
/// Accumulates the results of repeated discovery broadcasts into a stable list.
/// </summary>
/// <remarks>
/// A single broadcast is a 1.5-second snapshot, so a phone that was asleep or busy is
/// simply absent from it. Dropping a device the moment one round misses it would make
/// the list flicker under the user's cursor, so an unseen device is marked stale first
/// and only removed once it has been gone long enough to be genuinely absent.
/// </remarks>
public sealed class DiscoveryTracker
{
    private readonly TimeSpan _staleAfter;
    private readonly TimeSpan _dropAfter;
    private readonly Func<long> _clock;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public DiscoveryTracker(TimeSpan? staleAfter = null, TimeSpan? dropAfter = null, Func<long>? clock = null)
    {
        _staleAfter = staleAfter ?? TimeSpan.FromSeconds(6);
        _dropAfter = dropAfter ?? TimeSpan.FromSeconds(30);
        _clock = clock ?? (() => Environment.TickCount64);
    }

    public void Observe(IEnumerable<DiscoveredDevice> devices)
    {
        var now = _clock();
        foreach (var device in devices)
            _entries[device.Name] = new Entry(device, now);
    }

    /// <summary>
    /// Devices seen recently enough to still be worth showing, most recently seen first.
    /// </summary>
    public IReadOnlyList<TrackedDevice> Current()
    {
        var now = _clock();

        foreach (var name in _entries.Where(e => Elapsed(now, e.Value) >= _dropAfter.TotalMilliseconds)
                     .Select(e => e.Key).ToList())
        {
            _entries.Remove(name);
        }

        return _entries.Values
            .OrderByDescending(e => e.LastSeen)
            .Select(e => new TrackedDevice(e.Device, Elapsed(now, e) >= _staleAfter.TotalMilliseconds))
            .ToList();
    }

    public void Clear() => _entries.Clear();

    private static double Elapsed(long now, Entry entry) => now - entry.LastSeen;

    private sealed record Entry(DiscoveredDevice Device, long LastSeen);
}
