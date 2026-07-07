using Fling.Config;

namespace Fling.Tests;

public sealed class DeviceResolverTests
{
    private static FlingConfig CreateConfig(params DeviceConfig[] devices) =>
        new() { Devices = [..devices] };

    private static DeviceConfig Device(string name) =>
        new() { Name = name, Host = "10.0.0.1", ApiKey = "key" };

    [Fact]
    public void Resolve_NoArgs_NoDevices_Throws()
    {
        var config = CreateConfig();
        var resolver = new DeviceResolver(config);

        var ex = Assert.Throws<DeviceResolutionException>(() =>
            resolver.Resolve(deviceName: null, all: false));
        Assert.Contains("No paired devices", ex.Message);
    }

    [Fact]
    public void Resolve_NoArgs_MultipleDevices_ThrowsWithNames()
    {
        var config = CreateConfig(Device("Phone A"), Device("Phone B"));
        var resolver = new DeviceResolver(config);

        var ex = Assert.Throws<DeviceResolutionException>(() =>
            resolver.Resolve(deviceName: null, all: false));
        Assert.Contains("--device", ex.Message);
        Assert.Contains("--all", ex.Message);
        Assert.Contains("Phone A", ex.Message);
        Assert.Contains("Phone B", ex.Message);
    }

    [Fact]
    public void Resolve_ByName_CaseInsensitive()
    {
        var config = CreateConfig(Device("Pixel 8"), Device("Galaxy S24"));
        var resolver = new DeviceResolver(config);

        var result = resolver.Resolve(deviceName: "pixel 8", all: false);

        Assert.Single(result);
        Assert.Equal("Pixel 8", result[0].Name);
    }

    [Fact]
    public void Resolve_ByName_NotFound_Throws()
    {
        var config = CreateConfig(Device("Pixel 8"));
        var resolver = new DeviceResolver(config);

        var ex = Assert.Throws<DeviceResolutionException>(() =>
            resolver.Resolve(deviceName: "iPhone 15", all: false));
        Assert.Contains("iPhone 15", ex.Message);
    }

    [Fact]
    public void Resolve_All_ReturnsAllDevices()
    {
        var config = CreateConfig(Device("Phone A"), Device("Phone B"), Device("Phone C"));
        var resolver = new DeviceResolver(config);

        var result = resolver.Resolve(deviceName: null, all: true);

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void Resolve_All_NoDevices_Throws()
    {
        var config = CreateConfig();
        var resolver = new DeviceResolver(config);

        var ex = Assert.Throws<DeviceResolutionException>(() =>
            resolver.Resolve(deviceName: null, all: true));
        Assert.Contains("No paired devices", ex.Message);
    }
}
