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
    public required ItemEvent Event { get; init; }
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
    private List<Rule>? _cache;

    public RuleEngine(IAppDb db, ICurrentContext ctx, ILivePublisher live, IWebhookDispatcher webhooks)
    {
        _db = db; _ctx = ctx; _live = live; _webhooks = webhooks;
    }

    public async Task<List<Alert>> EvaluateAsync(RuleContext c, CancellationToken ct = default)
    {
        _cache ??= await _db.Rules.Where(r => r.Enabled).ToListAsync(ct);
        var alerts = new List<Alert>();
        foreach (var rule in _cache.Where(r => r.Trigger == c.Event.Type))
        {
            if (!rule.Conditions.All(cond => Matches(cond, c))) continue;
            switch (rule.Action)
            {
                case RuleAction.CreateAlert:
                {
                    var message = Interpolate(rule.Params.TryGetValue("message", out var m) ? m?.ToString() : null, c)
                                  ?? $"{rule.Name}: {c.Item.Name} ({c.Item.Identifier})";
                    var alert = new Alert
                    {
                        TenantId = _ctx.TenantId, RuleId = rule.Id, ItemId = c.Item.Id,
                        LocationId = c.Event.ToLocationId ?? c.Item.CurrentLocationId,
                        Severity = rule.Severity, Message = message,
                    };
                    _db.Alerts.Add(alert);
                    alerts.Add(alert);
                    await _live.PublishAlertAsync(alert, ct);
                    break;
                }
                case RuleAction.SetState:
                {
                    var target = rule.Params.TryGetValue("state", out var s) ? s?.ToString() : null;
                    if (!string.IsNullOrEmpty(target) && !string.Equals(c.Item.State, target, StringComparison.OrdinalIgnoreCase))
                    {
                        var prev = c.Item.State;
                        c.Item.State = target;
                        _db.ItemEvents.Add(new ItemEvent
                        {
                            TenantId = _ctx.TenantId, ItemId = c.Item.Id, Type = ItemEventType.StateChanged,
                            FromState = prev, ToState = target, OperationId = c.Event.OperationId,
                            Data = new() { ["rule"] = rule.Name }
                        });
                    }
                    break;
                }
                case RuleAction.Webhook:
                {
                    var url = rule.Params.TryGetValue("url", out var u) ? u?.ToString() : null;
                    if (!string.IsNullOrEmpty(url))
                        await _webhooks.SendAsync(url, new { rule = rule.Name, item = c.Item.Identifier, itemId = c.Item.Id, eventType = c.Event.Type.ToString(), at = c.Event.OccurredAt }, ct);
                    break;
                }
            }
        }
        return alerts;
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
                    "type" => c.Event.Type.ToString(),
                    "fromstate" => c.Event.FromState,
                    "tostate" => c.Event.ToState,
                    "haslocationchange" => c.Event.FromLocationId != c.Event.ToLocationId,
                    _ => null
                };
            case "data":
                return c.Event.Data.TryGetValue(rest, out var dv) ? dv : null;
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
