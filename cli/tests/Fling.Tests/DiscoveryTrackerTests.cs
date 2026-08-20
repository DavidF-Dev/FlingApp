using Fling.Net;
using Fling.Operations;

namespace Fling.Tests;

public sealed class DiscoveryTrackerTests
{
    private long _now;

    private DiscoveryTracker Tracker(int staleSeconds = 6, int dropSeconds = 30) =>
        new(TimeSpan.FromSeconds(staleSeconds), TimeSpan.FromSeconds(dropSeconds), () => _now);

    private void Advance(int seconds) => _now += seconds * 1000;

    private static DiscoveredDevice Device(string name, string host = "10.0.0.1", int port = 7291) =>
        new(name, host, port);

    [Fact]
    public void Current_SameDeviceAcrossRounds_AppearsOnce()
    {
        var tracker = Tracker();

        tracker.Observe([Device("Pixel")]);
        Advance(2);
        tracker.Observe([Device("Pixel")]);
        Advance(2);
        tracker.Observe([Device("Pixel")]);

        Assert.Single(tracker.Current());
    }

    [Fact]
    public void Current_DeviceMissesARound_StaysVisibleAndFresh()
    {
        var tracker = Tracker();

        tracker.Observe([Device("Pixel")]);
        Advance(2);
        tracker.Observe([]);

        var current = tracker.Current();

        Assert.Single(current);
        Assert.False(current[0].IsStale);
    }

    [Fact]
    public void Current_DeviceGoneBeyondStaleWindow_IsMarkedStaleNotRemoved()
    {
        var tracker = Tracker(staleSeconds: 6);

        tracker.Observe([Device("Pixel")]);
        Advance(10);

        var current = tracker.Current();

        Assert.Single(current);
        Assert.True(current[0].IsStale);
    }

    [Fact]
    public void Current_DeviceGoneBeyondDropWindow_IsRemoved()
    {
        var tracker = Tracker(dropSeconds: 30);

        tracker.Observe([Device("Pixel")]);
        Advance(31);

        Assert.Empty(tracker.Current());
    }

    [Fact]
    public void Current_StaleDeviceSeenAgain_BecomesFresh()
    {
        var tracker = Tracker(staleSeconds: 6);

        tracker.Observe([Device("Pixel")]);
        Advance(10);
        Assert.True(tracker.Current()[0].IsStale);

        tracker.Observe([Device("Pixel")]);

        Assert.False(tracker.Current()[0].IsStale);
    }

    [Fact]
    public void Observe_AddressChanged_KeepsOneEntryWithTheNewAddress()
    {
        var tracker = Tracker();

        tracker.Observe([Device("Pixel", "10.0.0.1")]);
        Advance(2);
        tracker.Observe([Device("Pixel", "10.0.0.7")]);

        var current = tracker.Current();

        Assert.Single(current);
        Assert.Equal("10.0.0.7", current[0].Device.Host);
    }

    [Fact]
    public void Observe_NameDiffersOnlyByCase_IsTheSameDevice()
    {
        var tracker = Tracker();

        tracker.Observe([Device("Pixel")]);
        tracker.Observe([Device("PIXEL")]);

        Assert.Single(tracker.Current());
    }

    [Fact]
    public void Current_OrdersMostRecentlySeenFirst()
    {
        var tracker = Tracker();

        tracker.Observe([Device("Older")]);
        Advance(2);
        tracker.Observe([Device("Newer")]);

        var current = tracker.Current();

        Assert.Equal("Newer", current[0].Device.Name);
        Assert.Equal("Older", current[1].Device.Name);
    }

    [Fact]
    public void Clear_RemovesEverything()
    {
        var tracker = Tracker();
        tracker.Observe([Device("Pixel")]);

        tracker.Clear();

        Assert.Empty(tracker.Current());
    }
}
