using Fling.Net;

namespace Fling.Config;

public sealed class DeviceResolver
{
    private readonly FlingConfig _config;
    private readonly ConfigStore? _store;
    private readonly DiscoveryCache? _cache;
    private readonly UdpDiscovery? _discovery;

    public DeviceResolver(FlingConfig config)
    {
        _config = config;
    }

    public DeviceResolver(FlingConfig config, ConfigStore store, DiscoveryCache cache, UdpDiscovery discovery)
    {
        _config = config;
        _store = store;
        _cache = cache;
        _discovery = discovery;
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

    /// <summary>
    /// Resolves addresses for the given devices using cached or discovered IPs.
    /// Updates config.json when a discovered IP differs from the stored one.
    /// </summary>
    public async Task ResolveAddressesAsync(List<DeviceConfig> devices, CancellationToken ct = default)
    {
        if (_cache is null || _discovery is null)
            return;

        var needDiscovery = new List<DeviceConfig>();
        foreach (var device in devices)
        {
            if (_cache.TryGet(device.Name, out var host, out var port))
            {
                UpdateDevice(device, host, port);
            }
            else
            {
                needDiscovery.Add(device);
            }
        }

        if (needDiscovery.Count == 0)
            return;

        List<DiscoveredDevice> discovered;
        try
        {
            discovered = await _discovery.DiscoverAsync(ct);
        }
        catch (Exception)
        {
            return;
        }

        foreach (var device in needDiscovery)
        {
            var match = discovered.Find(d =>
                d.Name.Equals(device.Name, StringComparison.OrdinalIgnoreCase));

            if (match is not null)
            {
                _cache.Set(device.Name, match.Host, match.Port);
                UpdateDevice(device, match.Host, match.Port);
            }
        }
    }

    private void UpdateDevice(DeviceConfig device, string host, int port)
    {
        if (device.Host == host && device.Port == port)
            return;

        var name = device.Name;
        device.Host = host;
        device.Port = port;

        if (_store is null)
            return;

        // Persist against a freshly loaded config rather than the copy this resolver was
        // handed, so a device paired by another process meanwhile is not erased.
        try
        {
            _store.Update(fresh =>
            {
                var match = fresh.Devices.Find(d => d.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (match is null)
                    return;

                match.Host = host;
                match.Port = port;
            });
        }
        catch
        {
            // A stale stored address is recoverable; failing the command is not.
        }
    }
}

public sealed class DeviceResolutionException : Exception
{
    public DeviceResolutionException(string message) : base(message) { }
}
