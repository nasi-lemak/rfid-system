using Microsoft.EntityFrameworkCore;
using Rfid.Application.Contracts;
using Rfid.Domain;
using Rfid.Domain.Entities;

namespace Rfid.Application.Services;

/// <summary>
/// Predictive maintenance: a transparent risk score (0–100) per item from cycle wear, inspection failures, overdue
/// inspections, service interval drift, usage intensity, age and open alerts, plus a predicted service date from the
/// dominant driver. Forecasts are snapshotted daily; High-risk items raise a warning alert once.
/// </summary>
public class MaintenanceService
{
    private readonly IAppDb _db; private readonly ICurrentContext _ctx; private readonly NotificationService? _notifications;
    public double AlertRisk { get; set; } = 70;
    public MaintenanceService(IAppDb db, ICurrentContext ctx, NotificationService? notifications = null) { _db = db; _ctx = ctx; _notifications = notifications; }

    public record Factor(string Name, double Weight, double Value, string Detail);
    public record Prediction(Guid ItemId, string Item, string Identifier, string ItemType, double Risk, string Level, DateTime? PredictedServiceAt, string? PredictedBy, List<Factor> Factors, string? State, DateTime? LastInspectedAt, DateTime? LastMaintainedAt, int CycleCount, int? MaxCycles);

    public static string LevelOf(double risk) => risk >= 70 ? "High" : risk >= 40 ? "Medium" : "Low";

    /// <summary>Scores every active item of types that have maintenance signals (MaxCycles, inspections, or a maintenance history).</summary>
    public async Task<List<Prediction>> PredictAsync(DateTime now, Guid? itemTypeId = null, Guid? itemId = null, CancellationToken ct = default)
    {
        var q = _db.Items.Include(i => i.ItemType).Where(i => i.Status == ItemStatus.Active);
        if (itemTypeId.HasValue) q = q.Where(i => i.ItemTypeId == itemTypeId); if (itemId.HasValue) q = q.Where(i => i.Id == itemId);
        var items = await q.ToListAsync(ct);
        var ids = items.Select(i => i.Id).ToList();
        var since = now.AddDays(-365);
        var events = await _db.ItemEvents.Where(e => ids.Contains(e.ItemId) && e.OccurredAt >= since && (e.Type == ItemEventType.Inspected || e.Type == ItemEventType.Maintained || e.Type == ItemEventType.Moved || e.Type == ItemEventType.CustodyChanged || e.Type == ItemEventType.StateChanged)).Select(e => new { e.ItemId, e.Type, e.OccurredAt, e.Data }).ToListAsync(ct);
        var alerts = await _db.Alerts.Where(a => a.ItemId != null && ids.Contains(a.ItemId.Value) && a.Status != AlertStatus.Closed).GroupBy(a => a.ItemId!.Value).Select(g => new { g.Key, C = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.C, ct);
        var byItem = events.GroupBy(e => e.ItemId).ToDictionary(g => g.Key, g => g.ToList());
        // Type-level usage baseline: moves per item per 30 days.
        var typeUsage = items.GroupBy(i => i.ItemTypeId).ToDictionary(g => g.Key, g => Math.Max(0.1, g.Average(i => byItem.TryGetValue(i.Id, out var ev) ? ev.Count(e => e.Type is ItemEventType.Moved or ItemEventType.CustodyChanged && e.OccurredAt >= now.AddDays(-30)) : 0)));
        var result = new List<Prediction>();
        foreach (var item in items)
        {
            var t = item.ItemType!; var ev = byItem.GetValueOrDefault(item.Id, new());
            var hasSignals = t.MaxCycles.HasValue || t.RequiresInspection || t.InspectionIntervalDays.HasValue || ev.Any(e => e.Type is ItemEventType.Maintained or ItemEventType.Inspected);
            if (!hasSignals && !itemId.HasValue) continue;
            var factors = new List<Factor>(); DateTime? predicted = null; string? predictedBy = null;
            // 1. Cycle wear (weight 30): fraction of MaxCycles consumed.
            if (t.MaxCycles is > 0)
            {
                var frac = Math.Min(1.5, item.CycleCount / (double)t.MaxCycles.Value);
                factors.Add(new("Cycle wear", 30, Math.Min(1, frac), $"{item.CycleCount} of {t.MaxCycles} cycles"));
                var recentCycles = ev.Count(e => e.Type == ItemEventType.StateChanged && e.OccurredAt >= now.AddDays(-90));
                var perDay = recentCycles / 90.0;
                if (perDay > 0 && item.CycleCount < t.MaxCycles) { var d = now.AddDays((t.MaxCycles.Value - item.CycleCount) / perDay); if (predicted == null || d < predicted) { predicted = d; predictedBy = "cycle limit at current usage"; } }
                else if (item.CycleCount >= t.MaxCycles) { predicted = now; predictedBy = "cycle limit reached"; }
            }
            // 2. Inspection outcomes (weight 25): failures in the last 5 inspections, weighted to the most recent.
            var inspections = ev.Where(e => e.Type == ItemEventType.Inspected).OrderByDescending(e => e.OccurredAt).Take(5).ToList();
            if (inspections.Count > 0)
            {
                var fails = inspections.Select((e, i) => (fail: IsFail(e.Data), w: 1.0 / (i + 1))).ToList();
                var score = fails.Sum(f => f.fail ? f.w : 0) / fails.Sum(f => f.w);
                factors.Add(new("Inspection failures", 25, score, $"{fails.Count(f => f.fail)} of the last {inspections.Count} inspections failed"));
            }
            // 3. Overdue inspection (weight 15).
            if (item.NextInspectionDue.HasValue)
            {
                var overdueDays = (now - item.NextInspectionDue.Value).TotalDays;
                var v = overdueDays <= 0 ? Math.Max(0, 1 + overdueDays / Math.Max(1, t.InspectionIntervalDays ?? 30)) * 0.5 : Math.Min(1, 0.5 + overdueDays / 30);
                factors.Add(new("Inspection due", 15, v, overdueDays > 0 ? $"{overdueDays:F0} days overdue" : $"due in {-overdueDays:F0} days"));
                if (overdueDays <= 0 && (predicted == null || item.NextInspectionDue < predicted)) { predicted = item.NextInspectionDue; predictedBy = "next inspection"; }
                else if (overdueDays > 0) { predicted = now; predictedBy = "inspection overdue"; }
            }
            // 4. Service interval drift (weight 15): time since last maintenance vs the item's own mean interval.
            var maint = ev.Where(e => e.Type == ItemEventType.Maintained).OrderBy(e => e.OccurredAt).Select(e => e.OccurredAt).ToList();
            if (maint.Count >= 2)
            {
                var intervals = maint.Zip(maint.Skip(1), (a, b) => (b - a).TotalDays).ToList(); var mean = Math.Max(1, intervals.Average());
                var sinceLast = (now - maint[^1]).TotalDays; var ratio = sinceLast / mean;
                factors.Add(new("Service interval", 15, Math.Min(1, ratio), $"{sinceLast:F0} days since service, usual interval {mean:F0} days"));
                var d = maint[^1].AddDays(mean); if (predicted == null || d < predicted) { predicted = d; predictedBy = "usual service interval"; }
            }
            else if (maint.Count == 1 && t.InspectionIntervalDays is > 0) { var sinceLast = (now - maint[0]).TotalDays; factors.Add(new("Service interval", 15, Math.Min(1, sinceLast / t.InspectionIntervalDays.Value), $"{sinceLast:F0} days since the only recorded service")); }
            // 5. Usage intensity (weight 10): this item's moves vs its type over 30 days.
            var moves30 = ev.Count(e => e.Type is ItemEventType.Moved or ItemEventType.CustodyChanged && e.OccurredAt >= now.AddDays(-30));
            var rel = moves30 / typeUsage[item.ItemTypeId];
            factors.Add(new("Usage intensity", 10, Math.Min(1, rel / 3), $"{moves30} moves in 30 days ({rel:F1}× the type average)"));
            // 6. Age (weight 5): years since purchase, 5+ years = max.
            if (item.PurchasedAt.HasValue) { var years = (now - item.PurchasedAt.Value.ToDateTime(TimeOnly.MinValue)).TotalDays / 365; factors.Add(new("Age", 5, Math.Min(1, years / 5), $"{years:F1} years")); }
            // 7. Open alerts (weight 10).
            var open = alerts.GetValueOrDefault(item.Id); if (open > 0) factors.Add(new("Open alerts", 10, Math.Min(1, open / 2.0), $"{open} open alert(s)"));
            var totalW = factors.Sum(f => f.Weight);
            var risk = totalW == 0 ? 0 : Math.Round(100 * factors.Sum(f => f.Weight * f.Value) / totalW, 1);
            // Failures and reached limits dominate: floor the score.
            if (factors.Any(f => f.Name == "Inspection failures" && f.Value >= 0.6)) risk = Math.Max(risk, 65);
            if (t.MaxCycles is > 0 && item.CycleCount >= t.MaxCycles) risk = Math.Max(risk, 80);
            result.Add(new Prediction(item.Id, item.Name, item.Identifier, t.Name, risk, LevelOf(risk), predicted, predictedBy, factors, item.State, item.LastInspectedAt, maint.LastOrDefault() == default ? null : maint.Last(), item.CycleCount, t.MaxCycles));
        }
        return result.OrderByDescending(r => r.Risk).ToList();
    }

    private static bool IsFail(Dictionary<string, object?> data) => data.TryGetValue("result", out var r) && r?.ToString() is { } s && (s.Contains("fail", StringComparison.OrdinalIgnoreCase) || s.Contains("reject", StringComparison.OrdinalIgnoreCase) || s.Contains("defect", StringComparison.OrdinalIgnoreCase) || s.Contains("quarantine", StringComparison.OrdinalIgnoreCase));

    /// <summary>Snapshots forecasts and raises one warning alert per item that crosses the High threshold. Returns (forecasts, alerts).</summary>
    public async Task<(int forecasts, int alerts)> RunAsync(DateTime now, CancellationToken ct = default)
    {
        var preds = await PredictAsync(now, null, null, ct);
        var previous = await _db.MaintenanceForecasts.Where(f => f.ComputedAt >= now.AddDays(-2)).ToListAsync(ct);
        var open = await _db.Alerts.Where(a => a.Source == "maintenance" && a.Status != AlertStatus.Closed && a.ItemId != null).Select(a => a.ItemId!.Value).ToListAsync(ct);
        var alerts = 0;
        foreach (var p in preds)
        {
            var f = new MaintenanceForecast { TenantId = _ctx.TenantId, ItemId = p.ItemId, Risk = p.Risk, Level = p.Level, PredictedServiceAt = p.PredictedServiceAt, PredictedBy = p.PredictedBy, ComputedAt = now, Factors = p.Factors.ToDictionary(x => x.Name, x => (object?)new { x.Weight, x.Value, x.Detail }) };
            if (p.Risk >= AlertRisk && !open.Contains(p.ItemId))
            {
                var alert = new Alert { TenantId = _ctx.TenantId, ItemId = p.ItemId, Severity = Severity.Warning, Source = "maintenance", RaisedAt = now, Message = $"Maintenance risk {p.Risk:F0}/100 for {p.Item} ({p.Identifier}): {string.Join(", ", p.Factors.OrderByDescending(x => x.Weight * x.Value).Take(2).Select(x => x.Detail))}" };
                _db.Alerts.Add(alert); f.AlertId = alert.Id; alerts++; open.Add(p.ItemId);
                if (_notifications != null) await _notifications.OnAlertRaisedAsync(alert, null, ct);
            }
            _db.MaintenanceForecasts.Add(f);
        }
        // Keep one snapshot per item per day.
        var today = now.Date;
        _db.MaintenanceForecasts.RemoveRange(previous.Where(x => x.ComputedAt.Date == today));
        await _db.SaveChangesAsync(ct);
        return (preds.Count, alerts);
    }

    public record TrendPoint(DateTime At, double Risk);
    public async Task<List<TrendPoint>> TrendAsync(Guid itemId, int days = 90, CancellationToken ct = default)
        => await _db.MaintenanceForecasts.Where(f => f.ItemId == itemId && f.ComputedAt >= DateTime.UtcNow.AddDays(-days)).OrderBy(f => f.ComputedAt).Select(f => new TrendPoint(f.ComputedAt, f.Risk)).ToListAsync(ct);
}
