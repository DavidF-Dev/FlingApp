using Fling.Config;
using Fling.Content;
using Fling.Net;

namespace Fling.Operations;

/// <summary>
/// Outcome of sending to a single device.
/// </summary>
public sealed record SendDeviceResult(DeviceConfig Device, bool Success, string? Error, bool AuthFailed);

/// <summary>
/// Encodes content and delivers it to paired devices, reporting per-device outcomes.
/// </summary>
public sealed class SendOperation(ConfigStore store, Func<FlingHttpClient>? clientFactory = null)
{
    private readonly Func<FlingHttpClient> _clientFactory = clientFactory ?? (() => new FlingHttpClient());

    public static ClipPayload Encode(FlingConfig config, ResolvedContent content) =>
        new ContentEncoder(config.Compress, config.MaxSizeMb).Encode(content.ContentType, content.Data);

    /// <summary>
    /// Sends to every device in parallel. Never throws for a per-device failure — the
    /// outcome is reported in the returned results so callers can present partial
    /// success. <paramref name="onSending"/> fires before each device is contacted.
    /// </summary>
    public async Task<IReadOnlyList<SendDeviceResult>> SendAsync(
        FlingConfig config,
        IReadOnlyList<DeviceConfig> devices,
        ClipPayload payload,
        Action<DeviceConfig>? onSending = null,
        CancellationToken ct = default)
    {
        var pcName = ResolvePcName(config);

        using var client = _clientFactory();

        var results = await Task.WhenAll(devices.Select(async device =>
        {
            onSending?.Invoke(device);

            var result = await client.SendClipAsync(device.Host, device.Port, device.ApiKey, payload, pcName, ct);
            return (device, result);
        }));

        var renames = new List<(string From, string To)>();
        var outcomes = new List<SendDeviceResult>(results.Length);

        foreach (var (device, result) in results)
        {
            if (result.Success && result.DeviceName is not null && result.DeviceName != device.Name)
            {
                renames.Add((device.Name, result.DeviceName));
                device.Name = result.DeviceName;
            }

            outcomes.Add(new SendDeviceResult(device, result.Success, result.Error, result.AuthFailed));
        }

        ApplyRenames(renames);
        return outcomes;
    }

    public static string ResolvePcName(FlingConfig config) =>
        string.IsNullOrEmpty(config.HostName) ? Environment.MachineName : config.HostName;

    /// <summary>
    /// Persists name changes against a freshly loaded config rather than writing back the
    /// copy this operation started with, so a device paired concurrently is not erased.
    /// </summary>
    private void ApplyRenames(List<(string From, string To)> renames)
    {
        if (renames.Count == 0)
            return;

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
            // A failed name sync must not fail the send that already succeeded.
        }
    }
}
