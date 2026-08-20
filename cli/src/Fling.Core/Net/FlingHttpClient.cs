using System.Net;
using System.Net.Http.Json;
using Fling.Content;

namespace Fling.Net;

public sealed class FlingHttpClient : IDisposable
{
    private static readonly TimeSpan PairTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PingTimeout = TimeSpan.FromSeconds(3);

    private readonly HttpClient _http;

    public FlingHttpClient(HttpMessageHandler? handler = null)
    {
        _http = handler is not null ? new HttpClient(handler) : new HttpClient();

        // Per-operation timeouts are applied with a linked token instead. HttpClient.Timeout
        // can only be assigned before the first request, so a shared client sending to
        // several devices at once cannot use it.
        _http.Timeout = Timeout.InfiniteTimeSpan;
    }

    public async Task<PairResponse> PairAsync(string host, int port, string pcName, string apiKey, CancellationToken ct = default)
    {
        using var timeout = WithTimeout(PairTimeout, ct);

        var request = new PairRequest { Name = pcName, Key = apiKey };
        var url = $"http://{FormatHost(host)}:{port}/pair";

        var response = await _http.PostAsJsonAsync(url, request, ProtocolJsonContext.Default.PairRequest, timeout.Token);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync(ProtocolJsonContext.Default.PairResponse, timeout.Token)
               ?? throw new InvalidOperationException("Empty response from device.");
    }

    public async Task<SendResult> SendClipAsync(string host, int port, string apiKey, ClipPayload payload, string? pcName = null, CancellationToken ct = default)
    {
        using var timeout = WithTimeout(SendTimeout, ct);

        var url = $"http://{FormatHost(host)}:{port}/clip";
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(payload, ProtocolJsonContext.Default.ClipPayload),
        };
        request.Headers.Add("X-Fling-Key", apiKey);
        if (pcName is not null)
            request.Headers.Add("X-Fling-Name", pcName);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, timeout.Token);
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

        string? deviceName = null;
        try
        {
            var clipResponse = await response.Content.ReadFromJsonAsync(ProtocolJsonContext.Default.ClipResponse, timeout.Token);
            deviceName = clipResponse?.Name;
        }
        catch { }

        return new SendResult { Success = true, DeviceName = deviceName };
    }

    public async Task<PingResponse> PingAsync(string host, int port, string apiKey, string? pcName = null, CancellationToken ct = default)
    {
        using var timeout = WithTimeout(PingTimeout, ct);

        var url = $"http://{FormatHost(host)}:{port}/ping";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Fling-Key", apiKey);
        if (pcName is not null)
            request.Headers.Add("X-Fling-Name", pcName);

        var response = await _http.SendAsync(request, timeout.Token);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync(ProtocolJsonContext.Default.PingResponse, timeout.Token)
               ?? throw new InvalidOperationException("Empty response from device.");
    }

    private static CancellationTokenSource WithTimeout(TimeSpan timeout, CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        return cts;
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

public sealed class ClipResponse
{
    public string Status { get; set; } = "";
    public string? Name { get; set; }
}

public sealed class SendResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public bool AuthFailed { get; init; }
    public string? DeviceName { get; init; }
}
