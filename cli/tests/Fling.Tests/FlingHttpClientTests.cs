using System.Net;
using System.Text;
using System.Text.Json;
using Fling.Net;

namespace Fling.Tests;

public sealed class FlingHttpClientTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static FakeHandler Respond(object body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(new HttpResponseMessage(status)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json"),
        });

    [Fact]
    public async Task PairAsync_Accepted_ReturnsResponse()
    {
        using var handler = Respond(new { status = "accepted", name = "Pixel 8" });
        using var client = new FlingHttpClient(handler);

        var response = await client.PairAsync("10.0.0.1", 7291, "My PC", "test-key");

        Assert.Equal("accepted", response.Status);
        Assert.Equal("Pixel 8", response.Name);
    }

    [Fact]
    public async Task PairAsync_SendsCorrectBody()
    {
        using var handler = Respond(new { status = "accepted", name = "Phone" });
        using var client = new FlingHttpClient(handler);

        await client.PairAsync("10.0.0.1", 7291, "My PC", "the-key");

        var body = await handler.CapturedRequest!.Content!.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        Assert.Equal("My PC", doc.RootElement.GetProperty("name").GetString());
        Assert.Equal("the-key", doc.RootElement.GetProperty("key").GetString());
    }

    [Fact]
    public async Task PairAsync_SendsToCorrectUrl()
    {
        using var handler = Respond(new { status = "accepted", name = "Phone" });
        using var client = new FlingHttpClient(handler);

        await client.PairAsync("192.168.1.50", 7291, "PC", "key");

        Assert.Equal("http://192.168.1.50:7291/pair", handler.CapturedRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task PairAsync_Rejected_ReturnsResponse()
    {
        using var handler = Respond(new { status = "rejected", name = "" });
        using var client = new FlingHttpClient(handler);

        var response = await client.PairAsync("10.0.0.1", 7291, "PC", "key");

        Assert.Equal("rejected", response.Status);
    }

    [Fact]
    public async Task PairAsync_ConnectionRefused_ThrowsHttpRequestException()
    {
        using var handler = new FakeHandler(new HttpRequestException("Connection refused"));
        using var client = new FlingHttpClient(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.PairAsync("10.0.0.1", 7291, "PC", "key"));
    }

    [Fact]
    public async Task PingAsync_SetsApiKeyHeader()
    {
        using var handler = Respond(new { status = "ok", name = "Phone", version = "1.0.0" });
        using var client = new FlingHttpClient(handler);

        await client.PingAsync("10.0.0.1", 7291, "my-api-key");

        Assert.Equal("my-api-key", handler.CapturedRequest!.Headers.GetValues("X-Fling-Key").Single());
    }

    [Fact]
    public async Task PingAsync_ReturnsResponse()
    {
        using var handler = Respond(new { status = "ok", name = "Pixel 8", version = "1.0.0" });
        using var client = new FlingHttpClient(handler);

        var response = await client.PingAsync("10.0.0.1", 7291, "key");

        Assert.Equal("ok", response.Status);
        Assert.Equal("Pixel 8", response.Name);
        Assert.Equal("1.0.0", response.Version);
    }

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
