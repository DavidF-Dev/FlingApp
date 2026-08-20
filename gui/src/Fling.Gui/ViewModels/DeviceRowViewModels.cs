using Fling.Config;
using Fling.Net;

namespace Fling.Gui.ViewModels;

public enum Reachability
{
    Unknown,
    Online,
    Offline,
}

/// <summary>
/// A device already paired with this PC.
/// </summary>
public sealed class PairedDeviceViewModel(DeviceConfig device) : ObservableObject
{
    private Reachability _reachability = Reachability.Unknown;
    private string? _version;
    private long? _latencyMs;

    public DeviceConfig Device { get; } = device;

    public string Name => Device.Name;

    public string Address => $"{Device.Host}:{Device.Port}";

    public Reachability Reachability
    {
        get => _reachability;
        set { if (Set(ref _reachability, value)) Raise(nameof(StatusText)); }
    }

    public string? Version
    {
        get => _version;
        set => Set(ref _version, value);
    }

    public long? LatencyMs
    {
        get => _latencyMs;
        set { if (Set(ref _latencyMs, value)) Raise(nameof(StatusText)); }
    }

    public string StatusText => Reachability switch
    {
        Reachability.Online => LatencyMs is { } ms ? $"Online · {ms} ms" : "Online",
        Reachability.Offline => "Not reachable",
        _ => "Checking…",
    };

    /// <summary>
    /// Refreshes the name and address shown after a probe, which may have picked up a
    /// rename from the device or a new address from discovery.
    /// </summary>
    public void RefreshIdentity()
    {
        Raise(nameof(Name));
        Raise(nameof(Address));
    }
}

/// <summary>
/// A device answering discovery broadcasts that is not yet paired.
/// </summary>
public sealed class DiscoveredDeviceViewModel(DiscoveredDevice device, bool isStale) : ObservableObject
{
    private bool _isStale = isStale;

    public DiscoveredDevice Device { get; private set; } = device;

    public string Name => Device.Name;

    public string Address => $"{Device.Host}:{Device.Port}";

    public bool IsStale
    {
        get => _isStale;
        private set { if (Set(ref _isStale, value)) Raise(nameof(StatusText)); }
    }

    public string StatusText => IsStale ? "Not responding" : "Ready to pair";

    public void Update(DiscoveredDevice device, bool isStale)
    {
        if (Device != device)
        {
            Device = device;
            Raise(nameof(Address));
        }

        IsStale = isStale;
    }
}
