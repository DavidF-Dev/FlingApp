using System.Net;
using System.Text;
using Fling.Config;
using Fling.Content;
using Fling.Net;
using Fling.Operations;

namespace Fling.Tests;

public sealed class SendOperationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _configPath;
    private readonly ConfigStore _store;

    public SendOperationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"fling-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _configPath = Path.Combine(_tempDir, "config.json");
        _store = new ConfigStore(_configPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private static ClipPayload Payload() =>
        new() { Type = "text/plain", Data = Convert.ToBase64String("hi"u8.ToArray()) };

    private static DeviceConfig Device(string name, string host) =>
        new() { Name = name, Host = host, Port = 7291, ApiKey = "key" };

    [Fact]
    public async Task SendAsync_MixedOutcomes_ReportsEachDeviceSeparately()
    {
        var devices = new List<DeviceConfig>
        {
            Device("Good", "10.0.0.1"),
            Device("Unauthorized", "10.0.0.2"),
            Device("Offline", "10.0.0.3"),
        };

        var config = new FlingConfig { Devices = devices, HostName = "PC" };
        _store.Save(config);

        var operation = new SendOperation(_store, () => new FlingHttpClient(new RoutingHandler
        {
            ["10.0.0.1"] = _ => Json(HttpStatusCode.OK, """{"status":"ok","name":"Good"}"""),
            ["10.0.0.2"] = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized),
            ["10.0.0.3"] = _ => throw new HttpRequestException("No route to host"),
        }));

        var results = await operation.SendAsync(config, devices, Payload());

        var byName = results.ToDictionary(r => r.Device.Name);

        Assert.True(byName["Good"].Success);

        Assert.False(byName["Unauthorized"].Success);
        Assert.True(byName["Unauthorized"].AuthFailed);

        Assert.False(byName["Offline"].Success);
        Assert.False(byName["Offline"].AuthFailed);
        Assert.Contains("No route to host", byName["Offline"].Error);
    }

    [Fact]
    public async Task SendAsync_DeviceReportsNewName_PersistsRename()
    {
        var devices = new List<DeviceConfig> { Device("Old Name", "10.0.0.1") };
        var config = new FlingConfig { Devices = devices };
        _store.Save(config);

        var operation = new SendOperation(_store, () => new FlingHttpClient(new RoutingHandler
        {
            ["10.0.0.1"] = _ => Json(HttpStatusCode.OK, """{"status":"ok","name":"New Name"}"""),
        }));

        await operation.SendAsync(config, devices, Payload());

        Assert.Equal("New Name", _store.Load().Devices.Single().Name);
    }

    [Fact]
    public async Task SendAsync_RenameDoesNotDiscardDevicePairedMeanwhile()
    {
        var devices = new List<DeviceConfig> { Device("Old Name", "10.0.0.1") };
        var config = new FlingConfig { Devices = devices };
        _store.Save(config);

        // Stands in for the tray app pairing a device while this send is in flight.
        _store.Update(c => c.Devices.Add(Device("Paired Meanwhile", "10.0.0.9")));

        var operation = new SendOperation(_store, () => new FlingHttpClient(new RoutingHandler
        {
            ["10.0.0.1"] = _ => Json(HttpStatusCode.OK, """{"status":"ok","name":"New Name"}"""),
        }));

        await operation.SendAsync(config, devices, Payload());

        var saved = _store.Load().Devices.Select(d => d.Name).ToList();
        Assert.Contains("New Name", saved);
        Assert.Contains("Paired Meanwhile", saved);
    }

    [Fact]
    public async Task SendAsync_ReportsProgressBeforeEachDevice()
    {
        var devices = new List<DeviceConfig> { Device("A", "10.0.0.1"), Device("B", "10.0.0.2") };
        var config = new FlingConfig { Devices = devices };
        _store.Save(config);

        var operation = new SendOperation(_store, () => new FlingHttpClient(new RoutingHandler
        {
            ["10.0.0.1"] = _ => Json(HttpStatusCode.OK, """{"status":"ok"}"""),
            ["10.0.0.2"] = _ => Json(HttpStatusCode.OK, """{"status":"ok"}"""),
        }));

        var announced = new List<string>();
        await operation.SendAsync(config, devices, Payload(), d =>
        {
            lock (announced) announced.Add(d.Name);
        });

        Assert.Equal(["A", "B"], announced.Order());
    }

    [Fact]
    public void Encode_HonoursConfiguredSizeLimit()
    {
        var config = new FlingConfig { MaxSizeMb = 1 };
        var content = new ResolvedContent("image/png", new byte[2 * 1024 * 1024]);

        Assert.Throws<ContentTooLargeException>(() => SendOperation.Encode(config, content));
    }

    [Fact]
    public void ResolvePcName_EmptyHostName_FallsBackToMachineName()
    {
        Assert.Equal(Environment.MachineName, SendOperation.ResolvePcName(new FlingConfig()));
        Assert.Equal("Chosen", SendOperation.ResolvePcName(new FlingConfig { HostName = "Chosen" }));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    /// <summary>
    /// Dispatches by request host so a single client can stand in for several devices.
    /// </summary>
    private sealed class RoutingHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> _routes = [];

        public Func<HttpRequestMessage, HttpResponseMessage> this[string host]
        {
            set => _routes[host] = value;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var host = request.RequestUri!.Host;
            if (!_routes.TryGetValue(host, out var route))
                return Task.FromException<HttpResponseMessage>(new HttpRequestException($"Unexpected host {host}"));

            try
            {
                return Task.FromResult(route(request));
            }
            catch (Exception ex)
            {
                return Task.FromException<HttpResponseMessage>(ex);
            }
        }
    }
}
