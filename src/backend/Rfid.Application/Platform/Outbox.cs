using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Rfid.Application.Contracts;
using Rfid.Domain;
using Rfid.Domain.Entities;

namespace Rfid.Application.Platform;

/// <summary>Kinds of outbox messages. Closed set; the dispatcher knows how to deliver each.</summary>
public static class OutboxKinds
{
    public const string LiveEvent = "live.event";
    public const string LiveAlert = "live.alert";
    public const string Webhook = "webhook";
    public const string Notification = "notification";
}

/// <summary>Where live pushes actually go (SignalR in the API, a recorder in tests). Only the outbox dispatcher and the post-commit read stream use it.</summary>
public interface ILiveTransport
{
    Task SendAsync(Guid tenantId, string method, object payload, CancellationToken ct = default);
}

/// <summary>Raw HTTP delivery for webhooks (the outbox dispatcher's transport).</summary>
public interface IWebhookTransport
{
    Task<(bool ok, string? error)> PostJsonAsync(string url, string json, CancellationToken ct = default);
}

/// <summary>Writes an outbox row into the current unit of work (same DbContext → same transaction as the business change).</summary>
public class Outbox
{
    private readonly IAppDb _db; private readonly ICurrentContext _ctx;
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public Outbox(IAppDb db, ICurrentContext ctx) { _db = db; _ctx = ctx; }
    public OutboxMessage Enqueue(string kind, object payload, string? destination = null, Guid? tenantId = null)
    {
        var m = new OutboxMessage { TenantId = tenantId ?? _ctx.TenantId, Kind = kind, Destination = destination, Payload = JsonSerializer.Serialize(payload, Json) };
        _db.Outbox.Add(m);
        return m;
    }
}

/// <summary>ILivePublisher that defers events/alerts to the outbox (committed with the state) and streams raw reads directly (telemetry, best effort).</summary>
public class OutboxLivePublisher : ILivePublisher
{
    private readonly Outbox _outbox; private readonly ILiveTransport _transport; private readonly ICurrentContext _ctx;
    public OutboxLivePublisher(Outbox outbox, ILiveTransport transport, ICurrentContext ctx) { _outbox = outbox; _transport = transport; _ctx = ctx; }
    public Task PublishReadsAsync(IEnumerable<LiveRead> reads, CancellationToken ct = default) => _transport.SendAsync(_ctx.TenantId, "reads", reads.ToList(), ct);
    public Task PublishAlertAsync(Alert alert, CancellationToken ct = default) { _outbox.Enqueue(OutboxKinds.LiveAlert, new { alert.Id, alert.ItemId, alert.LocationId, Severity = alert.Severity.ToString(), alert.Message, alert.RaisedAt }, tenantId: alert.TenantId == Guid.Empty ? null : alert.TenantId); return Task.CompletedTask; }
    public Task PublishEventAsync(ItemEvent ev, CancellationToken ct = default) { _outbox.Enqueue(OutboxKinds.LiveEvent, new { ev.Id, ev.ItemId, Type = ev.Type.ToString(), ev.FromLocationId, ev.ToLocationId, ev.ToState, ev.OccurredAt }, tenantId: ev.TenantId == Guid.Empty ? null : ev.TenantId); return Task.CompletedTask; }
}

/// <summary>IWebhookDispatcher that defers to the outbox.</summary>
public class OutboxWebhookDispatcher : IWebhookDispatcher
{
    private readonly Outbox _outbox;
    public OutboxWebhookDispatcher(Outbox outbox) => _outbox = outbox;
    public Task SendAsync(string url, object payload, CancellationToken ct = default) { _outbox.Enqueue(OutboxKinds.Webhook, payload, url); return Task.CompletedTask; }
}

/// <summary>Delivers committed outbox messages with bounded retries. Runs on one node (lease) and is nudged in-process after each commit.</summary>
public class OutboxDispatcher
{
    private readonly IAppDb _db; private readonly ILiveTransport _live; private readonly IWebhookTransport _webhooks; private readonly Services.INotificationSender _notifications;
    public int MaxAttempts { get; set; } = 8;
    public OutboxDispatcher(IAppDb db, ILiveTransport live, IWebhookTransport webhooks, Services.INotificationSender notifications) { _db = db; _live = live; _webhooks = webhooks; _notifications = notifications; }

    public record Stats(int Delivered, int Failed, int Dead);

    /// <summary>Processes up to <paramref name="take"/> due messages across all tenants, oldest first.</summary>
    public async Task<Stats> DispatchAsync(DateTime now, int take = 200, CancellationToken ct = default)
    {
        var due = await _db.Outbox.IgnoreQueryFilters().Where(m => m.ProcessedAt == null && (m.NextAttemptAt == null || m.NextAttemptAt <= now) && m.Attempts < MaxAttempts).OrderBy(m => m.CreatedAt).Take(take).ToListAsync(ct);
        int delivered = 0, failed = 0, dead = 0;
        foreach (var m in due)
        {
            m.Attempts++;
            try
            {
                await DeliverAsync(m, ct);
                m.ProcessedAt = now; m.Error = null; delivered++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                m.Error = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
                if (m.Attempts >= MaxAttempts) { m.ProcessedAt = now; dead++; }               // dead-letter: kept with its error for inspection
                else { m.NextAttemptAt = now.AddSeconds(Math.Min(3600, 5 * Math.Pow(2, m.Attempts))); failed++; }
                if (m.Kind == OutboxKinds.Notification) await MarkNotificationAsync(m, NotificationStatus.Failed, null, m.Error, ct);
            }
        }
        if (due.Count > 0) await _db.SaveChangesAsync(ct);
        return new Stats(delivered, failed, dead);
    }

    private async Task DeliverAsync(OutboxMessage m, CancellationToken ct)
    {
        switch (m.Kind)
        {
            case OutboxKinds.LiveEvent: await _live.SendAsync(m.TenantId, "event", JsonSerializer.Deserialize<JsonElement>(m.Payload), ct); break;
            case OutboxKinds.LiveAlert: await _live.SendAsync(m.TenantId, "alert", JsonSerializer.Deserialize<JsonElement>(m.Payload), ct); break;
            case OutboxKinds.Webhook:
            {
                var (ok, error) = await _webhooks.PostJsonAsync(m.Destination ?? throw new InvalidOperationException("webhook without URL"), m.Payload, ct);
                if (!ok) throw new InvalidOperationException(error ?? "webhook failed");
                break;
            }
            case OutboxKinds.Notification:
            {
                var env = JsonSerializer.Deserialize<NotificationEnvelope>(m.Payload, Outbox.Json) ?? throw new InvalidOperationException("bad notification payload");
                var channel = await _db.NotificationChannels.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == env.ChannelId, ct) ?? throw new InvalidOperationException("channel deleted");
                var recipient = await _notifications.SendAsync(channel, env.Message, ct);
                await MarkNotificationAsync(m, NotificationStatus.Sent, recipient, null, ct);
                break;
            }
            default: throw new InvalidOperationException($"unknown outbox kind {m.Kind}");
        }
    }

    private async Task MarkNotificationAsync(OutboxMessage m, NotificationStatus status, string? recipient, string? error, CancellationToken ct)
    {
        var env = JsonSerializer.Deserialize<NotificationEnvelope>(m.Payload, Outbox.Json); if (env == null) return;
        var log = await _db.NotificationLogs.IgnoreQueryFilters().FirstOrDefaultAsync(l => l.Id == env.LogId, ct); if (log == null) return;
        if (status == NotificationStatus.Failed && m.Attempts < MaxAttempts) return; // still retrying – leave it Queued
        log.Status = status; log.Error = error; log.SentAt = DateTime.UtcNow; if (recipient != null) log.Recipient = recipient;
    }

    public record NotificationEnvelope(Guid LogId, Guid ChannelId, Services.NotificationMessage Message);
}
