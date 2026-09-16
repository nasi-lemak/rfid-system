using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Client;
using Rfid.Protocols;

namespace Rfid.Edge;

/// <summary>
/// On-site MQTT bridge: readers that publish to a local broker (Impinj IoT Interface, Zebra IoT Connector, generic
/// JSON) keep working through a WAN outage because their publications land in the same durable queue as LLRP
/// reads. Topics map to platform device ids by MQTT filter (same matching as the server's subscriber).
/// </summary>
public sealed class MqttBridge : BackgroundService
{
    private readonly EdgeOptions.MqttOptions _o; private readonly ReadBuffer _buffer; private readonly EdgeQueue _queue; private readonly ILogger<MqttBridge> _log;
    private IMqttClient? _client;
    public long Messages { get; private set; }
    public long Unmatched { get; private set; }

    public MqttBridge(IOptions<EdgeOptions> o, ReadBuffer buffer, EdgeQueue queue, ILogger<MqttBridge> log) { _o = o.Value.Mqtt; _buffer = buffer; _queue = queue; _log = log; }

    /// <summary>Resolves the device a topic belongs to (first matching filter), or null.</summary>
    public static Guid? DeviceFor(IReadOnlyDictionary<string, Guid> map, string topic)
    {
        foreach (var (pattern, id) in map) if (TopicMatcher.Matches(pattern, topic)) return id;
        return null;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!_o.Enabled) return;
        _client = new MqttFactory().CreateMqttClient();
        _client.ApplicationMessageReceivedAsync += e =>
        {
            Messages++;
            var topic = e.ApplicationMessage.Topic;
            var payload = e.ApplicationMessage.PayloadSegment.Count > 0 ? Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment) : "";
            if (string.IsNullOrWhiteSpace(payload)) return Task.CompletedTask;
            try
            {
                var batch = IngestAdapters.Parse(payload, _o.Vendor);
                var deviceId = batch.DeviceId ?? DeviceFor(_o.Devices, topic);
                if (deviceId == null) { Unmatched++; _log.LogDebug("MQTT {Topic}: no device mapping", topic); return Task.CompletedTask; }
                var full = _buffer.Add(deviceId.Value, batch.Reads, "mqtt:" + topic);
                if (full != null) _queue.Enqueue(full);
            }
            catch (Exception ex) { _log.LogWarning("MQTT {Topic}: {Message}", topic, ex.Message); }
            return Task.CompletedTask;
        };
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!_client.IsConnected)
                {
                    var b = new MqttClientOptionsBuilder().WithTcpServer(_o.Host, _o.Port).WithClientId("rfid-edge-" + Environment.MachineName);
                    if (!string.IsNullOrEmpty(_o.Username)) b = b.WithCredentials(_o.Username, _o.Password);
                    await _client.ConnectAsync(b.Build(), ct);
                    foreach (var t in _o.Topics) await _client.SubscribeAsync(t, cancellationToken: ct);
                    _log.LogInformation("MQTT bridge connected to {Host}:{Port}, subscribed to {Topics}", _o.Host, _o.Port, string.Join(", ", _o.Topics));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { _log.LogWarning("MQTT bridge: {Message}; retrying", ex.Message); }
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
        }
        if (_client.IsConnected) await _client.DisconnectAsync();
    }
}
