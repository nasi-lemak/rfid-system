using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rfid.Protocols;
using Rfid.Protocols.Llrp;

namespace Rfid.Edge;

/// <summary>
/// The agent: LLRP supervisors and an optional vendor-webhook listener feed a <see cref="ReadBuffer"/>; a flush
/// timer cuts batches into the durable <see cref="EdgeQueue"/>; the <see cref="Forwarder"/> drains it to the server;
/// a heartbeat reports queue depth and reader state so the platform's device health SLAs cover the agent itself.
/// </summary>
public sealed class EdgeAgent : BackgroundService
{
    private readonly EdgeOptions _o; private readonly ILogger<EdgeAgent> _log; private readonly EdgeQueue _queue; private readonly ReadBuffer _buffer; private readonly Forwarder _forwarder; private readonly IServerClient _server; private readonly ReaderCatalog _catalog;
    private readonly Dictionary<Guid, (LlrpClient client, string key)> _readers = new();
    private HttpListener? _listener;

    public EdgeAgent(IOptions<EdgeOptions> o, ILogger<EdgeAgent> log, EdgeQueue queue, ReadBuffer buffer, Forwarder forwarder, IServerClient server, ReaderCatalog catalog)
    { _o = o.Value; _log = log; _queue = queue; _buffer = buffer; _forwarder = forwarder; _server = server; _catalog = catalog; }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _log.LogInformation("Edge agent {Agent} → {Server}; {Readers} reader(s) ({Source}); queue {Path} ({Pending} pending, next #{Seq})", _o.AgentId, _o.Server.Url, _catalog.Current().Count, _catalog.FromServer ? "server revision " + _catalog.Revision : "local config", _o.Queue.Path, _queue.Count, _queue.NextSequence);
        if (!string.IsNullOrWhiteSpace(_o.Listen)) _ = ListenAsync(ct);
        var flush = Task.Run(() => FlushLoopAsync(ct), ct);
        var forward = Task.Run(() => ForwardLoopAsync(ct), ct);
        var heartbeat = Task.Run(() => HeartbeatLoopAsync(ct), ct);
        var readers = Task.Run(() => ReaderLoopAsync(ct), ct);
        var config = Task.Run(() => ConfigLoopAsync(ct), ct);
        await Task.WhenAll(flush, forward, heartbeat, readers, config);
    }

    // ---- readers -------------------------------------------------------------------------------------------
    private readonly SemaphoreSlim _readerWake = new(0, 1);

    private async Task ReaderLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var wanted = _catalog.Current().Where(r => !string.IsNullOrWhiteSpace(r.Host)).ToList();
            foreach (var r in wanted)
            {
                var key = ReaderCatalog.Key(r);
                if (_readers.TryGetValue(r.DeviceId, out var existing) && existing.client.Connected && existing.key == key) continue;
                if (existing.client != null) { await existing.client.StopAsync(); _readers.Remove(r.DeviceId); }
                var client = new LlrpClient(r.Host, r.Port);
                var deviceId = r.DeviceId;
                client.Log += m => _log.LogInformation("LLRP {Device}: {Message}", deviceId, m);
                client.TagsReported += tags => OnTags(deviceId, tags);
                try
                {
                    await client.StartAsync(new LlrpReaderOptions { TransmitPowerDbm = r.PowerDbm, Session = r.Session, TagPopulation = r.TagPopulation, AntennaIds = r.Antennas, GpiStartPort = r.GpiStartPort, ReportEveryNTags = 1 }, ct);
                    _readers[r.DeviceId] = (client, key);
                }
                catch (Exception ex) when (ex is not OperationCanceledException) { _log.LogWarning("LLRP {Host}:{Port}: {Message}; will retry", r.Host, r.Port, ex.Message); await client.StopAsync(); }
            }
            // Readers no longer assigned to this agent are disconnected.
            foreach (var stale in _readers.Keys.Except(wanted.Select(w => w.DeviceId)).ToList()) { if (_readers.Remove(stale, out var sc)) await sc.client.StopAsync(); _log.LogInformation("LLRP {Device}: no longer assigned; disconnected", stale); }
            try { await _readerWake.WaitAsync(TimeSpan.FromSeconds(30), ct); } catch (OperationCanceledException) { break; }
        }
        foreach (var c in _readers.Values) await c.client.StopAsync();
    }

    /// <summary>Pulls the desired configuration (readers assigned to this gateway) and wakes the reader loop when it changed.</summary>
    private async Task ConfigLoopAsync(CancellationToken ct)
    {
        if (_o.PullConfigSeconds <= 0) return;
        await Task.Delay(TimeSpan.FromSeconds(3), ct);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var cfg = await _server.GetConfigAsync(ct);
                if (cfg != null)
                {
                    var changed = _catalog.Apply(cfg);
                    if (changed.Count > 0) { _log.LogInformation("Reader configuration revision {Revision}: {Count} reader(s) changed", cfg.Revision, changed.Count); if (_readerWake.CurrentCount == 0) _readerWake.Release(); }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { _log.LogDebug("config pull: {Message}", ex.Message); }
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(10, _o.PullConfigSeconds)), ct);
        }
    }

    private void OnTags(Guid deviceId, IReadOnlyList<LlrpTag> tags)
    {
        var reads = tags.Select(t => new ReadRequest { Epc = t.Epc, AntennaPort = t.AntennaId, Rssi = t.PeakRssi, ReadAt = t.LastSeenUtc ?? t.FirstSeenUtc ?? DateTime.UtcNow });
        var full = _buffer.Add(deviceId, reads, "llrp");
        if (full != null) _queue.Enqueue(full);
    }

    // ---- vendor push formats -------------------------------------------------------------------------------
    private async Task ListenAsync(CancellationToken ct)
    {
        try
        {
            _listener = new HttpListener(); _listener.Prefixes.Add(_o.Listen!.EndsWith('/') ? _o.Listen : _o.Listen + "/"); _listener.Start();
            _log.LogInformation("Listening for reader pushes on {Url} (POST /impinj|/zebra|/generic?deviceId=…)", _o.Listen);
        }
        catch (Exception ex) { _log.LogError(ex, "Cannot listen on {Url}", _o.Listen); return; }
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync().WaitAsync(ct); } catch (OperationCanceledException) { break; } catch (Exception ex) { _log.LogDebug(ex, "listener"); continue; }
            _ = Task.Run(() => HandlePushAsync(ctx), ct);
        }
        _listener.Stop();
    }

    private async Task HandlePushAsync(HttpListenerContext ctx)
    {
        try
        {
            var path = ctx.Request.Url?.AbsolutePath.Trim('/').ToLowerInvariant() ?? "";
            var vendor = path switch { "impinj" => "impinj", "zebra" => "zebra", _ => null };
            using var reader = new StreamReader(ctx.Request.InputStream);
            var body = await reader.ReadToEndAsync();
            var batch = IngestAdapters.Parse(body, vendor);
            if (Guid.TryParse(ctx.Request.QueryString["deviceId"], out var d)) batch.DeviceId = d;
            if (batch.DeviceId == null) { ctx.Response.StatusCode = 400; await Write(ctx, "{\"error\":\"deviceId required (query string or payload)\"}"); return; }
            var full = _buffer.Add(batch.DeviceId.Value, batch.Reads, vendor ?? "push");
            if (full != null) _queue.Enqueue(full);
            ctx.Response.StatusCode = 202; await Write(ctx, $"{{\"accepted\":{batch.Reads.Count}}}");
        }
        catch (Exception ex) { ctx.Response.StatusCode = 400; await Write(ctx, JsonSerializer.Serialize(new { error = ex.Message })); }
    }
    private static async Task Write(HttpListenerContext ctx, string json) { ctx.Response.ContentType = "application/json"; await using var w = new StreamWriter(ctx.Response.OutputStream); await w.WriteAsync(json); }

    // ---- batching, forwarding, heartbeat -------------------------------------------------------------------
    private async Task FlushLoopAsync(CancellationToken ct)
    {
        var period = TimeSpan.FromSeconds(Math.Max(0.2, _o.Flush.Seconds));
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(period, ct);
            foreach (var b in _buffer.Flush()) _queue.Enqueue(b);
        }
    }

    private async Task ForwardLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var sent = 0;
            try { sent = await _forwarder.DrainAsync(ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { _log.LogWarning(ex, "forwarder"); }
            if (sent > 0) _log.LogInformation("Forwarded {Count} batch(es); {Pending} pending", sent, _queue.Count);
            await Task.Delay(_forwarder.Failures > 0 ? _forwarder.Backoff : TimeSpan.FromMilliseconds(500), ct);
        }
    }

    private async Task HeartbeatLoopAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(5), ct);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var metrics = new
                {
                    ipAddress = Dns.GetHostName(),
                    metrics = new Dictionary<string, object?>
                    {
                        ["agent"] = _o.AgentId, ["queuePending"] = _queue.Count, ["queuePoison"] = _queue.PoisonCount, ["nextSequence"] = _queue.NextSequence,
                        ["delivered"] = _forwarder.Delivered, ["duplicates"] = _forwarder.Duplicates, ["lastError"] = _forwarder.LastError, ["lastDeliveryAt"] = _forwarder.LastDeliveryAt,
                        ["configRevision"] = _catalog.Revision,
                        ["readers"] = _catalog.Current().Select(r => new { r.DeviceId, r.Host, connected = _readers.TryGetValue(r.DeviceId, out var c) && c.client.Connected, tags = _readers.TryGetValue(r.DeviceId, out var c2) ? c2.client.TagsReceived : 0 }).ToList(),
                    },
                };
                await _server.HeartbeatAsync(null, metrics, ct);
                foreach (var (id, c) in _readers) await _server.HeartbeatAsync(id, new { firmwareVersion = c.client.Capabilities?.Firmware, metrics = new Dictionary<string, object?> { ["tagsReceived"] = c.client.TagsReceived, ["connectedAt"] = c.client.ConnectedAt, ["transport"] = "edge-llrp", ["agent"] = _o.AgentId } }, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { _log.LogDebug("heartbeat: {Message}", ex.Message); }
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(10, _o.HeartbeatSeconds)), ct);
        }
    }
}
