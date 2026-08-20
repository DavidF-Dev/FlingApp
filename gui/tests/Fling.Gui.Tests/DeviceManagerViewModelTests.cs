using System.Net;
using System.Net.Http;
using System.Text;
using Fling.Config;
using Fling.Gui.ViewModels;
using Fling.Net;
using Fling.Operations;

namespace Fling.Gui.Tests;

public sealed class DeviceManagerViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ConfigStore _store;

    public DeviceManagerViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"fling-gui-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _store = new ConfigStore(Path.Combine(_tempDir, "config.json"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private DeviceManagerViewModel Build(
        FakeDiscovery? discovery = null,
        HttpMessageHandler? pairHandler = null,
        HttpMessageHandler? probeHandler = null)
    {
        return new DeviceManagerViewModel(
            _store,
            new ReachabilityProbe(_store, () => new FlingHttpClient(probeHandler ?? new UnreachableHandler())),
            discovery ?? new FakeDiscovery(),
            new PairOperation(_store, () => new FlingHttpClient(pairHandler ?? new UnreachableHandler())));
    }

    private void SaveDevice(string name, string host = "10.0.0.1") =>
        _store.Update(c => c.Devices.Add(new DeviceConfig { Name = name, Host = host, ApiKey = "key" }));

    // --- Paired list -----------------------------------------------------------------

    [Fact]
    public void New_NoDevices_ReportsEmptyState()
    {
        var model = Build();

        Assert.Empty(model.Paired);
        Assert.False(model.HasPairedDevices);
    }

    [Fact]
    public void LoadPairedDevices_ReflectsConfig()
    {
        SaveDevice("Pixel");
        var model = Build();

        Assert.Equal("Pixel", model.Paired.Single().Name);
        Assert.True(model.HasPairedDevices);
    }

    [Fact]
    public void RemoveDevice_DeletesFromConfigAndList()
    {
        SaveDevice("Pixel");
        var model = Build();

        var removed = model.RemoveDevice(model.Paired.Single());

        Assert.True(removed);
        Assert.Empty(model.Paired);
        Assert.Empty(_store.Load().Devices);
    }

    // --- Discovery -------------------------------------------------------------------

    [Fact]
    public async Task PollDiscovery_ExcludesAlreadyPairedDevices()
    {
        SaveDevice("Pixel");
        var discovery = new FakeDiscovery([new DiscoveredDevice("Pixel", "10.0.0.1", 7291),
                                           new DiscoveredDevice("Tablet", "10.0.0.2", 7291)]);
        var model = Build(discovery);

        await model.PollDiscoveryAsync();

        Assert.Equal("Tablet", model.Discovered.Single().Name);
    }

    [Fact]
    public async Task PollDiscovery_RepeatedRounds_DoNotDuplicateRows()
    {
        var discovery = new FakeDiscovery([new DiscoveredDevice("Tablet", "10.0.0.2", 7291)]);
        var model = Build(discovery);

        await model.PollDiscoveryAsync();
        await model.PollDiscoveryAsync();
        await model.PollDiscoveryAsync();

        Assert.Single(model.Discovered);
    }

    [Fact]
    public async Task PollDiscovery_RowIdentityIsStableAcrossRounds()
    {
        var discovery = new FakeDiscovery([new DiscoveredDevice("Tablet", "10.0.0.2", 7291)]);
        var model = Build(discovery);

        await model.PollDiscoveryAsync();
        var first = model.Discovered.Single();

        await model.PollDiscoveryAsync();

        Assert.Same(first, model.Discovered.Single());
    }

    [Fact]
    public async Task PollDiscovery_BroadcastThrows_LeavesListIntact()
    {
        var discovery = new FakeDiscovery([new DiscoveredDevice("Tablet", "10.0.0.2", 7291)]);
        var model = Build(discovery);
        await model.PollDiscoveryAsync();

        discovery.ThrowNext = true;
        await model.PollDiscoveryAsync();

        Assert.Single(model.Discovered);
        Assert.False(model.IsDiscovering);
    }

    [Fact]
    public async Task PollDiscovery_PairedDeviceMoved_UpdatesStoredAddress()
    {
        SaveDevice("Pixel", "10.0.0.1");
        var discovery = new FakeDiscovery([new DiscoveredDevice("Pixel", "10.0.0.99", 7291)]);
        var model = Build(discovery);

        await model.PollDiscoveryAsync();

        Assert.Equal("10.0.0.99:7291", model.Paired.Single().Address);
        Assert.Equal("10.0.0.99", _store.Load().Devices.Single().Host);
    }

    [Fact]
    public async Task PollDiscovery_PairedDeviceUnmoved_LeavesConfigAlone()
    {
        SaveDevice("Pixel", "10.0.0.1");
        var discovery = new FakeDiscovery([new DiscoveredDevice("Pixel", "10.0.0.1", 7291)]);
        var model = Build(discovery);

        await model.PollDiscoveryAsync();

        Assert.Equal("10.0.0.1", _store.Load().Devices.Single().Host);
        Assert.Empty(model.Discovered);
    }

    // --- Pairing state machine -------------------------------------------------------

    [Fact]
    public async Task Pair_Accepted_TransitionsAndPersists()
    {
        var model = Build(pairHandler: Json("""{"status":"accepted","name":"Pixel 8"}"""));

        await model.PairAsync("10.0.0.5", 7291);

        Assert.Equal(PairingState.Accepted, model.PairingState);
        Assert.Equal("Pixel 8", _store.Load().Devices.Single().Name);
        Assert.Equal("Pixel 8", model.Paired.Single().Name);
    }

    [Fact]
    public async Task Pair_Rejected_TransitionsAndStoresNothing()
    {
        var model = Build(pairHandler: Json("""{"status":"rejected"}"""));

        await model.PairAsync("10.0.0.5", 7291);

        Assert.Equal(PairingState.Rejected, model.PairingState);
        Assert.Empty(_store.Load().Devices);
    }

    [Fact]
    public async Task Pair_DeviceNeverAnswers_TransitionsToTimedOut()
    {
        var model = Build(pairHandler: new ThrowingHandler(new TaskCanceledException()));

        await model.PairAsync("10.0.0.5", 7291);

        Assert.Equal(PairingState.TimedOut, model.PairingState);
        Assert.Empty(_store.Load().Devices);
    }

    [Fact]
    public async Task Pair_UserCancels_TransitionsToCancelledNotTimedOut()
    {
        using var cancellation = new CancellationTokenSource();
        var model = Build(pairHandler: new BlockingHandler(cancellation));

        await model.PairAsync("10.0.0.5", 7291, cancellation.Token);

        Assert.Equal(PairingState.Cancelled, model.PairingState);
        Assert.Empty(_store.Load().Devices);
    }

    [Fact]
    public async Task Pair_ConnectionFails_TransitionsToFailed()
    {
        var model = Build(pairHandler: new ThrowingHandler(new HttpRequestException("No route to host")));

        await model.PairAsync("10.0.0.5", 7291);

        Assert.Equal(PairingState.Failed, model.PairingState);
        Assert.Contains("No route to host", model.PairingMessage);
    }

    [Fact]
    public async Task PairManual_InvalidEndpoint_FailsWithoutContactingAnything()
    {
        var model = Build(pairHandler: new ThrowingHandler(new InvalidOperationException("should not be called")));

        await model.PairManualAsync("not a real endpoint");

        Assert.Equal(PairingState.Failed, model.PairingState);
        Assert.NotNull(model.PairingMessage);
    }

    [Fact]
    public async Task PairManual_BareAddress_UsesDefaultPort()
    {
        var handler = Json("""{"status":"accepted","name":"Pixel 8"}""");
        var model = Build(pairHandler: handler);

        await model.PairManualAsync("192.168.1.50");

        Assert.Equal(PairingState.Accepted, model.PairingState);
        Assert.Equal(7291, _store.Load().Devices.Single().Port);
    }

    [Fact]
    public async Task ClearPairingState_ReturnsToIdle()
    {
        var model = Build(pairHandler: Json("""{"status":"rejected"}"""));
        await model.PairAsync("10.0.0.5", 7291);

        model.ClearPairingState();

        Assert.Equal(PairingState.Idle, model.PairingState);
        Assert.Null(model.PairingMessage);
    }

    [Fact]
    public async Task Pair_AcceptedDeviceLeavesTheDiscoveredList()
    {
        var discovery = new FakeDiscovery([new DiscoveredDevice("Pixel 8", "10.0.0.5", 7291)]);
        var model = Build(discovery, pairHandler: Json("""{"status":"accepted","name":"Pixel 8"}"""));
        await model.PollDiscoveryAsync();
        Assert.Single(model.Discovered);

        await model.PairAsync("10.0.0.5", 7291);

        Assert.Empty(model.Discovered);
        Assert.Single(model.Paired);
    }

    // --- Reachability ----------------------------------------------------------------

    [Fact]
    public async Task RefreshReachability_OnlineDevice_ShowsOnlineWithLatency()
    {
        SaveDevice("Pixel");
        var model = Build(probeHandler: Json("""{"status":"ok","name":"Pixel","version":"1.0.1"}"""));

        await model.RefreshReachabilityAsync();

        var row = model.Paired.Single();
        Assert.Equal(Reachability.Online, row.Reachability);
        Assert.Equal("1.0.1", row.Version);
        Assert.Contains("Online", row.StatusText);
    }

    [Fact]
    public async Task RefreshReachability_UnreachableDevice_ShowsOffline()
    {
        SaveDevice("Pixel");
        var model = Build();

        await model.RefreshReachabilityAsync();

        Assert.Equal(Reachability.Offline, model.Paired.Single().Reachability);
        Assert.Equal("Not reachable", model.Paired.Single().StatusText);
    }

    [Fact]
    public async Task RefreshReachability_UnreachableDevice_FlagsTheExplanation()
    {
        SaveDevice("Pixel");
        var model = Build();
        Assert.False(model.HasUnreachableDevice);

        await model.RefreshReachabilityAsync();

        Assert.True(model.HasUnreachableDevice);
    }

    [Fact]
    public async Task RefreshReachability_AllOnline_DoesNotFlagTheExplanation()
    {
        SaveDevice("Pixel");
        var model = Build(probeHandler: Json("""{"status":"ok","name":"Pixel","version":"1.0.1"}"""));

        await model.RefreshReachabilityAsync();

        Assert.False(model.HasUnreachableDevice);
    }

    [Fact]
    public async Task RefreshReachability_NoDevices_DoesNothing()
    {
        var model = Build();

        await model.RefreshReachabilityAsync();

        Assert.Empty(model.Paired);
    }

    // --- Fakes -----------------------------------------------------------------------

    private static FakeHandler Json(string body) => new(body);

    private sealed class FakeDiscovery(List<DiscoveredDevice>? devices = null) : IDeviceDiscovery
    {
        private readonly List<DiscoveredDevice> _devices = devices ?? [];

        public bool ThrowNext { get; set; }

        public Task<List<DiscoveredDevice>> DiscoverAsync(CancellationToken ct = default)
        {
            if (ThrowNext)
                return Task.FromException<List<DiscoveredDevice>>(new InvalidOperationException("broadcast failed"));

            return Task.FromResult(_devices.ToList());
        }
    }

    private sealed class FakeHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromException<HttpResponseMessage>(exception);
    }

    private sealed class UnreachableHandler() : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("unreachable"));
    }

    /// <summary>
    /// Stands in for a device that never answers, cancelling the caller's token the way
    /// closing the window or pressing Cancel would.
    /// </summary>
    private sealed class BlockingHandler(CancellationTokenSource cancellation) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            await cancellation.CancelAsync();
            ct.ThrowIfCancellationRequested();
            throw new InvalidOperationException("should have been cancelled");
        }
    }
}
