using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fling.Content;

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

    public async Task<SendResult> SendClipAsync(string host, int port, string apiKey, ClipPayload payload, CancellationToken ct = default)
    {
        _http.Timeout = TimeSpan.FromSeconds(10);

        var url = $"http://{FormatHost(host)}:{port}/clip";
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(payload, options: JsonOptions),
        };
        request.Headers.Add("X-Fling-Key", apiKey);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (TaskCanceledException)
        {
            return new SendResult { Success = false, Error = "Connection timed out." };
        }
        catch (HttpRequestException ex)
        {
            return new SendResult { Success = false, Error = ex.Message };
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized)
            return new SendResult { Success = false, Error = "Authentication failed. Try re-pairing with 'fling pair --force'.", AuthFailed = true };

        if (response.StatusCode == HttpStatusCode.RequestEntityTooLarge)
            return new SendResult { Success = false, Error = "Payload too large — device rejected it." };

        if (response.StatusCode == (HttpStatusCode)429)
            return new SendResult { Success = false, Error = "Rate limited — device is rejecting requests. Try again shortly." };

        if (!response.IsSuccessStatusCode)
            return new SendResult { Success = false, Error = $"Device returned HTTP {(int)response.StatusCode}." };

        return new SendResult { Success = true };
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

public sealed class SendResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public bool AuthFailed { get; init; }
}
