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
    /// <summary>A prepared integration batch for one endpoint (Integrations module).</summary>
    public const string Integration = "integration";
    /// <summary>An operation a rule asked for, executed after the triggering unit of work committed (Operations module).</summary>
    public const string RunOperation = "operation.run";
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

/// <summary>
/// One handler per outbox kind. Modules register their own (integration batches, rule-triggered operations)
/// next to the platform's live/webhook/notification handlers; the dispatcher only knows the retry policy.
/// </summary>
public interface IOutboxHandler
{
    string Kind { get; }
    Task DeliverAsync(OutboxMessage message, CancellationToken ct);
    /// <summary>Called after a failed attempt (<paramref name="deadLettered"/> when no more attempts follow).</summary>
    Task OnFailureAsync(OutboxMessage message, bool deadLettered, CancellationToken ct) => Task.CompletedTask;
}

public sealed class LiveOutboxHandler : IOutboxHandler
{
    private readonly ILiveTransport _live; private readonly string _kind; private readonly string _method;
    public LiveOutboxHandler(ILiveTransport live, string kind, string method) { _live = live; _kind = kind; _method = method; }
    public string Kind => _kind;
    public Task DeliverAsync(OutboxMessage m, CancellationToken ct) => _live.SendAsync(m.TenantId, _method, JsonSerializer.Deserialize<JsonElement>(m.Payload), ct);
}

public sealed class WebhookOutboxHandler : IOutboxHandler
{
    private readonly IWebhookTransport _webhooks;
    public WebhookOutboxHandler(IWebhookTransport webhooks) => _webhooks = webhooks;
    public string Kind => OutboxKinds.Webhook;
    public async Task DeliverAsync(OutboxMessage m, CancellationToken ct)
    {
        var (ok, error) = await _webhooks.PostJsonAsync(m.Destination ?? throw new InvalidOperationException("webhook without URL"), m.Payload, ct);
        if (!ok) throw new InvalidOperationException(error ?? "webhook failed");
    }
}

public sealed class NotificationOutboxHandler : IOutboxHandler
{
    private readonly IAppDb _db; private readonly Services.INotificationSender _sender;
    public NotificationOutboxHandler(IAppDb db, Services.INotificationSender sender) { _db = db; _sender = sender; }
    public string Kind => OutboxKinds.Notification;
    public async Task DeliverAsync(OutboxMessage m, CancellationToken ct)
    {
        var env = JsonSerializer.Deserialize<OutboxDispatcher.NotificationEnvelope>(m.Payload, Outbox.Json) ?? throw new InvalidOperationException("bad notification payload");
        var channel = await _db.NotificationChannels.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == env.ChannelId, ct) ?? throw new InvalidOperationException("channel deleted");
        var recipient = await _sender.SendAsync(channel, env.Message, ct);
        await MarkAsync(env, NotificationStatus.Sent, recipient, null, ct);
    }
    public async Task OnFailureAsync(OutboxMessage m, bool deadLettered, CancellationToken ct)
    {
        if (!deadLettered) return; // still retrying – the log stays Queued
        var env = JsonSerializer.Deserialize<OutboxDispatcher.NotificationEnvelope>(m.Payload, Outbox.Json); if (env == null) return;
        await MarkAsync(env, NotificationStatus.Failed, null, m.Error, ct);
    }
    private async Task MarkAsync(OutboxDispatcher.NotificationEnvelope env, NotificationStatus status, string? recipient, string? error, CancellationToken ct)
    {
        var log = await _db.NotificationLogs.IgnoreQueryFilters().FirstOrDefaultAsync(l => l.Id == env.LogId, ct); if (log == null) return;
        log.Status = status; log.Error = error; log.SentAt = DateTime.UtcNow; if (recipient != null) log.Recipient = recipient;
    }
}

/// <summary>Delivers committed outbox messages with bounded retries. Runs on one node (lease) and is nudged in-process after each commit.</summary>
public class OutboxDispatcher
{
    private readonly IAppDb _db; private readonly Dictionary<string, IOutboxHandler> _handlers;
    public int MaxAttempts { get; set; } = 8;

    public OutboxDispatcher(IAppDb db, IEnumerable<IOutboxHandler> handlers) { _db = db; _handlers = handlers.ToDictionary(h => h.Kind); }

    /// <summary>Convenience wiring of the platform handlers (tests, small hosts).</summary>
    public OutboxDispatcher(IAppDb db, ILiveTransport live, IWebhookTransport webhooks, Services.INotificationSender notifications, params IOutboxHandler[] extra)
        : this(db, Platform(db, live, webhooks, notifications).Concat(extra)) { }

    public static IEnumerable<IOutboxHandler> Platform(IAppDb db, ILiveTransport live, IWebhookTransport webhooks, Services.INotificationSender notifications) => new IOutboxHandler[]
    {
        new LiveOutboxHandler(live, OutboxKinds.LiveEvent, "event"), new LiveOutboxHandler(live, OutboxKinds.LiveAlert, "alert"),
        new WebhookOutboxHandler(webhooks), new NotificationOutboxHandler(db, notifications),
    };

    public record Stats(int Delivered, int Failed, int Dead);

    /// <summary>Processes up to <paramref name="take"/> due messages across all tenants, oldest first.</summary>
    public async Task<Stats> DispatchAsync(DateTime now, int take = 200, CancellationToken ct = default)
    {
        var due = await _db.Outbox.IgnoreQueryFilters().Where(m => m.ProcessedAt == null && (m.NextAttemptAt == null || m.NextAttemptAt <= now) && m.Attempts < MaxAttempts).OrderBy(m => m.CreatedAt).Take(take).ToListAsync(ct);
        int delivered = 0, failed = 0, dead = 0;
        foreach (var m in due)
        {
            m.Attempts++;
            var handler = _handlers.GetValueOrDefault(m.Kind);
            try
            {
                if (handler == null) throw new InvalidOperationException($"no handler for outbox kind '{m.Kind}'");
                await handler.DeliverAsync(m, ct);
                m.ProcessedAt = now; m.Error = null; delivered++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                m.Error = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
                var isDead = m.Attempts >= MaxAttempts;
                if (isDead) { m.ProcessedAt = now; dead++; }               // dead-letter: kept with its error for inspection
                else { m.NextAttemptAt = now.AddSeconds(Math.Min(3600, 5 * Math.Pow(2, m.Attempts))); failed++; }
                if (handler != null) { try { await handler.OnFailureAsync(m, isDead, ct); } catch (Exception fx) when (fx is not OperationCanceledException) { m.Error += " | on-failure: " + fx.Message; } }
            }
        }
        if (due.Count > 0) await _db.SaveChangesAsync(ct);
        return new Stats(delivered, failed, dead);
    }

    public record NotificationEnvelope(Guid LogId, Guid ChannelId, Services.NotificationMessage Message);
}
