using Fling.Config;
using Fling.Net;

namespace Fling.Tests;

public sealed class DeviceResolverDiscoveryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _configPath;

    public DeviceResolverDiscoveryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"fling_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _configPath = Path.Combine(_tempDir, "config.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public async Task ResolveAddresses_UsesCachedIp()
    {
        var config = CreateConfig(Device("Pixel 8", "10.0.0.1"));
        var store = new ConfigStore(_configPath);
        store.Save(config);

        var cache = new DiscoveryCache();
        cache.Set("Pixel 8", "192.168.1.50", 7291);

        var resolver = new DeviceResolver(config, store, cache, new UdpDiscovery(TimeSpan.FromMilliseconds(1)));
        var devices = resolver.Resolve(null, all: true);

        await resolver.ResolveAddressesAsync(devices);

        Assert.Equal("192.168.1.50", devices[0].Host);
        Assert.Equal(7291, devices[0].Port);
    }

    [Fact]
    public async Task ResolveAddresses_UpdatesConfigOnIpChange()
    {
        var config = CreateConfig(Device("Pixel 8", "10.0.0.1"));
        var store = new ConfigStore(_configPath);
        store.Save(config);

        var cache = new DiscoveryCache();
        cache.Set("Pixel 8", "192.168.1.50", 7291);

        var resolver = new DeviceResolver(config, store, cache, new UdpDiscovery(TimeSpan.FromMilliseconds(1)));
        var devices = resolver.Resolve(null, all: true);

        await resolver.ResolveAddressesAsync(devices);

        var reloaded = store.Load();
        Assert.Equal("192.168.1.50", reloaded.Devices[0].Host);
    }

    [Fact]
    public async Task ResolveAddresses_SameIp_DoesNotSave()
    {
        var config = CreateConfig(Device("Pixel 8", "192.168.1.50"));
        var store = new ConfigStore(_configPath);
        store.Save(config);
        var lastWrite = File.GetLastWriteTimeUtc(_configPath);

        var cache = new DiscoveryCache();
        cache.Set("Pixel 8", "192.168.1.50", 7291);

        await Task.Delay(50);

        var resolver = new DeviceResolver(config, store, cache, new UdpDiscovery(TimeSpan.FromMilliseconds(1)));
        var devices = resolver.Resolve(null, all: true);
        await resolver.ResolveAddressesAsync(devices);

        Assert.Equal(lastWrite, File.GetLastWriteTimeUtc(_configPath));
    }

    [Fact]
    public async Task ResolveAddresses_NoDiscoveryObjects_Noop()
    {
        var config = CreateConfig(Device("Pixel 8", "10.0.0.1"));
        var resolver = new DeviceResolver(config);
        var devices = resolver.Resolve(null, all: true);

        await resolver.ResolveAddressesAsync(devices);

        Assert.Equal("10.0.0.1", devices[0].Host);
    }

    [Fact]
    public void Resolve_StillWorksWithoutDiscovery()
    {
        var config = CreateConfig(Device("Pixel 8", "10.0.0.1"), Device("Galaxy", "10.0.0.2"));
        var resolver = new DeviceResolver(config);

        var result = resolver.Resolve("pixel 8", all: false);
        Assert.Single(result);
        Assert.Equal("Pixel 8", result[0].Name);
    }

    private static FlingConfig CreateConfig(params DeviceConfig[] devices) =>
        new() { Devices = [.. devices] };

    private static DeviceConfig Device(string name, string host) =>
        new() { Name = name, Host = host, Port = 7291, ApiKey = "testkey" };
}
