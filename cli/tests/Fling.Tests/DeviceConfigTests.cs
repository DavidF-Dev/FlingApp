using Fling.Config;

namespace Fling.Tests;

public sealed class DeviceConfigTests
{
    [Fact]
    public void Defaults_PortIs7291()
    {
        var device = new DeviceConfig();

        Assert.Equal(7291, device.Port);
    }
}
