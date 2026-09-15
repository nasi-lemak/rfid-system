using Microsoft.EntityFrameworkCore;
using Rfid.Application.Contracts;
using Rfid.Domain;
using Rfid.Domain.Entities;

namespace Rfid.Application.Services;

/// <summary>Long-term analytics over the operational tables (trend, utilisation, dwell, inventory accuracy, alert response, uptime).</summary>
public class AnalyticsService
{
    private readonly IAppDb _db;
    public AnalyticsService(IAppDb db) => _db = db;

    public record Bucket(DateTime Start, int Count);
    public record Series(string Metric, string BucketSize, List<Bucket> Buckets, int Total);

    public async Task<Series> TrendAsync(string metric, int days, string bucket = "day", DateTime? now = null, CancellationToken ct = default)
    {
        var t = now ?? DateTime.UtcNow; var since = t.Date.AddDays(-(days - 1));
        List<(DateTime day, int count)> rows = metric switch
        {
            "reads" => (await _db.TagReads.Where(r => r.ReadAt >= since).GroupBy(r => r.ReadAt.Date).Select(g => new { g.Key, C = g.Count() }).ToListAsync(ct)).Select(x => (x.Key, x.C)).ToList(),
            "operations" => (await _db.Operations.Where(o => o.StartedAt >= since).GroupBy(o => o.StartedAt.Date).Select(g => new { g.Key, C = g.Count() }).ToListAsync(ct)).Select(x => (x.Key, x.C)).ToList(),
            "events" => (await _db.ItemEvents.Where(e => e.OccurredAt >= since).GroupBy(e => e.OccurredAt.Date).Select(g => new { g.Key, C = g.Count() }).ToListAsync(ct)).Select(x => (x.Key, x.C)).ToList(),
            "alerts" => (await _db.Alerts.Where(a => a.RaisedAt >= since).GroupBy(a => a.RaisedAt.Date).Select(g => new { g.Key, C = g.Count() }).ToListAsync(ct)).Select(x => (x.Key, x.C)).ToList(),
            "moves" => (await _db.ItemEvents.Where(e => e.OccurredAt >= since && e.Type == ItemEventType.Moved).GroupBy(e => e.OccurredAt.Date).Select(g => new { g.Key, C = g.Count() }).ToListAsync(ct)).Select(x => (x.Key, x.C)).ToList(),
            "gps" => (await _db.GpsFixes.Where(f => f.At >= since).GroupBy(f => f.At.Date).Select(g => new { g.Key, C = g.Count() }).ToListAsync(ct)).Select(x => (x.Key, x.C)).ToList(),
            "notifications" => (await _db.NotificationLogs.Where(f => f.SentAt >= since).GroupBy(f => f.SentAt.Date).Select(g => new { g.Key, C = g.Count() }).ToListAsync(ct)).Select(x => (x.Key, x.C)).ToList(),
            _ => throw new DomainException($"Unknown metric '{metric}'"),
        };
        var buckets = new List<Bucket>();
        if (bucket == "week")
        {
            var start = since.AddDays(-(int)since.DayOfWeek);
            for (var d = start; d <= t.Date; d = d.AddDays(7)) { var end = d.AddDays(7); buckets.Add(new Bucket(d, rows.Where(r => r.day >= d && r.day < end).Sum(r => r.count))); }
        }
        else for (var d = since; d <= t.Date; d = d.AddDays(1)) buckets.Add(new Bucket(d, rows.Where(r => r.day == d).Sum(r => r.count)));
        return new Series(metric, bucket, buckets, rows.Sum(r => r.count));
    }

    public record UtilizationRow(Guid ItemTypeId, string ItemType, int Items, int Active, int InCustody, int MovedInWindow, int EventsInWindow, double AvgCycles, double UtilizationPercent);
    /// <summary>Per item type: how many items were touched (any event) in the window, custody share and average cycles.</summary>
    public async Task<List<UtilizationRow>> UtilizationAsync(int days, DateTime? now = null, CancellationToken ct = default)
    {
        var t = now ?? DateTime.UtcNow; var since = t.AddDays(-days);
        var items = await _db.Items.Where(i => i.Status != ItemStatus.Disposed).Select(i => new { i.Id, i.ItemTypeId, i.Status, i.CustodianPartyId, i.CycleCount }).ToListAsync(ct);
        var types = await _db.ItemTypes.ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var touched = await _db.ItemEvents.Where(e => e.OccurredAt >= since).GroupBy(e => e.ItemId).Select(g => new { g.Key, Events = g.Count(), Moves = g.Count(e => e.Type == ItemEventType.Moved) }).ToDictionaryAsync(x => x.Key, ct);
        return items.GroupBy(i => i.ItemTypeId).Select(g =>
        {
            var ids = g.Select(i => i.Id).ToList();
            var withEvents = ids.Count(id => touched.ContainsKey(id));
            return new UtilizationRow(g.Key, types.GetValueOrDefault(g.Key, "?"), g.Count(), g.Count(i => i.Status == ItemStatus.Active), g.Count(i => i.CustodianPartyId != null),
                ids.Sum(id => touched.TryGetValue(id, out var x) ? x.Moves : 0), ids.Sum(id => touched.TryGetValue(id, out var x) ? x.Events : 0), Math.Round(g.Average(i => (double)i.CycleCount), 1), Math.Round(100.0 * withEvents / g.Count(), 1));
        }).OrderByDescending(r => r.Items).ToList();
    }

    public record DwellRow(Guid LocationId, string Location, string Kind, int Sessions, int DistinctItems, double AvgMinutes, double MaxMinutes, double P90Minutes);
    public async Task<List<DwellRow>> DwellAsync(int days, DateTime? now = null, CancellationToken ct = default)
    {
        var t = now ?? DateTime.UtcNow; var since = t.AddDays(-days);
        var sessions = await _db.PresenceSessions.Where(s => s.EnteredAt >= since).Select(s => new { s.LocationId, s.ItemId, s.EnteredAt, End = s.ExitedAt ?? s.LastSeenAt }).ToListAsync(ct);
        var locs = await _db.Locations.ToDictionaryAsync(l => l.Id, ct);
        return sessions.GroupBy(s => s.LocationId).Select(g =>
        {
            var mins = g.Select(s => Math.Max(0, (s.End - s.EnteredAt).TotalMinutes)).OrderBy(x => x).ToList();
            var p90 = mins[(int)Math.Min(mins.Count - 1, Math.Floor(mins.Count * 0.9))];
            var l = locs.GetValueOrDefault(g.Key);
            return new DwellRow(g.Key, l?.Name ?? "?", l?.Kind.ToString() ?? "", g.Count(), g.Select(s => s.ItemId).Distinct().Count(), Math.Round(mins.Average(), 1), Math.Round(mins.Max(), 1), Math.Round(p90, 1));
        }).OrderByDescending(r => r.Sessions).ToList();
    }

    public record AccuracyRow(Guid Id, string Name, string Location, DateTime At, int Expected, int Found, int Missing, int Unexpected, double? Accuracy);
    public record AccuracyReport(double? OverallAccuracy, int Stocktakes, List<AccuracyRow> Rows);
    public async Task<AccuracyReport> AccuracyAsync(int days, DateTime? now = null, CancellationToken ct = default)
    {
        var t = now ?? DateTime.UtcNow; var since = t.AddDays(-days);
        var sts = await _db.Stocktakes.Where(s => (s.Status == StocktakeStatus.Reconciled || s.Status == StocktakeStatus.Applied) && (s.CompletedAt ?? s.StartedAt) >= since).OrderBy(s => s.CompletedAt ?? s.StartedAt).ToListAsync(ct);
        var locs = await _db.Locations.Where(l => sts.Select(s => s.LocationId).Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.Name, ct);
        var rows = sts.Select(s => new AccuracyRow(s.Id, s.Name, locs.GetValueOrDefault(s.LocationId, "?"), s.CompletedAt ?? s.StartedAt, s.ExpectedCount, s.FoundCount, s.MissingCount, s.UnexpectedCount, s.ExpectedCount == 0 ? null : Math.Round(100.0 * s.FoundCount / s.ExpectedCount, 1))).ToList();
        var exp = sts.Sum(s => s.ExpectedCount);
        return new AccuracyReport(exp == 0 ? null : Math.Round(100.0 * sts.Sum(s => s.FoundCount) / exp, 1), sts.Count, rows);
    }

    public record AlertResponseRow(string Severity, int Raised, int Acknowledged, int Closed, int OpenNow, double? AvgMinutesToAck, double? AvgMinutesToClose, int Escalated);
    public async Task<List<AlertResponseRow>> AlertResponseAsync(int days, DateTime? now = null, CancellationToken ct = default)
    {
        var t = now ?? DateTime.UtcNow; var since = t.AddDays(-days);
        var alerts = await _db.Alerts.Where(a => a.RaisedAt >= since).Select(a => new { a.Severity, a.Status, a.RaisedAt, a.AcknowledgedAt, a.ClosedAt, a.EscalationLevel }).ToListAsync(ct);
        return Enum.GetValues<Severity>().Reverse().Select(sev =>
        {
            var g = alerts.Where(a => a.Severity == sev).ToList();
            var acks = g.Where(a => a.AcknowledgedAt != null).Select(a => (a.AcknowledgedAt!.Value - a.RaisedAt).TotalMinutes).ToList();
            var closes = g.Where(a => a.ClosedAt != null).Select(a => (a.ClosedAt!.Value - a.RaisedAt).TotalMinutes).ToList();
            return new AlertResponseRow(sev.ToString(), g.Count, acks.Count, closes.Count, g.Count(a => a.Status == AlertStatus.Open), acks.Count == 0 ? null : Math.Round(acks.Average(), 1), closes.Count == 0 ? null : Math.Round(closes.Average(), 1), g.Count(a => a.EscalationLevel > 0));
        }).ToList();
    }
}
