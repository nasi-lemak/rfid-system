using Rfid.Protocols;
using Microsoft.EntityFrameworkCore;
using Rfid.Api.Auth;
using Rfid.Application.Cluster;
using Rfid.Application.Contracts;
using Rfid.Protocols.Llrp;
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
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, (LlrpClient client, string host)> _clients = new();
    public LlrpReaderService(IServiceScopeFactory scopes, ILogger<LlrpReaderService> log, IConfiguration cfg) { _scopes = scopes; _log = log; _cfg = cfg; }

    public static LlrpReaderOptions OptionsFor(Device d) => Rfid.Application.Devices.LlrpDeviceConfig.Options(d);

    public record ReaderStatus(Guid DeviceId, string Endpoint, bool Connected, DateTime? ConnectedAt, long TagsReceived, ReaderCapabilities? Capabilities, LlrpReaderOptions Options);
    public IEnumerable<ReaderStatus> Statuses() => _clients.Select(kv => new ReaderStatus(kv.Key, kv.Value.host, kv.Value.client.Connected, kv.Value.client.ConnectedAt, kv.Value.client.TagsReceived, kv.Value.client.Capabilities, kv.Value.client.Options)).ToList();
    public ReaderStatus? Status(Guid deviceId) => Statuses().FirstOrDefault(s => s.DeviceId == deviceId);
    public Task SetGpoAsync(Guid deviceId, int port, bool state, CancellationToken ct) => _clients.TryGetValue(deviceId, out var c) && c.client.Connected ? c.client.SetGpoAsync(port, state, ct) : throw new InvalidOperationException("Reader is not connected");
    /// <summary>Drops the connection so the supervisor reconnects with fresh configuration.</summary>
    public async Task ReconnectAsync(Guid deviceId) { if (_clients.Remove(deviceId, out var c)) await c.client.StopAsync(); }

    /// <summary>Server-driven endpoint: a reader driven by an on-site edge agent is never also driven from the server.</summary>
    public static (string host, int port)? Endpoint(Device d) => Rfid.Application.Devices.LlrpDeviceConfig.IsEdgeManaged(d) ? null : Rfid.Application.Devices.LlrpDeviceConfig.Endpoint(d);

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
                var refresh = _cfg.GetValue("Llrp:RefreshSeconds", 60);
                var candidates = devices.Select(d => (d, ep: Endpoint(d))).Where(x => x.ep != null).ToList();
                // In a cluster every reader is driven by exactly one node: the one holding its "llrp:{deviceId}" lease.
                var wanted = new List<(Device d, (string host, int port)? ep)>();
                using (var ls = _scopes.CreateScope())
                {
                    var leases = ls.ServiceProvider.GetRequiredService<LeaseService>();
                    foreach (var c in candidates)
                        if (await leases.TryAcquireAsync("llrp:" + c.d.Id, ClusterNode.Id, TimeSpan.FromSeconds(Math.Max(90, refresh * 1.5)), ct: ct)) wanted.Add(c);
                }
                foreach (var (d, ep) in wanted)
                {
                    var opts = OptionsFor(d);
                    var key = $"{ep!.Value.host}:{ep.Value.port}|{System.Text.Json.JsonSerializer.Serialize(opts)}";
                    if (_clients.TryGetValue(d.Id, out var existing) && existing.client.Connected && existing.host == key) { await HeartbeatAsync(d, existing.client, ct); continue; }
                    if (existing.client != null) await existing.client.StopAsync();
                    var client = new LlrpClient(ep.Value.host, ep.Value.port);
                    client.Log += m => _log.LogInformation("LLRP {Device}: {Message}", d.Name, m);
                    var deviceId = d.Id; var tenantId = d.TenantId; var deviceName = d.Name;
                    client.TagsReported += tags => _ = IngestAsync(tenantId, deviceId, deviceName, tags);
                    client.GpiChanged += (port, high) => _log.LogInformation("LLRP {Device}: GPI {Port} {State}", deviceName, port, high ? "high" : "low");
                    try { await client.StartAsync(opts, ct); _clients[d.Id] = (client, key); await HeartbeatAsync(d, client, ct); }
                    catch (Exception ex) when (ex is not OperationCanceledException) { _log.LogWarning("LLRP {Device} at {Endpoint}: {Message}; will retry", d.Name, key, ex.Message); await client.StopAsync(); _clients.TryRemove(d.Id, out _); }
                }
                foreach (var stale in _clients.Keys.Except(wanted.Select(w => w.d.Id)).ToList()) { if (_clients.TryRemove(stale, out var sc)) await sc.client.StopAsync(); }
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { _log.LogWarning(ex, "LLRP supervisor error"); }
            await Task.Delay(TimeSpan.FromSeconds(_cfg.GetValue("Llrp:RefreshSeconds", 60)), ct);
        }
        foreach (var c in _clients.Values) await c.client.StopAsync();
        try
        {
            using var ls = _scopes.CreateScope(); var leases = ls.ServiceProvider.GetRequiredService<LeaseService>();
            foreach (var id in _clients.Keys) await leases.ReleaseAsync("llrp:" + id, ClusterNode.Id);
        }
        catch (Exception ex) { _log.LogDebug(ex, "LLRP lease release failed"); }
    }

    /// <summary>Records a health sample for a connected LLRP reader (firmware from GET_READER_CAPABILITIES, tags/min from the client counter).</summary>
    private async Task HeartbeatAsync(Device d, LlrpClient client, CancellationToken ct)
    {
        try
        {
            using var _ = AmbientContext.Use(d.TenantId, deviceId: d.Id);
            using var scope = _scopes.CreateScope();
            await scope.ServiceProvider.GetRequiredService<DeviceHealthService>().RecordAsync(d.Id, new HeartbeatRequest { FirmwareVersion = client.Capabilities?.Firmware, Metrics = new() { ["tagsReceived"] = client.TagsReceived, ["connectedAt"] = client.ConnectedAt, ["transport"] = "llrp" } }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { _log.LogDebug(ex, "LLRP {Device}: heartbeat failed", d.Name); }
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
