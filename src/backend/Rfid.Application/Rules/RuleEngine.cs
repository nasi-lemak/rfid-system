using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Rfid.Application.Contracts;
using Rfid.Domain;
using Rfid.Domain.Entities;

namespace Rfid.Application.Services;

/// <summary>Evaluation context handed to conditions. Fields are addressed with dotted paths.</summary>
public class RuleContext
{
    /// <summary>The triggering event; null when a schedule rule sweeps items.</summary>
    public ItemEvent? Event { get; init; }
    public required Item Item { get; init; }
    public ItemType? ItemType { get; init; }
    public Location? FromLocation { get; init; }
    public Location? ToLocation { get; init; }
    public Party? Party { get; init; }
}

public class RuleEngine
{
    private readonly IAppDb _db;
    private readonly ICurrentContext _ctx;
    private readonly ILivePublisher _live;
    private readonly IWebhookDispatcher _webhooks;
    private readonly NotificationService? _notifications;
    private readonly Platform.Outbox? _outbox;
    private List<Rule>? _cache;

    public RuleEngine(IAppDb db, ICurrentContext ctx, ILivePublisher live, IWebhookDispatcher webhooks, NotificationService? notifications = null, Platform.Outbox? outbox = null)
    {
        _db = db; _ctx = ctx; _live = live; _webhooks = webhooks; _notifications = notifications; _outbox = outbox;
    }

    /// <summary>Drops the per-request rule cache, e.g. after a template installed new rules in the same unit of work.</summary>
    public void InvalidateCache() => _cache = null;

    public async Task<List<Alert>> EvaluateAsync(RuleContext c, CancellationToken ct = default)
    {
        if (c.Event == null) throw new ArgumentException("Event rules need the triggering event", nameof(c));
        _cache ??= await _db.Rules.Where(r => r.Enabled).ToListAsync(ct);
        var alerts = new List<Alert>();
        foreach (var rule in _cache.Where(r => r.Kind == RuleKind.Event && r.Trigger == c.Event.Type))
        {
            if (!rule.Conditions.All(cond => Matches(cond, c))) continue;
            await ApplyAsync(rule, c, alerts, ct);
        }
        return alerts;
    }

    public record ScheduleRun(int RulesRun, int ItemsMatched, int AlertsRaised, int Skipped);

    /// <summary>
    /// Runs every due schedule rule over the tenant's live items. Alert-type actions are de-duplicated: an item with an
    /// open alert from the same rule is not alerted again, so an interval sweep never storms. Other actions apply each time
    /// the conditions match (RunOperation is expected to change the item so it stops matching, e.g. Dispose at max cycles).
    /// </summary>
    public async Task<ScheduleRun> EvaluateScheduledAsync(DateTime now, CancellationToken ct = default)
    {
        var due = await _db.Rules.Where(r => r.Enabled && r.Kind == RuleKind.Schedule && (r.LastRunAt == null || r.LastRunAt <= now.AddMinutes(-Math.Max(1, r.IntervalMinutes)))).ToListAsync(ct);
        if (due.Count == 0) return new ScheduleRun(0, 0, 0, 0);
        var alerts = new List<Alert>(); int matched = 0, skipped = 0;
        var openAlerts = (await _db.Alerts.Where(a => a.Status != AlertStatus.Closed && a.RuleId != null && a.ItemId != null).Select(a => new { a.RuleId, a.ItemId }).ToListAsync(ct)).Select(x => (x.RuleId!.Value, x.ItemId!.Value)).ToHashSet();
        const int page = 500;
        for (var skip = 0; ; skip += page)
        {
            var items = await _db.Items.Include(i => i.ItemType).Include(i => i.CurrentLocation).Include(i => i.CustodianParty).Where(i => i.Status != ItemStatus.Disposed).OrderBy(i => i.Id).Skip(skip).Take(page).ToListAsync(ct);
            if (items.Count == 0) break;
            foreach (var item in items)
            {
                var c = new RuleContext { Event = null, Item = item, ItemType = item.ItemType, ToLocation = item.CurrentLocation, Party = item.CustodianParty };
                foreach (var rule in due)
                {
                    if (!rule.Conditions.All(cond => Matches(cond, c))) continue;
                    matched++;
                    if (rule.Action is RuleAction.CreateAlert or RuleAction.Notify && openAlerts.Contains((rule.Id, item.Id))) { skipped++; continue; }
                    var before = alerts.Count;
                    await ApplyAsync(rule, c, alerts, ct);
                    if (alerts.Count > before) openAlerts.Add((rule.Id, item.Id));
                }
            }
            if (items.Count < page) break;
        }
        foreach (var r in due) r.LastRunAt = now;
        await _db.SaveChangesAsync(ct);
        return new ScheduleRun(due.Count, matched, alerts.Count, skipped);
    }

    private async Task ApplyAsync(Rule rule, RuleContext c, List<Alert> alerts, CancellationToken ct)
    {
        switch (rule.Action)
        {
            case RuleAction.CreateAlert:
            case RuleAction.Notify:
            {
                var message = Interpolate(rule.Params.TryGetValue("message", out var m) ? m?.ToString() : null, c)
                              ?? $"{rule.Name}: {c.Item.Name} ({c.Item.Identifier})";
                var alert = new Alert
                {
                    TenantId = _ctx.TenantId, RuleId = rule.Id, ItemId = c.Item.Id,
                    LocationId = c.Event?.ToLocationId ?? c.Item.CurrentLocationId,
                    Severity = rule.Severity, Message = message,
                };
                _db.Alerts.Add(alert);
                alerts.Add(alert);
                await _live.PublishAlertAsync(alert, ct);
                if (_notifications != null) await _notifications.OnAlertRaisedAsync(alert, rule, ct);
                break;
            }
            case RuleAction.SetState:
            {
                var target = rule.Params.TryGetValue("state", out var s) ? s?.ToString() : null;
                var lifecycle = c.ItemType?.Lifecycle;
                if (!string.IsNullOrEmpty(target) && lifecycle != null && !lifecycle.IsValidState(target)) break; // never write a state the type does not know
                if (!string.IsNullOrEmpty(target) && !string.Equals(c.Item.State, target, StringComparison.OrdinalIgnoreCase))
                {
                    var prev = c.Item.State;
                    c.Item.State = target;
                    _db.ItemEvents.Add(new ItemEvent
                    {
                        TenantId = _ctx.TenantId, ItemId = c.Item.Id, Type = ItemEventType.StateChanged,
                        FromState = prev, ToState = target, OperationId = c.Event?.OperationId,
                        Data = new() { ["rule"] = rule.Name }
                    });
                }
                break;
            }
            case RuleAction.Webhook:
            {
                var url = rule.Params.TryGetValue("url", out var u) ? u?.ToString() : null;
                if (!string.IsNullOrEmpty(url))
                    await _webhooks.SendAsync(url, new { rule = rule.Name, item = c.Item.Identifier, itemId = c.Item.Id, eventType = c.Event?.Type.ToString() ?? "Schedule", at = c.Event?.OccurredAt ?? DateTime.UtcNow }, ct);
                break;
            }
            case RuleAction.RunOperation:
            {
                // Runs after this unit of work commits (outbox), idempotent per message; never re-enters the processor mid-operation.
                var op = rule.Params.TryGetValue("operation", out var o) ? o?.ToString() : null;
                if (string.IsNullOrWhiteSpace(op) || _outbox == null) break;
                Guid? G(string key) => rule.Params.TryGetValue(key, out var v) && Guid.TryParse(v?.ToString(), out var g) ? g : null;
                string? S(string key) => rule.Params.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v?.ToString()) ? Interpolate(v!.ToString(), c) : null;
                _outbox.Enqueue(Platform.OutboxKinds.RunOperation, new Operations.RunOperationCommand(_ctx.TenantId, c.Item.Id, op, G("toLocationId"), G("partyId"), S("targetState"), S("reference") ?? $"rule:{rule.Name}", rule.Id, rule.Name));
                break;
            }
        }
    }

    public static bool Matches(RuleCondition cond, RuleContext c)
    {
        var actual = Resolve(cond.Field, c);
        var expected = Unwrap(cond.Value);
        switch (cond.Op.ToLowerInvariant())
        {
            case "exists": return actual != null;
            case "notexists": return actual == null;
            case "eq": return Compare(actual, expected) == 0;
            case "ne": return Compare(actual, expected) != 0;
            case "gt": return Compare(actual, expected) is > 0;
            case "gte": return Compare(actual, expected) is >= 0;
            case "lt": return Compare(actual, expected) is < 0 and not int.MinValue;
            case "lte": return Compare(actual, expected) is <= 0 and not int.MinValue;
            case "in": return ToList(expected).Any(v => Compare(actual, v) == 0);
            case "nin": return !ToList(expected).Any(v => Compare(actual, v) == 0);
            case "contains": return actual?.ToString()?.Contains(expected?.ToString() ?? "", StringComparison.OrdinalIgnoreCase) == true;
            default: return false;
        }
    }

    private static IEnumerable<object?> ToList(object? v) => v switch
    {
        null => Array.Empty<object?>(),
        IEnumerable<object?> e => e,
        string s => s.Split(',').Select(x => (object?)x.Trim()),
        _ => new[] { v }
    };

    private static object? Unwrap(object? v)
    {
        if (v is JsonElement je)
        {
            return je.ValueKind switch
            {
                JsonValueKind.Number => je.GetDouble(),
                JsonValueKind.String => je.GetString(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Array => je.EnumerateArray().Select(x => Unwrap(x)).ToList(),
                JsonValueKind.Null => null,
                _ => je.ToString()
            };
        }
        return v;
    }

    /// <summary>Returns int.MinValue when values are incomparable.</summary>
    private static int Compare(object? a, object? b)
    {
        a = Unwrap(a); b = Unwrap(b);
        if (a == null && b == null) return 0;
        if (a == null || b == null) return int.MinValue;
        if (TryNum(a, out var na) && TryNum(b, out var nb)) return na.CompareTo(nb);
        if (a is bool ba && bool.TryParse(b.ToString(), out var bb)) return ba.CompareTo(bb);
        if (a is DateTime da && DateTime.TryParse(b.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var db)) return da.CompareTo(db);
        if (a is DateOnly dao && DateOnly.TryParse(b.ToString(), CultureInfo.InvariantCulture, out var dbo)) return dao.CompareTo(dbo);
        return string.Compare(a.ToString(), b.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryNum(object v, out double d)
    {
        switch (v)
        {
            case double x: d = x; return true;
            case int x: d = x; return true;
            case long x: d = x; return true;
            case decimal x: d = (double)x; return true;
            case float x: d = x; return true;
            default:
                return double.TryParse(v.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out d) && v is not string { Length: 0 };
        }
    }

    public static object? Resolve(string field, RuleContext c)
    {
        var parts = field.Split('.', 2);
        var root = parts[0].ToLowerInvariant();
        var rest = parts.Length > 1 ? parts[1] : "";
        var now = DateTime.UtcNow;
        switch (root)
        {
            case "item":
                if (rest.StartsWith("attributes.", StringComparison.OrdinalIgnoreCase))
                    return c.Item.Attributes.TryGetValue(rest[11..], out var av) ? av : null;
                return rest.ToLowerInvariant() switch
                {
                    "state" => c.Item.State,
                    "status" => c.Item.Status.ToString(),
                    "cyclecount" => c.Item.CycleCount,
                    "quantity" => c.Item.Quantity,
                    "expirydate" => c.Item.ExpiryDate,
                    "daysuntilexpiry" => c.Item.ExpiryDate.HasValue ? (c.Item.ExpiryDate.Value.ToDateTime(TimeOnly.MinValue) - now).TotalDays : null,
                    "daysuntilinspection" => c.Item.NextInspectionDue.HasValue ? (c.Item.NextInspectionDue.Value - now).TotalDays : null,
                    "custodianpartyid" => c.Item.CustodianPartyId,
                    "hascustodian" => c.Item.CustodianPartyId != null,
                    "currentlocationid" => c.Item.CurrentLocationId,
                    "parentitemid" => c.Item.ParentItemId,
                    "duebackat" => c.Item.DueBackAt,
                    "overdue" => c.Item.DueBackAt.HasValue && c.Item.DueBackAt < now,
                    "lotnumber" => c.Item.LotNumber,
                    "identifier" => c.Item.Identifier,
                    "name" => c.Item.Name,
                    "maxcycles" => c.ItemType?.MaxCycles,
                    "reorderpoint" => c.ItemType?.ReorderPoint,
                    "cyclesremaining" => c.ItemType?.MaxCycles is int mc ? mc - c.Item.CycleCount : null,
                    "lastseenat" => c.Item.LastSeenAt,
                    "hourssinceseen" => c.Item.LastSeenAt.HasValue ? (now - c.Item.LastSeenAt.Value).TotalHours : null,
                    "dayssinceseen" => c.Item.LastSeenAt.HasValue ? (now - c.Item.LastSeenAt.Value).TotalDays : null,
                    "dayssinceinspection" => c.Item.LastInspectedAt.HasValue ? (now - c.Item.LastInspectedAt.Value).TotalDays : null,
                    "agedays" => c.Item.PurchasedAt.HasValue ? (now - c.Item.PurchasedAt.Value.ToDateTime(TimeOnly.MinValue)).TotalDays : null,
                    "daysoverdue" => c.Item.DueBackAt.HasValue && c.Item.DueBackAt < now ? (now - c.Item.DueBackAt.Value).TotalDays : null,
                    _ => null
                };
            case "itemtype":
                return rest.ToLowerInvariant() switch
                {
                    "code" => c.ItemType?.Code,
                    "name" => c.ItemType?.Name,
                    "category" => c.ItemType?.Category.ToString(),
                    "iscontainer" => c.ItemType?.IsContainer,
                    "maxcycles" => c.ItemType?.MaxCycles,
                    "reorderpoint" => c.ItemType?.ReorderPoint,
                    _ => null
                };
            case "tolocation": return LocationField(c.ToLocation, rest);
            case "fromlocation": return LocationField(c.FromLocation, rest);
            case "party":
                return rest.ToLowerInvariant() switch { "kind" => c.Party?.Kind.ToString(), "code" => c.Party?.Code, "name" => c.Party?.Name, _ => null };
            case "event":
                return rest.ToLowerInvariant() switch
                {
                    "type" => c.Event?.Type.ToString(),
                    "fromstate" => c.Event?.FromState,
                    "tostate" => c.Event?.ToState,
                    "haslocationchange" => c.Event != null && c.Event.FromLocationId != c.Event.ToLocationId,
                    _ => null
                };
            case "data":
                return c.Event != null && c.Event.Data.TryGetValue(rest, out var dv) ? dv : null;
            default: return null;
        }
    }

    private static object? LocationField(Location? l, string rest) => rest.ToLowerInvariant() switch
    {
        "kind" => l?.Kind.ToString(),
        "code" => l?.Code,
        "name" => l?.Name,
        "id" => l?.Id,
        "path" => l?.Path,
        _ => l?.Attributes.TryGetValue(rest, out var v) == true ? v : null
    };

    private static string? Interpolate(string? template, RuleContext c)
    {
        if (string.IsNullOrEmpty(template)) return null;
        return System.Text.RegularExpressions.Regex.Replace(template, @"\{([a-zA-Z0-9_.]+)\}",
            m => Resolve(m.Groups[1].Value, c)?.ToString() ?? "");
    }
}
