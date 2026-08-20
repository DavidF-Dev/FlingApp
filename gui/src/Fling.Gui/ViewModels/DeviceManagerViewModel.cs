using System.Collections.ObjectModel;
using Fling.Config;
using Fling.Net;
using Fling.Operations;

namespace Fling.Gui.ViewModels;

public enum PairingState
{
    Idle,
    WaitingForApproval,
    Accepted,
    Rejected,
    TimedOut,
    Failed,
    Cancelled,
}

/// <summary>
/// Drives the device manager: the paired list with its reachability, the devices found
/// on the network, and pairing.
/// </summary>
/// <remarks>
/// Owns no timers. The view decides how often to call the refresh methods and cancels
/// them when it closes, which keeps polling confined to the window's lifetime.
/// </remarks>
public sealed class DeviceManagerViewModel : ObservableObject
{
    private readonly ConfigStore _store;
    private readonly ReachabilityProbe _probe;
    private readonly IDeviceDiscovery _discovery;
    private readonly PairOperation _pairing;
    private readonly DiscoveryTracker _tracker;

    private CancellationTokenSource? _pairingCancellation;
    private PairingState _pairingState = PairingState.Idle;
    private string? _pairingMessage;
    private string? _pairingDeviceName;
    private bool _isDiscovering;

    public DeviceManagerViewModel(
        ConfigStore store,
        ReachabilityProbe probe,
        IDeviceDiscovery discovery,
        PairOperation pairing,
        DiscoveryTracker? tracker = null)
    {
        _store = store;
        _probe = probe;
        _discovery = discovery;
        _pairing = pairing;
        _tracker = tracker ?? new DiscoveryTracker();

        LoadPairedDevices();
    }

    public ObservableCollection<PairedDeviceViewModel> Paired { get; } = [];

    public ObservableCollection<DiscoveredDeviceViewModel> Discovered { get; } = [];

    public bool HasPairedDevices => Paired.Count > 0;

    public bool HasDiscoveredDevices => Discovered.Count > 0;

    /// <summary>
    /// Whether any paired device failed its last probe, which is worth explaining since
    /// the usual cause is on the phone rather than here.
    /// </summary>
    public bool HasUnreachableDevice => Paired.Any(p => p.Reachability == Reachability.Offline);

    public bool IsPairing => PairingState == PairingState.WaitingForApproval;

    public PairingState PairingState
    {
        get => _pairingState;
        private set
        {
            if (!Set(ref _pairingState, value))
                return;

            Raise(nameof(IsPairing));
        }
    }

    public string? PairingMessage
    {
        get => _pairingMessage;
        private set => Set(ref _pairingMessage, value);
    }

    public string? PairingDeviceName
    {
        get => _pairingDeviceName;
        private set => Set(ref _pairingDeviceName, value);
    }

    public bool IsDiscovering
    {
        get => _isDiscovering;
        private set => Set(ref _isDiscovering, value);
    }

    public void LoadPairedDevices()
    {
        Paired.Clear();
        foreach (var device in _store.Load().Devices)
            Paired.Add(new PairedDeviceViewModel(device));

        Raise(nameof(HasPairedDevices));
        PruneDiscovered();
    }

    /// <summary>
    /// Pings every paired device and updates its row.
    /// </summary>
    public async Task RefreshReachabilityAsync(CancellationToken ct = default)
    {
        if (Paired.Count == 0)
            return;

        var config = _store.Load();
        var devices = Paired.Select(p => p.Device).ToList();

        IReadOnlyList<DeviceReachability> results;
        try
        {
            results = await _probe.ProbeAsync(config, devices, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        foreach (var result in results)
        {
            var row = Paired.FirstOrDefault(p => ReferenceEquals(p.Device, result.Device));
            if (row is null)
                continue;

            row.Reachability = result.Online ? Reachability.Online : Reachability.Offline;
            row.Version = result.Version;
            row.LatencyMs = result.LatencyMs;
            row.RefreshIdentity();
        }

        Raise(nameof(HasUnreachableDevice));
    }

    /// <summary>
    /// Runs one discovery broadcast and folds the result into the visible list.
    /// </summary>
    public async Task PollDiscoveryAsync(CancellationToken ct = default)
    {
        IsDiscovering = true;
        try
        {
            _tracker.Observe(await _discovery.DiscoverAsync(ct));
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception)
        {
            // A broadcast can fail on a network that blocks it; the next round retries.
            return;
        }
        finally
        {
            IsDiscovering = false;
        }

        ReconcilePairedAddresses();
        MergeDiscovered();
    }

    /// <summary>
    /// Corrects the stored address of a paired device when discovery reports a different
    /// one, matching the self-healing the CLI already does.
    /// </summary>
    /// <remarks>
    /// Without this a phone that changed IP shows as unreachable with nothing on screen
    /// explaining why, because paired devices are excluded from the discovered list.
    /// </remarks>
    private void ReconcilePairedAddresses()
    {
        var moved = new List<(string Name, string Host, int Port)>();

        foreach (var tracked in _tracker.Current().Where(t => !t.IsStale))
        {
            var row = Paired.FirstOrDefault(p =>
                p.Name.Equals(tracked.Device.Name, StringComparison.OrdinalIgnoreCase));

            if (row is null || (row.Device.Host == tracked.Device.Host && row.Device.Port == tracked.Device.Port))
                continue;

            row.Device.Host = tracked.Device.Host;
            row.Device.Port = tracked.Device.Port;
            row.RefreshIdentity();
            moved.Add((row.Name, tracked.Device.Host, tracked.Device.Port));
        }

        if (moved.Count == 0)
            return;

        try
        {
            _store.Update(config =>
            {
                foreach (var (name, host, port) in moved)
                {
                    var match = config.Devices.Find(d => d.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (match is null)
                        continue;

                    match.Host = host;
                    match.Port = port;
                }
            });
        }
        catch
        {
            // A stale stored address is recoverable on the next round.
        }
    }

    public async Task PairAsync(string host, int port, CancellationToken ct = default)
    {
        if (IsPairing)
            return;

        _pairingCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var config = _store.Load();
        var pcName = SendOperation.ResolvePcName(config);

        PairingDeviceName = $"{host}:{port}";
        PairingMessage = "Waiting for approval on the device…";
        PairingState = PairingState.WaitingForApproval;

        PairOutcome outcome;
        try
        {
            outcome = await _pairing.ExecuteAsync(config, host, port, pcName, force: false, _pairingCancellation.Token);
        }
        catch (Exception ex)
        {
            PairingMessage = ex.Message;
            PairingState = PairingState.Failed;
            return;
        }
        finally
        {
            _pairingCancellation.Dispose();
            _pairingCancellation = null;
        }

        PairingMessage = outcome.Error;
        PairingState = outcome.Status switch
        {
            PairStatus.Accepted => PairingState.Accepted,
            PairStatus.Rejected => PairingState.Rejected,
            PairStatus.TimedOut => PairingState.TimedOut,
            PairStatus.Cancelled => PairingState.Cancelled,
            _ => PairingState.Failed,
        };

        if (outcome.Status != PairStatus.Accepted)
            return;

        PairingDeviceName = outcome.DeviceName;
        PairingMessage = $"Paired with '{outcome.DeviceName}'.";
        LoadPairedDevices();
    }

    public Task PairAsync(DiscoveredDeviceViewModel device, CancellationToken ct = default) =>
        PairAsync(device.Device.Host, device.Device.Port, ct);

    /// <summary>
    /// Parses a typed endpoint and pairs with it, for networks where broadcast is blocked.
    /// </summary>
    public Task PairManualAsync(string endpoint, CancellationToken ct = default)
    {
        string host;
        int port;
        try
        {
            (host, port) = EndpointParser.Parse(endpoint);
        }
        catch (FormatException ex)
        {
            PairingMessage = ex.Message;
            PairingState = PairingState.Failed;
            return Task.CompletedTask;
        }

        return PairAsync(host, port, ct);
    }

    public void CancelPairing() => _pairingCancellation?.Cancel();

    public bool RemoveDevice(PairedDeviceViewModel device)
    {
        var removed = 0;
        _store.Update(config => removed = config.Devices.RemoveAll(d =>
            d.Name.Equals(device.Name, StringComparison.OrdinalIgnoreCase)));

        if (removed == 0)
            return false;

        LoadPairedDevices();
        return true;
    }

    public void ClearPairingState()
    {
        PairingState = PairingState.Idle;
        PairingMessage = null;
        PairingDeviceName = null;
    }

    private void MergeDiscovered()
    {
        var pairedNames = Paired.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var tracked = _tracker.Current().Where(t => !pairedNames.Contains(t.Device.Name)).ToList();

        // Update in place rather than rebuilding, so a row does not disappear from under
        // a click while the list refreshes on its own schedule.
        foreach (var entry in tracked)
        {
            var existing = Discovered.FirstOrDefault(d =>
                d.Name.Equals(entry.Device.Name, StringComparison.OrdinalIgnoreCase));

            if (existing is null)
                Discovered.Add(new DiscoveredDeviceViewModel(entry.Device, entry.IsStale));
            else
                existing.Update(entry.Device, entry.IsStale);
        }

        var live = tracked.Select(t => t.Device.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var gone in Discovered.Where(d => !live.Contains(d.Name)).ToList())
            Discovered.Remove(gone);

        Raise(nameof(HasDiscoveredDevices));
    }

    private void PruneDiscovered()
    {
        var pairedNames = Paired.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var paired in Discovered.Where(d => pairedNames.Contains(d.Name)).ToList())
            Discovered.Remove(paired);

        Raise(nameof(HasDiscoveredDevices));
    }
}
