using System.Net;
using System.Text;
using System.Text.Json;
using Fling.Content;
using Fling.Net;

namespace Fling.Tests;

public sealed class SendClipTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public async Task SendClipAsync_Success_ReturnsOk()
    {
        using var handler = RespondWith(new { status = "ok" });
        using var client = new FlingHttpClient(handler);
        var payload = new ClipPayload
        {
            Type = "text/plain",
            Data = Convert.ToBase64String("hello"u8.ToArray()),
            Compressed = false,
            Timestamp = 1000,
        };

        var result = await client.SendClipAsync("10.0.0.1", 7291, "key", payload);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task SendClipAsync_SetsApiKeyHeader()
    {
        using var handler = RespondWith(new { status = "ok" });
        using var client = new FlingHttpClient(handler);
        var payload = new ClipPayload { Type = "text/plain", Data = "x", Timestamp = 1 };

        await client.SendClipAsync("10.0.0.1", 7291, "my-key", payload);

        Assert.Equal("my-key", handler.CapturedRequest!.Headers.GetValues("X-Fling-Key").Single());
    }

    [Fact]
    public async Task SendClipAsync_401_ReturnsAuthFailed()
    {
        using var handler = RespondWith(new { }, HttpStatusCode.Unauthorized);
        using var client = new FlingHttpClient(handler);
        var payload = new ClipPayload { Type = "text/plain", Data = "x", Timestamp = 1 };

        var result = await client.SendClipAsync("10.0.0.1", 7291, "bad-key", payload);

        Assert.False(result.Success);
        Assert.True(result.AuthFailed);
        Assert.Contains("re-pairing", result.Error);
    }

    [Fact]
    public async Task SendClipAsync_413_ReturnsTooLarge()
    {
        using var handler = RespondWith(new { }, HttpStatusCode.RequestEntityTooLarge);
        using var client = new FlingHttpClient(handler);
        var payload = new ClipPayload { Type = "text/plain", Data = "x", Timestamp = 1 };

        var result = await client.SendClipAsync("10.0.0.1", 7291, "key", payload);

        Assert.False(result.Success);
        Assert.Contains("too large", result.Error);
    }

    [Fact]
    public async Task SendClipAsync_429_ReturnsRateLimited()
    {
        using var handler = RespondWith(new { }, (HttpStatusCode)429);
        using var client = new FlingHttpClient(handler);
        var payload = new ClipPayload { Type = "text/plain", Data = "x", Timestamp = 1 };

        var result = await client.SendClipAsync("10.0.0.1", 7291, "key", payload);

        Assert.False(result.Success);
        Assert.Contains("Rate limited", result.Error);
    }

    [Fact]
    public async Task SendClipAsync_ConnectionError_ReturnsFailure()
    {
        using var handler = new FakeHandler(new HttpRequestException("Connection refused"));
        using var client = new FlingHttpClient(handler);
        var payload = new ClipPayload { Type = "text/plain", Data = "x", Timestamp = 1 };

        var result = await client.SendClipAsync("10.0.0.1", 7291, "key", payload);

        Assert.False(result.Success);
        Assert.Contains("Connection refused", result.Error);
    }

    [Fact]
    public async Task SendClipAsync_PostsToCorrectUrl()
    {
        using var handler = RespondWith(new { status = "ok" });
        using var client = new FlingHttpClient(handler);
        var payload = new ClipPayload { Type = "text/plain", Data = "x", Timestamp = 1 };

        await client.SendClipAsync("192.168.1.50", 7291, "key", payload);

        Assert.Equal("http://192.168.1.50:7291/clip", handler.CapturedRequest!.RequestUri!.ToString());
    }

    private static FakeHandler RespondWith(object body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(new HttpResponseMessage(status)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json"),
        });

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage? _response;
        private readonly Exception? _exception;

        public HttpRequestMessage? CapturedRequest { get; private set; }

        public FakeHandler(HttpResponseMessage response) => _response = response;
        public FakeHandler(Exception exception) => _exception = exception;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            CapturedRequest = request;
            if (_exception is not null)
                return Task.FromException<HttpResponseMessage>(_exception);
            return Task.FromResult(_response!);
        }
    }
}
