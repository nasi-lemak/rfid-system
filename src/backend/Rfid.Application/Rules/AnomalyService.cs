using Microsoft.EntityFrameworkCore;
using Rfid.Application.Contracts;
using Rfid.Domain;
using Rfid.Domain.Entities;

namespace Rfid.Application.Services;

/// <summary>
/// Statistical anomaly detection on read patterns. Baselines are built from the previous N days (per device / location /
/// item) and compared with the most recent window: read-rate spikes and drops, activity in normally quiet hours,
/// unknown-tag surges, items flapping between two locations and items moving far more than their type usually does.
/// Findings are de-duplicated while open and, above the alert threshold, raise a Warning alert.
/// </summary>
public class AnomalyService
{
    private readonly IAppDb _db; private readonly ICurrentContext _ctx; private readonly NotificationService? _notifications;
    public int BaselineDays { get; set; } = 14;
    public double ZThreshold { get; set; } = 3.0;
    public double AlertZ { get; set; } = 4.0;
    public int MinReads { get; set; } = 20;
    public AnomalyService(IAppDb db, ICurrentContext ctx, NotificationService? notifications = null) { _db = db; _ctx = ctx; _notifications = notifications; }

    public record Finding(AnomalyKind Kind, Guid? DeviceId, Guid? ItemId, Guid? LocationId, double Score, double Observed, double Expected, DateTime WindowStart, DateTime WindowEnd, string Message, Dictionary<string, object?> Details);

    private static (double mean, double std) Stats(IReadOnlyList<double> xs)
    {
        if (xs.Count == 0) return (0, 0);
        var mean = xs.Average(); var var_ = xs.Count > 1 ? xs.Sum(x => (x - mean) * (x - mean)) / (xs.Count - 1) : 0;
        return (mean, Math.Sqrt(var_));
    }
    /// <summary>z-score with a floor on the standard deviation so near-constant baselines don't explode.</summary>
    private static double Z(double observed, double mean, double std) => (observed - mean) / Math.Max(std, Math.Max(1, Math.Sqrt(Math.Max(mean, 1))));

    /// <summary>Runs every detector for the hour ending at <paramref name="now"/> and returns the findings (not persisted).</summary>
    public async Task<List<Finding>> DetectAsync(DateTime now, CancellationToken ct = default)
    {
        var windowEnd = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc);
        var windowStart = windowEnd.AddHours(-1);
        var baselineStart = windowStart.AddDays(-BaselineDays);
        var findings = new List<Finding>();
        var reads = await _db.TagReads.Where(r => r.ReadAt >= baselineStart && r.ReadAt < windowEnd).Select(r => new { r.DeviceId, r.LocationId, r.ItemId, r.ReadAt }).ToListAsync(ct);
        var devices = await _db.Devices.ToDictionaryAsync(d => d.Id, d => d.Name, ct);
        var locations = await _db.Locations.ToDictionaryAsync(l => l.Id, l => l.Name, ct);

        // 1–2. Read-rate spike/drop per device: this hour vs the same hour-of-day over the baseline days.
        foreach (var g in reads.Where(r => r.DeviceId != null).GroupBy(r => r.DeviceId!.Value))
        {
            var observed = g.Count(r => r.ReadAt >= windowStart);
            var sameHour = new List<double>();
            for (var d = 1; d <= BaselineDays; d++) { var s = windowStart.AddDays(-d); var e = windowEnd.AddDays(-d); sameHour.Add(g.Count(r => r.ReadAt >= s && r.ReadAt < e)); }
            var (mean, std) = Stats(sameHour);
            if (sameHour.Sum() < MinReads && observed < MinReads) continue; // not enough history to judge
            var z = Z(observed, mean, std);
            var name = devices.GetValueOrDefault(g.Key, "reader");
            if (z >= ZThreshold && observed >= MinReads) findings.Add(new(AnomalyKind.ReadRateSpike, g.Key, null, null, Math.Round(z, 1), observed, Math.Round(mean, 1), windowStart, windowEnd, $"{name}: {observed} reads in the last hour vs. a typical {mean:F0} (±{std:F0}) at this time of day", new() { ["sameHourHistory"] = sameHour }));
            else if (z <= -ZThreshold && mean >= MinReads) findings.Add(new(AnomalyKind.ReadRateDrop, g.Key, null, null, Math.Round(Math.Abs(z), 1), observed, Math.Round(mean, 1), windowStart, windowEnd, $"{name}: only {observed} reads in the last hour vs. a typical {mean:F0} at this time of day", new() { ["sameHourHistory"] = sameHour }));
        }

        // 3. Off-hours activity per location: hours that carry < 2% of the location's baseline reads are "quiet".
        foreach (var g in reads.Where(r => r.LocationId != null).GroupBy(r => r.LocationId!.Value))
        {
            var baseline = g.Where(r => r.ReadAt < windowStart).ToList(); if (baseline.Count < MinReads * 5) continue;
            var hour = windowStart.Hour; var share = baseline.Count(r => r.ReadAt.Hour == hour) / (double)baseline.Count;
            var observed = g.Count(r => r.ReadAt >= windowStart);
            if (share < 0.02 && observed >= MinReads)
            {
                var expected = baseline.Count(r => r.ReadAt.Hour == hour) / (double)BaselineDays;
                findings.Add(new(AnomalyKind.OffHoursActivity, null, null, g.Key, Math.Round(Math.Min(9.9, observed / Math.Max(1, expected)), 1), observed, Math.Round(expected, 1), windowStart, windowEnd, $"{locations.GetValueOrDefault(g.Key, "location")}: {observed} reads at {hour:00}:00 UTC, an hour that is normally quiet ({share:P1} of activity)", new() { ["quietHourShare"] = share }));
            }
        }

        // 4. Unknown-tag surge per device.
        foreach (var g in reads.Where(r => r.DeviceId != null && r.ItemId == null).GroupBy(r => r.DeviceId!.Value))
        {
            var observed = g.Count(r => r.ReadAt >= windowStart);
            var perHour = new List<double>(); for (var h = 1; h <= BaselineDays * 24; h++) { var s = windowStart.AddHours(-h); perHour.Add(g.Count(r => r.ReadAt >= s && r.ReadAt < s.AddHours(1))); }
            var (mean, std) = Stats(perHour); var z = Z(observed, mean, std);
            if (observed >= MinReads && z >= ZThreshold) findings.Add(new(AnomalyKind.UnknownTagSurge, g.Key, null, null, Math.Round(z, 1), observed, Math.Round(mean, 1), windowStart, windowEnd, $"{devices.GetValueOrDefault(g.Key, "reader")}: {observed} reads of unknown tags in the last hour (usually {mean:F1}/h) – foreign tags, stray inventory or a new supplier batch?", new()));
        }

        // 5–6. Movement anomalies from Moved events.
        var moves = await _db.ItemEvents.Where(e => e.Type == ItemEventType.Moved && e.OccurredAt >= baselineStart && e.OccurredAt < windowEnd).Select(e => new { e.ItemId, e.FromLocationId, e.ToLocationId, e.OccurredAt }).ToListAsync(ct);
        var itemNames = new Dictionary<Guid, (string name, Guid typeId)>();
        foreach (var g in moves.GroupBy(m => m.ItemId))
        {
            var recent = g.Where(m => m.OccurredAt >= windowEnd.AddHours(-24)).OrderBy(m => m.OccurredAt).ToList();
            if (recent.Count < 6) continue;
            // Flapping: A→B→A→B… between the same two locations.
            var pairs = recent.Select(m => new[] { m.FromLocationId, m.ToLocationId }.OrderBy(x => x).ToArray()).ToList();
            var top = pairs.GroupBy(p => $"{p[0]}|{p[1]}").OrderByDescending(x => x.Count()).First();
            if (top.Count() >= 6)
            {
                var (nm, _) = await ItemAsync(g.Key, itemNames, ct);
                var ids = top.Key.Split('|').Select(x => Guid.TryParse(x, out var id) ? id : (Guid?)null).ToList();
                findings.Add(new(AnomalyKind.ItemFlapping, null, g.Key, ids[1] ?? ids[0], Math.Round(top.Count() / 2.0, 1), top.Count(), 1, windowEnd.AddHours(-24), windowEnd, $"{nm} moved {top.Count()} times between {Name(locations, ids[0])} and {Name(locations, ids[1])} in 24 h – overlapping read zones or a tag on a moving fixture?", new() { ["moves24h"] = recent.Count }));
                continue;
            }
            // Excessive movement vs the item type's daily baseline.
            var (name, typeId) = await ItemAsync(g.Key, itemNames, ct);
            var typeItems = await _db.Items.Where(i => i.ItemTypeId == typeId).Select(i => i.Id).ToListAsync(ct);
            var dailyPerItem = new List<double>();
            for (var d = 1; d <= BaselineDays; d++) { var s = windowEnd.AddDays(-d - 1); var e = windowEnd.AddDays(-d); dailyPerItem.Add(moves.Count(m => typeItems.Contains(m.ItemId) && m.OccurredAt >= s && m.OccurredAt < e) / (double)Math.Max(1, typeItems.Count)); }
            var (mean, std) = Stats(dailyPerItem); var z = Z(recent.Count, mean, std);
            if (z >= ZThreshold) findings.Add(new(AnomalyKind.ExcessiveMovement, null, g.Key, recent[^1].ToLocationId, Math.Round(z, 1), recent.Count, Math.Round(mean, 1), windowEnd.AddHours(-24), windowEnd, $"{name} moved {recent.Count} times in 24 h; items of its type move {mean:F1} times a day", new()));
        }
        return findings;
    }

    private static string Name(Dictionary<Guid, string> names, Guid? id) => id.HasValue ? names.GetValueOrDefault(id.Value, "?") : "(none)";
    private async Task<(string name, Guid typeId)> ItemAsync(Guid id, Dictionary<Guid, (string, Guid)> cache, CancellationToken ct)
    {
        if (cache.TryGetValue(id, out var v)) return v;
        var it = await _db.Items.Where(i => i.Id == id).Select(i => new { i.Name, i.Identifier, i.ItemTypeId }).FirstOrDefaultAsync(ct);
        v = (it == null ? "item" : $"{it.Name} ({it.Identifier})", it?.ItemTypeId ?? Guid.Empty); cache[id] = v; return v;
    }

    /// <summary>Detects and persists: new anomalies are created, open ones for the same kind+subject are refreshed. Returns (new, updated).</summary>
    public async Task<(int created, int updated)> RunAsync(DateTime now, CancellationToken ct = default)
    {
        var findings = await DetectAsync(now, ct);
        var open = await _db.Anomalies.Where(a => a.Status == AnomalyStatus.Open).ToListAsync(ct);
        int created = 0, updated = 0;
        foreach (var f in findings)
        {
            var existing = open.FirstOrDefault(a => a.Kind == f.Kind && a.DeviceId == f.DeviceId && a.ItemId == f.ItemId && a.LocationId == f.LocationId && now - a.LastSeenAt < TimeSpan.FromHours(24));
            if (existing != null) { existing.LastSeenAt = now; existing.Occurrences++; existing.Score = Math.Max(existing.Score, f.Score); existing.Observed = f.Observed; existing.Expected = f.Expected; existing.WindowStart = f.WindowStart; existing.WindowEnd = f.WindowEnd; existing.Message = f.Message; updated++; continue; }
            var a = new Anomaly { TenantId = _ctx.TenantId, Kind = f.Kind, DeviceId = f.DeviceId, ItemId = f.ItemId, LocationId = f.LocationId, Score = f.Score, Observed = f.Observed, Expected = f.Expected, WindowStart = f.WindowStart, WindowEnd = f.WindowEnd, Message = f.Message, Details = f.Details, DetectedAt = now, LastSeenAt = now };
            if (f.Score >= AlertZ || f.Kind is AnomalyKind.ItemFlapping or AnomalyKind.OffHoursActivity)
            {
                var alert = new Alert { TenantId = _ctx.TenantId, ItemId = f.ItemId, LocationId = f.LocationId, DeviceId = f.DeviceId, Severity = Severity.Warning, Source = "anomaly", Message = "Anomaly: " + f.Message, RaisedAt = now };
                _db.Alerts.Add(alert); a.AlertId = alert.Id;
                if (_notifications != null) await _notifications.OnAlertRaisedAsync(alert, null, ct);
            }
            _db.Anomalies.Add(a); open.Add(a); created++;
        }
        await _db.SaveChangesAsync(ct);
        return (created, updated);
    }

    public record HourProfile(int Hour, double Baseline, int Today);
    /// <summary>Hour-of-day read profile for a device or location: baseline average per day vs today's counts (for the chart).</summary>
    public async Task<List<HourProfile>> ProfileAsync(Guid? deviceId, Guid? locationId, DateTime? now = null, CancellationToken ct = default)
    {
        var t = now ?? DateTime.UtcNow; var today = t.Date; var since = today.AddDays(-BaselineDays);
        var q = _db.TagReads.Where(r => r.ReadAt >= since && r.ReadAt < today.AddDays(1));
        if (deviceId.HasValue) q = q.Where(r => r.DeviceId == deviceId); if (locationId.HasValue) q = q.Where(r => r.LocationId == locationId);
        var rows = await q.GroupBy(r => new { Day = r.ReadAt.Date, r.ReadAt.Hour }).Select(g => new { g.Key.Day, g.Key.Hour, C = g.Count() }).ToListAsync(ct);
        return Enumerable.Range(0, 24).Select(h => new HourProfile(h, Math.Round(rows.Where(r => r.Hour == h && r.Day < today).Sum(r => r.C) / (double)BaselineDays, 1), rows.Where(r => r.Hour == h && r.Day == today).Sum(r => r.C))).ToList();
    }
}
