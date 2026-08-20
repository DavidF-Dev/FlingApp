namespace Fling.Net;

/// <summary>
/// Finds Fling devices on the local network.
/// </summary>
public interface IDeviceDiscovery
{
    Task<List<DiscoveredDevice>> DiscoverAsync(CancellationToken ct = default);
}
