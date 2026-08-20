using System.Diagnostics;
using Fling.Config;
using Fling.Net;

namespace Fling.Operations;

public sealed record DeviceReachability(
    DeviceConfig Device,
    bool Online,
    string? ReportedName,
    string? Version,
    long? LatencyMs,
    string? Error);

/// <summary>
/// Pings paired devices to determine whether they are reachable.
/// </summary>
public sealed class ReachabilityProbe(ConfigStore store, Func<FlingHttpClient>? clientFactory = null)
{
    private readonly Func<FlingHttpClient> _clientFactory = clientFactory ?? (() => new FlingHttpClient());

    public async Task<IReadOnlyList<DeviceReachability>> ProbeAsync(
        FlingConfig config,
        IReadOnlyList<DeviceConfig> devices,
        CancellationToken ct = default)
    {
        var pcName = SendOperation.ResolvePcName(config);

        using var client = _clientFactory();

        var results = await Task.WhenAll(devices.Select(async device =>
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var response = await client.PingAsync(device.Host, device.Port, device.ApiKey, pcName, ct);
                stopwatch.Stop();
                return new DeviceReachability(device, true, response.Name, response.Version, stopwatch.ElapsedMilliseconds, null);
            }
            catch (TaskCanceledException)
            {
                return new DeviceReachability(device, false, null, null, null, "timeout");
            }
            catch (HttpRequestException ex)
            {
                return new DeviceReachability(device, false, null, null, null, ex.Message);
            }
        }));

        ApplyRenames(results);
        return results;
    }

    private void ApplyRenames(DeviceReachability[] results)
    {
        var renames = results
            .Where(r => r.Online && r.ReportedName is not null && r.ReportedName != r.Device.Name)
            .Select(r => (From: r.Device.Name, To: r.ReportedName!))
            .ToList();

        if (renames.Count == 0)
            return;

        foreach (var (from, to) in renames)
        {
            var device = results.First(r => r.Device.Name == from).Device;
            device.Name = to;
        }

        try
        {
            store.Update(fresh =>
            {
                foreach (var (from, to) in renames)
                {
                    var match = fresh.Devices.Find(d => d.Name.Equals(from, StringComparison.OrdinalIgnoreCase));
                    if (match is not null)
                        match.Name = to;
                }
            });
        }
        catch
        {
            // A failed name sync must not fail the status report.
        }
    }
}
