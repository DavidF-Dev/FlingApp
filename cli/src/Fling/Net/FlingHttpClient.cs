using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fling.Net;

public sealed class FlingHttpClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly HttpClient _http;

    public FlingHttpClient(HttpMessageHandler? handler = null)
    {
        _http = handler is not null ? new HttpClient(handler) : new HttpClient();
    }

    public async Task<PairResponse> PairAsync(string host, int port, string pcName, string apiKey, CancellationToken ct = default)
    {
        _http.Timeout = TimeSpan.FromSeconds(60);

        var request = new PairRequest { Name = pcName, Key = apiKey };
        var url = $"http://{FormatHost(host)}:{port}/pair";

        var response = await _http.PostAsJsonAsync(url, request, JsonOptions, ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<PairResponse>(JsonOptions, ct)
               ?? throw new InvalidOperationException("Empty response from device.");
    }

    public async Task<PingResponse> PingAsync(string host, int port, string apiKey, CancellationToken ct = default)
    {
        _http.Timeout = TimeSpan.FromSeconds(3);

        var url = $"http://{FormatHost(host)}:{port}/ping";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Fling-Key", apiKey);

        var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<PingResponse>(JsonOptions, ct)
               ?? throw new InvalidOperationException("Empty response from device.");
    }

    private static string FormatHost(string host) =>
        host.Contains(':') ? $"[{host}]" : host;

    public void Dispose() => _http.Dispose();
}

public sealed class PairRequest
{
    public string Name { get; set; } = "";
    public string Key { get; set; } = "";
}

public sealed class PairResponse
{
    public string Status { get; set; } = "";
    public string Name { get; set; } = "";
}

public sealed class PingResponse
{
    public string Status { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
}
