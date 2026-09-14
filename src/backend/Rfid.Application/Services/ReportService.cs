using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Rfid.Application.Contracts;
using Rfid.Domain;
using Rfid.Domain.Entities;

namespace Rfid.Application.Services;

public record ReportDefinition(string Code, string Name, string Description);

/// <summary>Operational reports as row sets (JSON or CSV). Every report is a plain query over the primitives, so it works for any vertical.</summary>
public class ReportService
{
    private readonly IAppDb _db;
    public ReportService(IAppDb db) => _db = db;

    public static readonly List<ReportDefinition> Catalog = new()
    {
        new("inventory", "Inventory by location", "Every active item with type, state, location, custodian, quantity and last seen."),
        new("missing", "Missing & not seen", "Items flagged missing or not seen for more than N days (param: days, default 7)."),
        new("custody", "Custody register", "Items currently issued to people, departments and customers with due dates."),
        new("overdue", "Overdue returns", "Issued/dispatched items past their due-back date."),
        new("expiry", "Expiry", "Lots and items expiring within N days (param: days, default 30) or already expired."),
        new("inspection", "Inspection due", "Items whose next inspection is due within N days (param: days, default 30) or overdue."),
        new("cycles", "Cycle life", "Cycle-tracked items (linen, kegs, trays, tyres) with cycles used and remaining."),
        new("depreciation", "Fixed asset register", "Cost, straight-line depreciation and net book value for items with cost and purchase date (ERP export)."),
        new("movements", "Movement history", "Item events in a date range (params: from, to ISO dates; type)."),
        new("stocktakes", "Stocktake accuracy", "Expected / found / missing / unexpected and accuracy % per stocktake."),
        new("reads", "Reader activity", "Reads per device per day for the last 14 days."),
        new("dwell", "Zone dwell", "Closed presence sessions: time spent per item per zone (param: days, default 7)."),
    };

    public async Task<(List<string> columns, List<object?[]> rows)> RunAsync(string code, IDictionary<string, string?> p, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        int Days(int def) => int.TryParse(p.TryGetValue("days", out var d) ? d : null, out var n) ? n : def;
        switch (code)
        {
            case "inventory":
            {
                var rows = await _db.Items.Include(i => i.ItemType).Include(i => i.CurrentLocation).Include(i => i.CustodianParty).Where(i => i.Status != ItemStatus.Disposed).OrderBy(i => i.CurrentLocation!.Name).ThenBy(i => i.Name).ToListAsync(ct);
                return (new() { "Identifier", "Name", "Type", "State", "Status", "Location", "Custodian", "Quantity", "Unit", "Lot", "Expiry", "LastSeen", "EPC" },
                    rows.Select(i => new object?[] { i.Identifier, i.Name, i.ItemType?.Name, i.State, i.Status.ToString(), i.CurrentLocation?.Name, i.CustodianParty?.Name, i.Quantity, i.Unit, i.LotNumber, i.ExpiryDate, i.LastSeenAt, i.Tags.FirstOrDefault()?.Epc }).ToList());
            }
            case "missing":
            {
                var since = now.AddDays(-Days(7));
                var rows = await _db.Items.Include(i => i.ItemType).Include(i => i.CurrentLocation).Where(i => i.Status == ItemStatus.Missing || i.LastSeenAt == null || i.LastSeenAt < since).Where(i => i.Status != ItemStatus.Disposed).OrderBy(i => i.LastSeenAt).ToListAsync(ct);
                return (new() { "Identifier", "Name", "Type", "Status", "LastLocation", "LastSeen", "DaysUnseen", "Cost" },
                    rows.Select(i => new object?[] { i.Identifier, i.Name, i.ItemType?.Name, i.Status.ToString(), i.CurrentLocation?.Name, i.LastSeenAt, i.LastSeenAt.HasValue ? Math.Round((now - i.LastSeenAt.Value).TotalDays, 1) : null, i.Cost }).ToList());
            }
            case "custody":
            {
                var rows = await _db.Items.Include(i => i.ItemType).Include(i => i.CustodianParty).Include(i => i.CurrentLocation).Where(i => i.CustodianPartyId != null && i.Status != ItemStatus.Disposed).OrderBy(i => i.CustodianParty!.Name).ToListAsync(ct);
                return (new() { "Custodian", "PartyKind", "Identifier", "Name", "Type", "State", "Location", "DueBack", "Overdue" },
                    rows.Select(i => new object?[] { i.CustodianParty?.Name, i.CustodianParty?.Kind.ToString(), i.Identifier, i.Name, i.ItemType?.Name, i.State, i.CurrentLocation?.Name, i.DueBackAt, i.DueBackAt.HasValue && i.DueBackAt < now }).ToList());
            }
            case "overdue":
            {
                var rows = await _db.Items.Include(i => i.ItemType).Include(i => i.CustodianParty).Where(i => i.DueBackAt != null && i.DueBackAt < now && i.Status != ItemStatus.Disposed).OrderBy(i => i.DueBackAt).ToListAsync(ct);
                return (new() { "Identifier", "Name", "Type", "Custodian", "DueBack", "DaysOverdue" }, rows.Select(i => new object?[] { i.Identifier, i.Name, i.ItemType?.Name, i.CustodianParty?.Name, i.DueBackAt, Math.Round((now - i.DueBackAt!.Value).TotalDays, 1) }).ToList());
            }
            case "expiry":
            {
                var limit = DateOnly.FromDateTime(now.AddDays(Days(30)));
                var rows = await _db.Items.Include(i => i.ItemType).Include(i => i.CurrentLocation).Where(i => i.ExpiryDate != null && i.ExpiryDate <= limit && i.Status != ItemStatus.Disposed).OrderBy(i => i.ExpiryDate).ToListAsync(ct);
                var today = DateOnly.FromDateTime(now);
                return (new() { "Identifier", "Name", "Type", "Lot", "Quantity", "Location", "Expiry", "DaysToExpiry", "Expired" }, rows.Select(i => new object?[] { i.Identifier, i.Name, i.ItemType?.Name, i.LotNumber, i.Quantity, i.CurrentLocation?.Name, i.ExpiryDate, i.ExpiryDate!.Value.DayNumber - today.DayNumber, i.ExpiryDate < today }).ToList());
            }
            case "inspection":
            {
                var limit = now.AddDays(Days(30));
                var rows = await _db.Items.Include(i => i.ItemType).Include(i => i.CurrentLocation).Where(i => i.NextInspectionDue != null && i.NextInspectionDue <= limit && i.Status != ItemStatus.Disposed).OrderBy(i => i.NextInspectionDue).ToListAsync(ct);
                return (new() { "Identifier", "Name", "Type", "Location", "LastInspected", "NextDue", "DaysToDue", "Overdue" }, rows.Select(i => new object?[] { i.Identifier, i.Name, i.ItemType?.Name, i.CurrentLocation?.Name, i.LastInspectedAt, i.NextInspectionDue, Math.Round((i.NextInspectionDue!.Value - now).TotalDays, 1), i.NextInspectionDue < now }).ToList());
            }
            case "cycles":
            {
                var rows = await _db.Items.Include(i => i.ItemType).Where(i => i.ItemType!.TracksCycles && i.Status != ItemStatus.Disposed).OrderByDescending(i => i.CycleCount).ToListAsync(ct);
                return (new() { "Identifier", "Name", "Type", "State", "Cycles", "MaxCycles", "Remaining", "PercentUsed" }, rows.Select(i => new object?[] { i.Identifier, i.Name, i.ItemType?.Name, i.State, i.CycleCount, i.ItemType?.MaxCycles, i.ItemType?.MaxCycles is int m ? m - i.CycleCount : null, i.ItemType?.MaxCycles is int mm && mm > 0 ? Math.Round(100.0 * i.CycleCount / mm, 1) : null }).ToList());
            }
            case "depreciation":
            {
                var rows = await _db.Items.Include(i => i.ItemType).Include(i => i.CurrentLocation).Include(i => i.CustodianParty).Where(i => i.Cost != null).OrderBy(i => i.ItemType!.Name).ThenBy(i => i.Identifier).ToListAsync(ct);
                return (new() { "Identifier", "Name", "Type", "Location", "Custodian", "Status", "Cost", "PurchasedAt", "UsefulLifeMonths", "MonthsInService", "MonthlyDepreciation", "AccumulatedDepreciation", "NetBookValue" },
                    rows.Select(i => { var d = Depreciate(i, now); return new object?[] { i.Identifier, i.Name, i.ItemType?.Name, i.CurrentLocation?.Name, i.CustodianParty?.Name, i.Status.ToString(), i.Cost, i.PurchasedAt, i.ItemType?.UsefulLifeMonths, d.months, d.monthly, d.accumulated, d.nbv }; }).ToList());
            }
            case "movements":
            {
                var from = DateTime.TryParse(p.TryGetValue("from", out var f) ? f : null, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var fd) ? fd : now.AddDays(-7);
                var to = DateTime.TryParse(p.TryGetValue("to", out var t) ? t : null, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var td) ? td : now;
                var q = _db.ItemEvents.Where(e => e.OccurredAt >= from && e.OccurredAt <= to);
                if (p.TryGetValue("type", out var ty) && Enum.TryParse<ItemEventType>(ty, true, out var et)) q = q.Where(e => e.Type == et);
                var events = await q.OrderByDescending(e => e.OccurredAt).Take(5000).ToListAsync(ct);
                var itemIds = events.Select(e => e.ItemId).Distinct().ToList();
                var items = await _db.Items.Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
                var locs = await _db.Locations.ToDictionaryAsync(l => l.Id, l => l.Name, ct);
                var parties = await _db.Parties.ToDictionaryAsync(x => x.Id, x => x.Name, ct);
                return (new() { "OccurredAt", "Type", "Identifier", "Name", "From", "To", "FromParty", "ToParty", "FromState", "ToState", "Data" },
                    events.Select(e => new object?[] { e.OccurredAt, e.Type.ToString(), items.GetValueOrDefault(e.ItemId)?.Identifier, items.GetValueOrDefault(e.ItemId)?.Name, e.FromLocationId.HasValue ? locs.GetValueOrDefault(e.FromLocationId.Value) : null, e.ToLocationId.HasValue ? locs.GetValueOrDefault(e.ToLocationId.Value) : null, e.FromPartyId.HasValue ? parties.GetValueOrDefault(e.FromPartyId.Value) : null, e.ToPartyId.HasValue ? parties.GetValueOrDefault(e.ToPartyId.Value) : null, e.FromState, e.ToState, string.Join("; ", e.Data.Select(kv => $"{kv.Key}={kv.Value}")) }).ToList());
            }
            case "stocktakes":
            {
                var sts = await _db.Stocktakes.OrderByDescending(s => s.StartedAt).Take(500).ToListAsync(ct);
                var locs = await _db.Locations.ToDictionaryAsync(l => l.Id, l => l.Name, ct);
                return (new() { "Name", "Location", "Status", "StartedAt", "CompletedAt", "Expected", "Found", "Missing", "Unexpected", "AccuracyPercent" },
                    sts.Select(s => new object?[] { s.Name, locs.GetValueOrDefault(s.LocationId), s.Status.ToString(), s.StartedAt, s.CompletedAt, s.ExpectedCount, s.FoundCount, s.MissingCount, s.UnexpectedCount, s.ExpectedCount > 0 ? Math.Round(100.0 * s.FoundCount / s.ExpectedCount, 1) : null }).ToList());
            }
            case "reads":
            {
                var since = now.AddDays(-14);
                var rows = await _db.TagReads.Where(r => r.ReadAt >= since).GroupBy(r => new { r.DeviceId, Day = r.ReadAt.Date }).Select(g => new { g.Key.DeviceId, g.Key.Day, Count = g.Count(), Unique = g.Select(r => r.Epc).Distinct().Count() }).OrderBy(x => x.Day).ToListAsync(ct);
                var devs = await _db.Devices.ToDictionaryAsync(d => d.Id, d => d.Name, ct);
                return (new() { "Day", "Device", "Reads", "UniqueTags" }, rows.Select(r => new object?[] { r.Day, r.DeviceId.HasValue ? devs.GetValueOrDefault(r.DeviceId.Value) : "(handheld/manual)", r.Count, r.Unique }).ToList());
            }
            case "dwell":
            {
                var since = now.AddDays(-Days(7));
                var sessions = await _db.PresenceSessions.Where(s => s.EnteredAt >= since).OrderByDescending(s => s.EnteredAt).Take(5000).ToListAsync(ct);
                var itemIds = sessions.Select(s => s.ItemId).Distinct().ToList();
                var items = await _db.Items.Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
                var locs = await _db.Locations.ToDictionaryAsync(l => l.Id, l => l.Name, ct);
                return (new() { "Identifier", "Name", "Zone", "EnteredAt", "ExitedAt", "DwellMinutes", "Reads", "PeakRssi" }, sessions.Select(s => new object?[] { items.GetValueOrDefault(s.ItemId)?.Identifier, items.GetValueOrDefault(s.ItemId)?.Name, locs.GetValueOrDefault(s.LocationId), s.EnteredAt, s.ExitedAt, Math.Round(((s.ExitedAt ?? now) - s.EnteredAt).TotalMinutes, 1), s.ReadCount, s.PeakRssi }).ToList());
            }
            default: throw new NotFoundException($"Report {code}");
        }
    }

    /// <summary>Straight-line depreciation from cost, purchase date and the item type's useful life (default 60 months).</summary>
    public static (int months, decimal monthly, decimal accumulated, decimal nbv) Depreciate(Item i, DateTime now)
    {
        if (i.Cost is not decimal cost) return (0, 0, 0, 0);
        var life = i.ItemType?.UsefulLifeMonths ?? 60;
        var purchased = i.PurchasedAt?.ToDateTime(TimeOnly.MinValue) ?? i.CreatedAt;
        var months = Math.Max(0, (now.Year - purchased.Year) * 12 + now.Month - purchased.Month);
        var monthly = life > 0 ? Math.Round(cost / life, 2) : 0;
        var accumulated = Math.Min(cost, monthly * Math.Min(months, life));
        return (months, monthly, accumulated, cost - accumulated);
    }

    public static string ToCsv(List<string> columns, List<object?[]> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", columns.Select(Csv)));
        foreach (var r in rows) sb.AppendLine(string.Join(",", r.Select(v => Csv(Format(v)))));
        return sb.ToString();
    }
    private static string Format(object? v) => v switch { null => "", DateTime d => d.ToString("yyyy-MM-ddTHH:mm:ssZ"), DateOnly d => d.ToString("yyyy-MM-dd"), bool b => b ? "true" : "false", IFormattable f => f.ToString(null, CultureInfo.InvariantCulture), _ => v.ToString() ?? "" };
    private static string Csv(string s) => s.Contains(',') || s.Contains('"') || s.Contains('\n') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
}
