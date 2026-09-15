using System.Text;
using Microsoft.EntityFrameworkCore;
using MQTTnet;
using MQTTnet.Client;
using Rfid.Api.Auth;
using Rfid.Application.Cluster;
using Rfid.Application.Services;
using Rfid.Domain;
using Rfid.Infrastructure.Persistence;

namespace Rfid.Api.Background;

/// <summary>
/// Subscribes to an MQTT broker and feeds reader publications into the ingestion pipeline. A device is
/// matched by its Config["mqttTopic"] (exact topic or MQTT wildcard pattern); payloads may be Impinj IoT
/// Interface, Zebra IoT Connector or generic JSON (Config["vendor"] hints the format). Enable with
/// Mqtt:Enabled=true, Mqtt:Host, Mqtt:Port, Mqtt:Username/Password, Mqtt:Topics (list of subscriptions).
/// </summary>
public class MqttIngestService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IConfiguration _cfg;
    private readonly ILogger<MqttIngestService> _log;
    private IMqttClient? _client;

    public MqttIngestService(IServiceScopeFactory scopes, IConfiguration cfg, ILogger<MqttIngestService> log) { _scopes = scopes; _cfg = cfg; _log = log; }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!_cfg.GetValue("Mqtt:Enabled", false)) return;
        var host = _cfg["Mqtt:Host"] ?? "localhost"; var port = _cfg.GetValue("Mqtt:Port", 1883);
        var topics = _cfg.GetSection("Mqtt:Topics").Get<string[]>() ?? new[] { "rfid/#" };
        _client = new MqttFactory().CreateMqttClient();
        _client.ApplicationMessageReceivedAsync += OnMessageAsync;
        var leader = false;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                bool hold;
                using (var ls = _scopes.CreateScope()) hold = await ls.ServiceProvider.GetRequiredService<LeaseService>().TryAcquireAsync("job:mqtt", ClusterNode.Id, TimeSpan.FromSeconds(30), ct: ct);
                if (hold != leader) { leader = hold; _log.LogInformation("MQTT ingest: node {Node} is now {State}", ClusterNode.Id, hold ? "the subscriber" : "standby"); }
                if (!hold)
                {
                    if (_client.IsConnected) await _client.DisconnectAsync(cancellationToken: ct);
                }
                else if (!_client.IsConnected)
                {
                    var b = new MqttClientOptionsBuilder().WithTcpServer(host, port).WithClientId("rfid-platform-" + ClusterNode.Id);
                    if (_cfg["Mqtt:Username"] is { } u) b = b.WithCredentials(u, _cfg["Mqtt:Password"]);
                    await _client.ConnectAsync(b.Build(), ct);
                    foreach (var t in topics) await _client.SubscribeAsync(t, cancellationToken: ct);
                    _log.LogInformation("MQTT connected to {Host}:{Port}, subscribed to {Topics}", host, port, string.Join(", ", topics));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { _log.LogWarning("MQTT connect failed: {Message}; retrying", ex.Message); }
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
        }
    }

    private async Task OnMessageAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        var topic = e.ApplicationMessage.Topic;
        var payload = e.ApplicationMessage.PayloadSegment.Count > 0 ? Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment) : "";
        if (string.IsNullOrWhiteSpace(payload)) return;
        try
        {
            using var lookup = _scopes.CreateScope();
            var db = lookup.ServiceProvider.GetRequiredService<AppDbContext>();
            var candidates = await db.Devices.IgnoreQueryFilters().Where(d => d.Kind != DeviceKind.Handheld).ToListAsync();
            var device = candidates.FirstOrDefault(d => d.Config.TryGetValue("mqttTopic", out var t) && t != null && TopicMatches(t.ToString()!, topic));
            if (device == null) { _log.LogDebug("MQTT message on {Topic} matched no device", topic); return; }
            var batch = IngestAdapters.Parse(payload, device.Config.TryGetValue("vendor", out var v) ? v?.ToString() : null);
            batch.DeviceId = device.Id; batch.SessionId ??= topic;
            using var _ = AmbientContext.Use(device.TenantId, deviceId: device.Id);
            using var scope = _scopes.CreateScope();
            var result = await scope.ServiceProvider.GetRequiredService<ReadIngestionService>().IngestAsync(batch, ReadSource.Fixed);
            _log.LogDebug("MQTT {Topic}: {Received} reads, {Resolved} resolved, {Alerts} alerts", topic, result.Received, result.Resolved, result.Alerts);
        }
        catch (Exception ex) { _log.LogWarning(ex, "MQTT message on {Topic} could not be ingested", topic); }
    }

    /// <summary>MQTT wildcard matching (+ single level, # multi level).</summary>
    public static bool TopicMatches(string pattern, string topic)
    {
        var p = pattern.Split('/'); var t = topic.Split('/');
        for (var i = 0; i < p.Length; i++)
        {
            if (p[i] == "#") return true;
            if (i >= t.Length) return false;
            if (p[i] != "+" && p[i] != t[i]) return false;
        }
        return p.Length == t.Length;
    }
}
