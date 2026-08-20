using Fling.Config;
using Fling.Net;

namespace Fling.Operations;

public enum PairStatus
{
    Accepted,
    Rejected,
    Conflict,
    TimedOut,
    ConnectionFailed,
    Cancelled,
}

public sealed record PairOutcome(PairStatus Status, string? DeviceName = null, string? Error = null);

/// <summary>
/// Pairs with a device: checks for conflicting entries, exchanges a freshly generated
/// API key, and persists the result once the device accepts.
/// </summary>
public sealed class PairOperation(ConfigStore store, Func<FlingHttpClient>? clientFactory = null)
{
    private readonly Func<FlingHttpClient> _clientFactory = clientFactory ?? (() => new FlingHttpClient());

    public async Task<PairOutcome> ExecuteAsync(
        FlingConfig config,
        string host,
        int port,
        string pcName,
        bool force,
        CancellationToken ct = default)
    {
        var existingByAddress = FindByAddress(config, host, port);

        if (existingByAddress is not null && !force)
        {
            return new PairOutcome(PairStatus.Conflict,
                Error: $"A device at {host}:{port} is already paired as '{existingByAddress.Name}'. Use --force to re-pair.");
        }

        var apiKey = ApiKeyGenerator.Generate();

        PairResponse response;
        try
        {
            using var client = _clientFactory();
            response = await client.PairAsync(host, port, pcName, apiKey, ct);
        }
        // The caller abandoning the wait and the device never answering both surface as a
        // cancelled task; only the token says which happened.
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new PairOutcome(PairStatus.Cancelled, Error: "Pairing cancelled.");
        }
        catch (TaskCanceledException)
        {
            return new PairOutcome(PairStatus.TimedOut,
                Error: "Pairing timed out. Check that Fling is running on the device — look for its \"Fling is running\" notification.");
        }
        catch (HttpRequestException ex)
        {
            return new PairOutcome(PairStatus.ConnectionFailed,
                Error: $"Could not connect to {host}:{port}: {ex.Message}");
        }

        if (response.Status != "accepted")
        {
            return new PairOutcome(PairStatus.Rejected, Error: "Pairing was rejected on the device.");
        }

        var existingByName = config.Devices.Find(d =>
            d.Name.Equals(response.Name, StringComparison.OrdinalIgnoreCase));

        if (existingByName is not null && !ReferenceEquals(existingByName, existingByAddress) && !force)
        {
            return new PairOutcome(PairStatus.Conflict,
                Error: $"A device named '{response.Name}' already exists at {existingByName.Host}:{existingByName.Port}. Use --force to re-pair.");
        }

        store.Update(fresh =>
        {
            fresh.Devices.RemoveAll(d =>
                (d.Host.Equals(host, StringComparison.OrdinalIgnoreCase) && d.Port == port) ||
                d.Name.Equals(response.Name, StringComparison.OrdinalIgnoreCase));

            fresh.Devices.Add(new DeviceConfig
            {
                Name = response.Name,
                Host = host,
                Port = port,
                ApiKey = apiKey,
            });
        });

        return new PairOutcome(PairStatus.Accepted, response.Name);
    }

    private static DeviceConfig? FindByAddress(FlingConfig config, string host, int port) =>
        config.Devices.Find(d => d.Host.Equals(host, StringComparison.OrdinalIgnoreCase) && d.Port == port);
}
