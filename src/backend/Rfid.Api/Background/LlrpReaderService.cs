using Microsoft.EntityFrameworkCore;
using Rfid.Api.Auth;
using Rfid.Application.Contracts;
using Rfid.Application.Llrp;
using Rfid.Application.Services;
using Rfid.Domain;
using Rfid.Domain.Entities;
using Rfid.Infrastructure.Persistence;

namespace Rfid.Api.Background;

/// <summary>
/// Keeps an LLRP connection to every device whose Config has "llrpHost" (optional "llrpPort", default 5084;
/// "llrpEnabled": false to pause) and pushes tag reports into the ingestion pipeline. Connections are
/// re-established automatically; the device list is refreshed every minute.
/// </summary>
public class LlrpReaderService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<LlrpReaderService> _log;
    private readonly IConfiguration _cfg;
    private readonly Dictionary<Guid, (LlrpClient client, string host)> _clients = new();
    public LlrpReaderService(IServiceScopeFactory scopes, ILogger<LlrpReaderService> log, IConfiguration cfg) { _scopes = scopes; _log = log; _cfg = cfg; }

    public static (string host, int port)? Endpoint(Device d)
    {
        if (!d.Config.TryGetValue("llrpHost", out var h) || string.IsNullOrWhiteSpace(h?.ToString())) return null;
        if (d.Config.TryGetValue("llrpEnabled", out var en) && en?.ToString()?.Equals("false", StringComparison.OrdinalIgnoreCase) == true) return null;
        var port = d.Config.TryGetValue("llrpPort", out var p) && int.TryParse(p?.ToString(), out var pi) ? pi : 5084;
        return (h!.ToString()!, port);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!_cfg.GetValue("Llrp:Enabled", true)) return;
        await Task.Delay(TimeSpan.FromSeconds(8), ct);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                List<Device> devices;
                using (var s = _scopes.CreateScope()) devices = await s.ServiceProvider.GetRequiredService<AppDbContext>().Devices.IgnoreQueryFilters().Where(d => d.Kind != DeviceKind.Handheld && d.Kind != DeviceKind.Printer).ToListAsync(ct);
                var wanted = devices.Select(d => (d, ep: Endpoint(d))).Where(x => x.ep != null).ToList();
                foreach (var (d, ep) in wanted)
                {
                    var key = $"{ep!.Value.host}:{ep.Value.port}";
                    if (_clients.TryGetValue(d.Id, out var existing) && existing.client.Connected && existing.host == key) continue;
                    if (existing.client != null) await existing.client.StopAsync();
                    var client = new LlrpClient(ep.Value.host, ep.Value.port);
                    client.Log += m => _log.LogInformation("LLRP {Device}: {Message}", d.Name, m);
                    var deviceId = d.Id; var tenantId = d.TenantId; var deviceName = d.Name;
                    client.TagsReported += tags => _ = IngestAsync(tenantId, deviceId, deviceName, tags);
                    try { await client.StartAsync(ct); _clients[d.Id] = (client, key); }
                    catch (Exception ex) when (ex is not OperationCanceledException) { _log.LogWarning("LLRP {Device} at {Endpoint}: {Message}; will retry", d.Name, key, ex.Message); await client.StopAsync(); _clients.Remove(d.Id); }
                }
                foreach (var stale in _clients.Keys.Except(wanted.Select(w => w.d.Id)).ToList()) { await _clients[stale].client.StopAsync(); _clients.Remove(stale); }
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { _log.LogWarning(ex, "LLRP supervisor error"); }
            await Task.Delay(TimeSpan.FromSeconds(_cfg.GetValue("Llrp:RefreshSeconds", 60)), ct);
        }
        foreach (var c in _clients.Values) await c.client.StopAsync();
    }

    private async Task IngestAsync(Guid tenantId, Guid deviceId, string deviceName, IReadOnlyList<LlrpTag> tags)
    {
        try
        {
            using var _ = AmbientContext.Use(tenantId, deviceId: deviceId);
            using var scope = _scopes.CreateScope();
            var batch = new ReadBatchRequest { DeviceId = deviceId, SessionId = "llrp", Reads = tags.Select(t => new ReadRequest { Epc = t.Epc, AntennaPort = t.AntennaId, Rssi = t.PeakRssi, ReadAt = t.LastSeenUtc ?? t.FirstSeenUtc ?? DateTime.UtcNow }).ToList() };
            var r = await scope.ServiceProvider.GetRequiredService<ReadIngestionService>().IngestAsync(batch, ReadSource.Fixed);
            if (r.Alerts > 0) _log.LogInformation("LLRP {Device}: {Reads} reads → {Alerts} alert(s)", deviceName, r.Received, r.Alerts);
        }
        catch (Exception ex) { _log.LogWarning(ex, "LLRP {Device}: ingestion failed", deviceName); }
    }
}

public class HttpOAuthTokenProvider : IOAuthTokenProvider
{
    private readonly HttpClient _http;
    private readonly Dictionary<string, (string token, DateTime expires)> _cache = new();
    public HttpOAuthTokenProvider(HttpClient http) => _http = http;

    public async Task<string> GetTokenAsync(string tokenUrl, string clientId, string clientSecret, string? scope, CancellationToken ct)
    {
        var key = $"{tokenUrl}|{clientId}|{scope}";
        lock (_cache) if (_cache.TryGetValue(key, out var c) && c.expires > DateTime.UtcNow.AddSeconds(30)) return c.token;
        var form = new Dictionary<string, string> { ["grant_type"] = "client_credentials", ["client_id"] = clientId, ["client_secret"] = clientSecret };
        if (!string.IsNullOrEmpty(scope)) form["scope"] = scope;
        using var res = await _http.PostAsync(tokenUrl, new FormUrlEncodedContent(form), ct);
        res.EnsureSuccessStatusCode();
        using var doc = System.Text.Json.JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        var token = doc.RootElement.GetProperty("access_token").GetString() ?? throw new InvalidOperationException("no access_token");
        var expires = doc.RootElement.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var secs) ? DateTime.UtcNow.AddSeconds(secs) : DateTime.UtcNow.AddMinutes(30);
        lock (_cache) _cache[key] = (token, expires);
        return token;
    }
}
