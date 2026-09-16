using Microsoft.EntityFrameworkCore;
using Rfid.Application.Contracts;
using Rfid.Application.Platform;
using Rfid.Domain;
using Rfid.Domain.Entities;

namespace Rfid.Application.Services;

public record NotificationMessage(string Subject, string Body, Severity Severity, Guid? AlertId, Guid? ItemId, string? Link);

/// <summary>Delivers a message over a channel (SMTP / Twilio-style SMS / Teams / Slack / webhook in the API; recorded in tests).</summary>
public interface INotificationSender
{
    /// <summary>Returns the recipient description (address list, phone number, webhook host) for the log.</summary>
    Task<string> SendAsync(NotificationChannel channel, NotificationMessage message, CancellationToken ct = default);
}

public class NullNotificationSender : INotificationSender
{
    public List<(NotificationChannel channel, NotificationMessage message)> Sent { get; } = new();
    public Task<string> SendAsync(NotificationChannel channel, NotificationMessage message, CancellationToken ct = default) { Sent.Add((channel, message)); return Task.FromResult("test"); }
}

/// <summary>
/// Routes alerts to notification channels (rule actions, catch-all channels) and drives escalation policies for
/// alerts nobody acknowledges. Every delivery attempt is written to NotificationLog.
/// </summary>
public class NotificationService
{
    private readonly IAppDb _db; private readonly INotificationSender _sender; private readonly Outbox? _outbox;
    public string? BaseUrl { get; set; }
    /// <param name="outbox">When supplied, deliveries are queued in the outbox and sent after commit; otherwise sent inline (tests, tools).</param>
    public NotificationService(IAppDb db, INotificationSender sender, Outbox? outbox = null) { _db = db; _sender = sender; _outbox = outbox; }

    private static int Rank(Severity s) => s switch { Severity.Critical => 3, Severity.Warning => 2, _ => 1 };

    /// <summary>Called when an alert is created: immediate notifications (rule channels + catch-all) and escalation scheduling.</summary>
    public async Task<int> OnAlertRaisedAsync(Alert alert, Rule? rule, CancellationToken ct = default)
    {
        var channels = await _db.NotificationChannels.Where(c => c.Enabled).ToListAsync(ct);
        var targets = new HashSet<Guid>();
        if (rule != null) foreach (var id in rule.NotifyChannelIds) targets.Add(id);
        foreach (var c in channels.Where(c => c.CatchAll && Rank(alert.Severity) >= Rank(c.MinSeverity))) targets.Add(c.Id);

        var policyId = alert.EscalationPolicyId ?? rule?.EscalationPolicyId;
        EscalationPolicy? policy = null;
        if (policyId != null) policy = await _db.EscalationPolicies.FirstOrDefaultAsync(p => p.Id == policyId, ct);
        policy ??= (await _db.EscalationPolicies.Where(p => p.IsDefault).ToListAsync(ct)).Where(p => Rank(alert.Severity) >= Rank(p.MinSeverity)).OrderBy(p => p.Name).FirstOrDefault();
        if (policy != null && policy.Steps.Count > 0)
        {
            alert.EscalationPolicyId = policy.Id;
            var first = policy.Steps[0];
            if (first.AfterMinutes <= 0) { foreach (var id in first.ChannelIds) targets.Add(id); alert.EscalationLevel = 1; alert.NextEscalationAt = NextAt(policy, 1, alert.RaisedAt); }
            else alert.NextEscalationAt = alert.RaisedAt.AddMinutes(first.AfterMinutes);
        }
        var sent = 0;
        foreach (var ch in channels.Where(c => targets.Contains(c.Id))) if (await DeliverAsync(ch, alert, 0, null, ct)) sent++;
        return sent;
    }

    private static DateTime? NextAt(EscalationPolicy p, int level, DateTime from)
    {
        if (level < p.Steps.Count) return from.AddMinutes(Math.Max(1, p.Steps[level].AfterMinutes));
        return p.RepeatLastStep ? from.AddMinutes(Math.Max(1, p.Steps[^1].AfterMinutes)) : null;
    }

    /// <summary>Escalates open, unacknowledged alerts whose next step is due. Returns notifications sent.</summary>
    public async Task<int> EscalateDueAsync(DateTime now, CancellationToken ct = default)
    {
        var due = await _db.Alerts.Where(a => a.Status == AlertStatus.Open && a.NextEscalationAt != null && a.NextEscalationAt <= now && a.EscalationPolicyId != null).Take(200).ToListAsync(ct);
        if (due.Count == 0) return 0;
        var policies = await _db.EscalationPolicies.ToDictionaryAsync(p => p.Id, ct);
        var channels = await _db.NotificationChannels.Where(c => c.Enabled).ToDictionaryAsync(c => c.Id, ct);
        var sent = 0;
        foreach (var a in due)
        {
            if (!policies.TryGetValue(a.EscalationPolicyId!.Value, out var p) || p.Steps.Count == 0) { a.NextEscalationAt = null; continue; }
            var stepIndex = Math.Min(a.EscalationLevel, p.Steps.Count - 1);
            var step = p.Steps[stepIndex];
            a.EscalationLevel++;
            foreach (var id in step.ChannelIds) if (channels.TryGetValue(id, out var ch) && await DeliverAsync(ch, a, a.EscalationLevel, step.Message, ct)) sent++;
            a.NextEscalationAt = NextAt(p, a.EscalationLevel, now);
        }
        await _db.SaveChangesAsync(ct);
        return sent;
    }

    public Task<string> TestAsync(NotificationChannel ch, CancellationToken ct = default)
        => _sender.SendAsync(ch, new NotificationMessage("RFID Platform test notification", $"This is a test message from channel '{ch.Name}' ({ch.Kind}).", Severity.Info, null, null, BaseUrl), ct);

    private async Task<bool> DeliverAsync(NotificationChannel ch, Alert alert, int level, string? stepMessage, CancellationToken ct)
    {
        var item = alert.ItemId.HasValue ? await _db.Items.Where(i => i.Id == alert.ItemId).Select(i => new { i.Name, i.Identifier }).FirstOrDefaultAsync(ct) : null;
        var loc = alert.LocationId.HasValue ? await _db.Locations.Where(l => l.Id == alert.LocationId).Select(l => l.Name).FirstOrDefaultAsync(ct) : null;
        var subject = $"[{alert.Severity}] {(level > 0 ? $"Escalation #{level}: " : "")}{alert.Message}";
        var body = string.Join("\n", new[]
        {
            stepMessage, alert.Message,
            item != null ? $"Item: {item.Name} ({item.Identifier})" : null,
            loc != null ? $"Location: {loc}" : null,
            $"Raised: {alert.RaisedAt:u} · Severity: {alert.Severity}" + (level > 0 ? $" · Escalation level {level}" : ""),
            BaseUrl != null ? $"Open: {BaseUrl.TrimEnd('/')}/alerts" : null,
        }.Where(l => !string.IsNullOrWhiteSpace(l)));
        var log = new NotificationLog { TenantId = alert.TenantId, ChannelId = ch.Id, AlertId = alert.Id, Subject = subject, EscalationLevel = level, SentAt = DateTime.UtcNow, Recipient = ch.Name };
        var message = new NotificationMessage(subject, body, alert.Severity, alert.Id, alert.ItemId, BaseUrl);
        if (_outbox != null)
        {
            log.Status = NotificationStatus.Queued;
            _outbox.Enqueue(OutboxKinds.Notification, new OutboxDispatcher.NotificationEnvelope(log.Id, ch.Id, message), ch.Id.ToString(), alert.TenantId == Guid.Empty ? null : alert.TenantId);
        }
        else
        {
            try { log.Recipient = await _sender.SendAsync(ch, message, ct); log.Status = NotificationStatus.Sent; }
            catch (Exception ex) { log.Status = NotificationStatus.Failed; log.Error = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message; }
        }
        _db.NotificationLogs.Add(log);
        return log.Status is NotificationStatus.Sent or NotificationStatus.Queued;
    }
}
