namespace Fling.Config;

public sealed class DeviceResolver
{
    private readonly FlingConfig _config;

    public DeviceResolver(FlingConfig config)
    {
        _config = config;
    }

    public List<DeviceConfig> Resolve(string? deviceName, bool all)
    {
        if (all)
        {
            if (_config.Devices.Count == 0)
                throw new DeviceResolutionException("No paired devices. Run 'fling pair <ip:port>' first.");

            return _config.Devices;
        }

        if (deviceName is not null)
        {
            var device = _config.Devices.Find(d =>
                d.Name.Equals(deviceName, StringComparison.OrdinalIgnoreCase));

            if (device is null)
                throw new DeviceResolutionException(
                    $"No paired device named '{deviceName}'. Run 'fling config show' to list devices.");

            return [device];
        }

        var defaultDevice = _config.Devices.Find(d => d.Default);

        if (defaultDevice is null)
        {
            if (_config.Devices.Count == 0)
                throw new DeviceResolutionException("No paired devices. Run 'fling pair <ip:port>' first.");

            throw new DeviceResolutionException(
                "No default device set. Use '--device <name>' or run 'fling config default <name>'.");
        }

        return [defaultDevice];
    }
}

public sealed class DeviceResolutionException : Exception
{
    public DeviceResolutionException(string message) : base(message) { }
}
