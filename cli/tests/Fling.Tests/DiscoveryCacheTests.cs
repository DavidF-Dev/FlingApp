using Fling.Net;

namespace Fling.Tests;

public sealed class DiscoveryCacheTests
{
    [Fact]
    public void TryGet_Empty_ReturnsFalse()
    {
        var cache = new DiscoveryCache();
        Assert.False(cache.TryGet("Pixel 8", out _, out _));
    }

    [Fact]
    public void Set_ThenGet_ReturnsValues()
    {
        var cache = new DiscoveryCache();
        cache.Set("Pixel 8", "192.168.1.50", 7291);

        Assert.True(cache.TryGet("Pixel 8", out var host, out var port));
        Assert.Equal("192.168.1.50", host);
        Assert.Equal(7291, port);
    }

    [Fact]
    public void TryGet_CaseInsensitive()
    {
        var cache = new DiscoveryCache();
        cache.Set("Pixel 8", "192.168.1.50", 7291);

        Assert.True(cache.TryGet("pixel 8", out _, out _));
        Assert.True(cache.TryGet("PIXEL 8", out _, out _));
    }

    [Fact]
    public void TryGet_Expired_ReturnsFalse()
    {
        var currentTime = 0L;
        var cache = new DiscoveryCache(
            ttl: TimeSpan.FromSeconds(60),
            clock: () => currentTime);

        cache.Set("Pixel 8", "192.168.1.50", 7291);
        Assert.True(cache.TryGet("Pixel 8", out _, out _));

        currentTime = 61_000;
        Assert.False(cache.TryGet("Pixel 8", out _, out _));
    }

    [Fact]
    public void TryGet_NotExpired_ReturnsTrue()
    {
        var currentTime = 0L;
        var cache = new DiscoveryCache(
            ttl: TimeSpan.FromSeconds(60),
            clock: () => currentTime);

        cache.Set("Pixel 8", "192.168.1.50", 7291);

        currentTime = 59_000;
        Assert.True(cache.TryGet("Pixel 8", out _, out _));
    }

    [Fact]
    public void Set_OverwritesPrevious()
    {
        var cache = new DiscoveryCache();
        cache.Set("Pixel 8", "192.168.1.50", 7291);
        cache.Set("Pixel 8", "10.0.0.5", 8080);

        Assert.True(cache.TryGet("Pixel 8", out var host, out var port));
        Assert.Equal("10.0.0.5", host);
        Assert.Equal(8080, port);
    }

    [Fact]
    public void MultipleDevices_Independent()
    {
        var cache = new DiscoveryCache();
        cache.Set("Pixel 8", "192.168.1.50", 7291);
        cache.Set("Galaxy S24", "192.168.1.51", 7291);

        Assert.True(cache.TryGet("Pixel 8", out var host1, out _));
        Assert.True(cache.TryGet("Galaxy S24", out var host2, out _));
        Assert.Equal("192.168.1.50", host1);
        Assert.Equal("192.168.1.51", host2);
    }
}
