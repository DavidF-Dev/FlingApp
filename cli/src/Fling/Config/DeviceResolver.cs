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
        if (_config.Devices.Count == 0)
            throw new DeviceResolutionException("No paired devices. Run 'fling pair <ip:port>' first.");

        if (all)
            return _config.Devices;

        if (deviceName is not null)
        {
            var device = _config.Devices.Find(d =>
                d.Name.Equals(deviceName, StringComparison.OrdinalIgnoreCase));

            if (device is null)
                throw new DeviceResolutionException(
                    $"No paired device named '{deviceName}'. Run 'fling config show' to list devices.");

            return [device];
        }

        var names = string.Join(", ", _config.Devices.Select(d => $"'{d.Name}'"));
        throw new DeviceResolutionException(
            $"Multiple devices paired. Use '--device <name>' or '--all'.\nAvailable: {names}");
    }
}

public sealed class DeviceResolutionException : Exception
{
    public DeviceResolutionException(string message) : base(message) { }
}
