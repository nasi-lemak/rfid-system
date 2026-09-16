using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Rfid.Protocols;

namespace Rfid.Edge;

public enum PostOutcome { Delivered, Retry, Reject }

/// <summary>What the forwarder needs from the platform; the HTTP implementation is below, tests use a fake.</summary>
public interface IServerClient
{
    Task<(PostOutcome outcome, IngestResult? result, string? error)> PostBatchAsync(ReadBatchRequest batch, CancellationToken ct);
    Task HeartbeatAsync(Guid? deviceId, object metrics, CancellationToken ct);
    /// <summary>Desired reader configuration for this gateway, or null when the server does not know the gateway.</summary>
    Task<EdgeConfig?> GetConfigAsync(CancellationToken ct);
}

/// <summary>Device-token authentication (<c>POST /api/auth/device</c>) with automatic re-login on 401.</summary>
public sealed class HttpServerClient : IServerClient
{
    private readonly HttpClient _http; private readonly EdgeOptions.ServerOptions _opts;
    private string? _jwt; private Guid? _gatewayDeviceId;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public HttpServerClient(HttpClient http, EdgeOptions.ServerOptions opts) { _http = http; _opts = opts; _http.BaseAddress = new Uri(opts.Url.TrimEnd('/') + "/"); _http.Timeout = TimeSpan.FromSeconds(Math.Max(5, opts.TimeoutSeconds)); }

    public Guid? GatewayDeviceId => _gatewayDeviceId;

    private async Task<bool> LoginAsync(CancellationToken ct)
    {
        using var res = await _http.PostAsJsonAsync("api/auth/device", new { token = _opts.DeviceToken }, Json, ct);
        if (!res.IsSuccessStatusCode) return false;
        var body = await res.Content.ReadFromJsonAsync<JsonElement>(Json, ct);
        _jwt = body.GetProperty("token").GetString();
        if (body.TryGetProperty("device", out var d) && d.TryGetProperty("id", out var id) && Guid.TryParse(id.GetString(), out var g)) _gatewayDeviceId = g;
        return _jwt != null;
    }

    private async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> make, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (_jwt == null && !await LoginAsync(ct)) throw new HttpRequestException("device login failed");
            var req = make(); req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _jwt);
            var res = await _http.SendAsync(req, ct);
            if (res.StatusCode != HttpStatusCode.Unauthorized || attempt == 1) return res;
            res.Dispose(); _jwt = null; // token expired or rotated: log in again once
        }
        throw new InvalidOperationException("unreachable");
    }

    public async Task<(PostOutcome, IngestResult?, string?)> PostBatchAsync(ReadBatchRequest batch, CancellationToken ct)
    {
        try
        {
            using var res = await SendAsync(() => new HttpRequestMessage(HttpMethod.Post, "api/ingest/reads") { Content = JsonContent.Create(batch, options: Json) }, ct);
            if (res.IsSuccessStatusCode) return (PostOutcome.Delivered, await res.Content.ReadFromJsonAsync<IngestResult>(Json, ct), null);
            var text = await res.Content.ReadAsStringAsync(ct);
            // 4xx other than auth/throttling means the batch itself is unacceptable; retrying cannot fix it.
            if ((int)res.StatusCode is >= 400 and < 500 and not 401 and not 408 and not 429) return (PostOutcome.Reject, null, $"{(int)res.StatusCode} {text}");
            return (PostOutcome.Retry, null, $"{(int)res.StatusCode} {text}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException) { return (PostOutcome.Retry, null, ex.Message); }
    }

    public async Task<EdgeConfig?> GetConfigAsync(CancellationToken ct)
    {
        using var res = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, "api/edge/config"), ct);
        if (res.StatusCode == HttpStatusCode.NotFound) return null;
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<EdgeConfig>(Json, ct);
    }

    public async Task HeartbeatAsync(Guid? deviceId, object metrics, CancellationToken ct)
    {
        var path = deviceId.HasValue ? $"api/devices/{deviceId}/heartbeat" : "api/devices/heartbeat";
        using var res = await SendAsync(() => new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(metrics, options: Json) }, ct);
    }
}
