using System.Net;
using System.Text;
using System.Text.Json;
using Fling.Content;
using Fling.Net;

namespace Fling.Tests;

public sealed class NameSyncTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public async Task SendClipAsync_IncludesXFlingNameHeader()
    {
        using var handler = RespondWith(new { status = "ok", name = "Phone" });
        using var client = new FlingHttpClient(handler);
        var payload = new ClipPayload { Type = "text/plain", Data = "x", Timestamp = 1 };

        await client.SendClipAsync("10.0.0.1", 7291, "key", payload, pcName: "My PC");

        Assert.Equal("My PC", handler.CapturedRequest!.Headers.GetValues("X-Fling-Name").Single());
    }

    [Fact]
    public async Task SendClipAsync_NoPcName_NoHeader()
    {
        using var handler = RespondWith(new { status = "ok" });
        using var client = new FlingHttpClient(handler);
        var payload = new ClipPayload { Type = "text/plain", Data = "x", Timestamp = 1 };

        await client.SendClipAsync("10.0.0.1", 7291, "key", payload);

        Assert.False(handler.CapturedRequest!.Headers.Contains("X-Fling-Name"));
    }

    [Fact]
    public async Task SendClipAsync_ParsesDeviceNameFromResponse()
    {
        using var handler = RespondWith(new { status = "ok", name = "Grand Robin" });
        using var client = new FlingHttpClient(handler);
        var payload = new ClipPayload { Type = "text/plain", Data = "x", Timestamp = 1 };

        var result = await client.SendClipAsync("10.0.0.1", 7291, "key", payload);

        Assert.True(result.Success);
        Assert.Equal("Grand Robin", result.DeviceName);
    }

    [Fact]
    public async Task SendClipAsync_ResponseWithoutName_DeviceNameIsNull()
    {
        using var handler = RespondWith(new { status = "ok" });
        using var client = new FlingHttpClient(handler);
        var payload = new ClipPayload { Type = "text/plain", Data = "x", Timestamp = 1 };

        var result = await client.SendClipAsync("10.0.0.1", 7291, "key", payload);

        Assert.True(result.Success);
        Assert.Null(result.DeviceName);
    }

    [Fact]
    public async Task PingAsync_IncludesXFlingNameHeader()
    {
        using var handler = RespondWith(new { status = "ok", name = "Phone", version = "1.0.0" });
        using var client = new FlingHttpClient(handler);

        await client.PingAsync("10.0.0.1", 7291, "key", pcName: "My PC");

        Assert.Equal("My PC", handler.CapturedRequest!.Headers.GetValues("X-Fling-Name").Single());
    }

    [Fact]
    public async Task PingAsync_NoPcName_NoHeader()
    {
        using var handler = RespondWith(new { status = "ok", name = "Phone", version = "1.0.0" });
        using var client = new FlingHttpClient(handler);

        await client.PingAsync("10.0.0.1", 7291, "key");

        Assert.False(handler.CapturedRequest!.Headers.Contains("X-Fling-Name"));
    }

    private static FakeHandler RespondWith(object body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(new HttpResponseMessage(status)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json"),
        });

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage? _response;

        public HttpRequestMessage? CapturedRequest { get; private set; }

        public FakeHandler(HttpResponseMessage response) => _response = response;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            CapturedRequest = request;
            return Task.FromResult(_response!);
        }
    }
}
