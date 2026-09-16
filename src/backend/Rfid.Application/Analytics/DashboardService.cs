using Microsoft.EntityFrameworkCore;
using Rfid.Application.Contracts;
using Rfid.Application.Security;
using Rfid.Domain;
using Rfid.Domain.Entities;

namespace Rfid.Application.Services;

public record WidgetResult(string Id, string Type, object? Data, string? Error = null);

/// <summary>Evaluates dashboard widgets server-side so every dashboard is a stored list of widget configs.</summary>
public class DashboardService
{
    private readonly IAppDb _db;
    public DashboardService(IAppDb db) => _db = db;

    public static readonly string[] StatFilters = { "items", "tags", "openAlerts", "criticalAlerts", "inCustody", "overdue", "missing", "notSeen7d", "expiring", "inspectionDue", "lowStock", "readsToday", "operationsToday", "openStocktakes", "devicesOffline", "devicesOnline", "present", "gpsTracked", "insideFences", "printQueued" };

    public static List<DashboardWidget> DefaultWidgets() => new()
    {
        W("stat", "Tracked items", 1, new() { ["filter"] = "items", ["link"] = "/items" }),
        W("stat", "Open alerts", 1, new() { ["filter"] = "openAlerts", ["tone"] = "warn", ["link"] = "/alerts" }),
        W("stat", "Critical alerts", 1, new() { ["filter"] = "criticalAlerts", ["tone"] = "crit", ["link"] = "/alerts" }),
        W("stat", "Readers offline", 1, new() { ["filter"] = "devicesOffline", ["tone"] = "crit", ["link"] = "/devices" }),
        W("stat", "In custody", 1, new() { ["filter"] = "inCustody", ["link"] = "/items?filter=inCustody" }),
        W("stat", "Overdue returns", 1, new() { ["filter"] = "overdue", ["tone"] = "warn", ["link"] = "/items?filter=overdue" }),
        W("stat", "Missing", 1, new() { ["filter"] = "missing", ["tone"] = "crit", ["link"] = "/items?status=Missing" }),
        W("stat", "Reads today", 1, new() { ["filter"] = "readsToday", ["link"] = "/live" }),
        W("breakdown", "Items by type", 1, new() { ["by"] = "type" }),
        W("breakdown", "Items by state", 1, new() { ["by"] = "state" }),
        W("breakdown", "Items by location", 1, new() { ["by"] = "location" }),
        W("trend", "Tag reads · 14 days", 1, new() { ["metric"] = "reads", ["days"] = 14 }),
        W("trend", "Operations · 14 days", 1, new() { ["metric"] = "operations", ["days"] = 14 }),
        W("devices", "Reader health", 1, new()),
        W("list", "Open alerts", 2, new() { ["source"] = "alerts", ["take"] = 8 }),
        W("list", "Recent activity", 2, new() { ["source"] = "events", ["take"] = 10 }),
    };
    private static DashboardWidget W(string type, string title, int w, Dictionary<string, object?> cfg) => new() { Type = type, Title = title, W = w, Config = cfg };

    public async Task<List<WidgetResult>> EvaluateAsync(IEnumerable<DashboardWidget> widgets, SiteScope? scope = null, CancellationToken ct = default)
    {
        var sc = scope ?? SiteScope.All;
        var results = new List<WidgetResult>();
        foreach (var w in widgets)
        {
            try { results.Add(new WidgetResult(w.Id, w.Type, await EvaluateOneAsync(w, sc, ct))); }
            catch (Exception ex) when (ex is not OperationCanceledException) { results.Add(new WidgetResult(w.Id, w.Type, null, ex.Message)); }
        }
        return results;
    }

    private static string? Str(DashboardWidget w, string k) => w.Config.TryGetValue(k, out var v) && v != null ? v.ToString() : null;
    private static int Int(DashboardWidget w, string k, int def) => int.TryParse(Str(w, k), out var i) ? i : def;
    private static Guid? Gid(DashboardWidget w, string k) => Guid.TryParse(Str(w, k), out var g) ? g : null;

    private async Task<IQueryable<Item>> ScopedItemsAsync(DashboardWidget w, SiteScope scope, CancellationToken ct)
    {
        var q = scope.Items(_db.Items).Where(i => i.Status != ItemStatus.Disposed);
        if (Gid(w, "itemTypeId") is { } t) q = q.Where(i => i.ItemTypeId == t);
        if (Gid(w, "locationId") is { } l) { var path = await _db.Locations.Where(x => x.Id == l).Select(x => x.Path).FirstOrDefaultAsync(ct); if (path != null) q = q.Where(i => i.CurrentLocation != null && i.CurrentLocation.Path.StartsWith(path)); }
        if (Str(w, "state") is { } s && s != "") q = q.Where(i => i.State == s);
        return q;
    }

    private async Task<object?> EvaluateOneAsync(DashboardWidget w, SiteScope scope, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        switch (w.Type)
        {
            case "stat":
            {
                var items = await ScopedItemsAsync(w, scope, ct);
                var filter = Str(w, "filter") ?? "items";
                long value = filter switch
                {
                    "items" => await items.CountAsync(ct),
                    "tags" => await _db.Tags.CountAsync(t => t.Status == TagStatus.Active, ct),
                    "openAlerts" => await scope.Alerts(_db.Alerts).CountAsync(a => a.Status == AlertStatus.Open, ct),
                    "criticalAlerts" => await scope.Alerts(_db.Alerts).CountAsync(a => a.Status == AlertStatus.Open && a.Severity == Severity.Critical, ct),
                    "inCustody" => await items.CountAsync(i => i.CustodianPartyId != null, ct),
                    "overdue" => await items.CountAsync(i => i.DueBackAt != null && i.DueBackAt < now, ct),
                    "missing" => await items.CountAsync(i => i.Status == ItemStatus.Missing, ct),
                    "notSeen7d" => await items.CountAsync(i => i.LastSeenAt == null || i.LastSeenAt < now.AddDays(-7), ct),
                    "expiring" => await items.CountAsync(i => i.ExpiryDate != null && i.ExpiryDate <= DateOnly.FromDateTime(now.AddDays(30)), ct),
                    "inspectionDue" => await items.CountAsync(i => i.NextInspectionDue != null && i.NextInspectionDue <= now.AddDays(14), ct),
                    "lowStock" => await items.CountAsync(i => i.ItemType!.ReorderPoint != null && i.Quantity < i.ItemType.ReorderPoint, ct),
                    "readsToday" => await scope.Reads(_db.TagReads).CountAsync(r => r.ReadAt >= now.Date, ct),
                    "operationsToday" => await scope.Operations(_db.Operations).CountAsync(o => o.StartedAt >= now.Date, ct),
                    "openStocktakes" => await scope.Stocktakes(_db.Stocktakes).CountAsync(s => s.Status == StocktakeStatus.Open, ct),
                    "devicesOffline" => await scope.Devices(_db.Devices).CountAsync(d => d.Health == DeviceHealth.Offline, ct),
                    "devicesOnline" => await scope.Devices(_db.Devices).CountAsync(d => d.Health == DeviceHealth.Online, ct),
                    "present" => await scope.Sessions(_db.PresenceSessions).CountAsync(s => s.ExitedAt == null, ct),
                    "gpsTracked" => await items.CountAsync(i => i.Latitude != null && i.GpsAt >= now.AddDays(-1), ct),
                    "insideFences" => await _db.GeoFenceStates.CountAsync(s => s.Inside, ct),
                    "printQueued" => await _db.PrintJobs.CountAsync(p => p.Status == PrintJobStatus.Queued, ct),
                    _ => throw new DomainException($"Unknown stat filter '{filter}'"),
                };
                return new { value, link = Str(w, "link"), tone = Str(w, "tone") };
            }
            case "breakdown":
            {
                var items = await ScopedItemsAsync(w, scope, ct); var take = Math.Clamp(Int(w, "take", 10), 1, 30);
                var by = Str(w, "by") ?? "type";
                var rows = by switch
                {
                    "type" => await items.GroupBy(i => i.ItemType!.Name).Select(g => new { label = g.Key, count = g.Count() }).OrderByDescending(x => x.count).Take(take).ToListAsync(ct),
                    "state" => await items.Where(i => i.State != null).GroupBy(i => i.State!).Select(g => new { label = g.Key, count = g.Count() }).OrderByDescending(x => x.count).Take(take).ToListAsync(ct),
                    "location" => await items.Where(i => i.CurrentLocation != null).GroupBy(i => i.CurrentLocation!.Name).Select(g => new { label = g.Key, count = g.Count() }).OrderByDescending(x => x.count).Take(take).ToListAsync(ct),
                    "status" => await scope.Items(_db.Items).GroupBy(i => i.Status).Select(g => new { label = g.Key.ToString(), count = g.Count() }).OrderByDescending(x => x.count).ToListAsync(ct),
                    "custodian" => await items.Where(i => i.CustodianParty != null).GroupBy(i => i.CustodianParty!.Name).Select(g => new { label = g.Key, count = g.Count() }).OrderByDescending(x => x.count).Take(take).ToListAsync(ct),
                    "severity" => await scope.Alerts(_db.Alerts).Where(a => a.Status == AlertStatus.Open).GroupBy(a => a.Severity).Select(g => new { label = g.Key.ToString(), count = g.Count() }).OrderByDescending(x => x.count).ToListAsync(ct),
                    "alertSource" => await scope.Alerts(_db.Alerts).Where(a => a.Status == AlertStatus.Open).GroupBy(a => a.Source ?? "rule").Select(g => new { label = g.Key, count = g.Count() }).OrderByDescending(x => x.count).ToListAsync(ct),
                    _ => throw new DomainException($"Unknown breakdown '{by}'"),
                };
                return rows;
            }
            case "trend":
            {
                var days = Math.Clamp(Int(w, "days", 14), 1, 365); var since = now.Date.AddDays(-(days - 1));
                var metric = Str(w, "metric") ?? "reads";
                var rows = metric switch
                {
                    "reads" => await scope.Reads(_db.TagReads).Where(r => r.ReadAt >= since).GroupBy(r => r.ReadAt.Date).Select(g => new { day = g.Key, count = g.Count() }).OrderBy(x => x.day).ToListAsync(ct),
                    "operations" => await scope.Operations(_db.Operations).Where(o => o.StartedAt >= since).GroupBy(o => o.StartedAt.Date).Select(g => new { day = g.Key, count = g.Count() }).OrderBy(x => x.day).ToListAsync(ct),
                    "events" => await scope.Events(_db.ItemEvents).Where(e => e.OccurredAt >= since).GroupBy(e => e.OccurredAt.Date).Select(g => new { day = g.Key, count = g.Count() }).OrderBy(x => x.day).ToListAsync(ct),
                    "alerts" => await scope.Alerts(_db.Alerts).Where(a => a.RaisedAt >= since).GroupBy(a => a.RaisedAt.Date).Select(g => new { day = g.Key, count = g.Count() }).OrderBy(x => x.day).ToListAsync(ct),
                    "gps" => await _db.GpsFixes.Where(a => a.At >= since).GroupBy(a => a.At.Date).Select(g => new { day = g.Key, count = g.Count() }).OrderBy(x => x.day).ToListAsync(ct),
                    "notifications" => await _db.NotificationLogs.Where(a => a.SentAt >= since).GroupBy(a => a.SentAt.Date).Select(g => new { day = g.Key, count = g.Count() }).OrderBy(x => x.day).ToListAsync(ct),
                    _ => throw new DomainException($"Unknown metric '{metric}'"),
                };
                return new { days, rows };
            }
            case "presence":
            {
                var take = Math.Clamp(Int(w, "take", 8), 1, 30);
                var open = scope.Sessions(_db.PresenceSessions).Where(s => s.ExitedAt == null);
                if (Gid(w, "locationId") is { } l) { var path = await _db.Locations.Where(x => x.Id == l).Select(x => x.Path).FirstOrDefaultAsync(ct); if (path != null) { var ids = await _db.Locations.Where(x => x.Path.StartsWith(path)).Select(x => x.Id).ToListAsync(ct); open = open.Where(s => ids.Contains(s.LocationId)); } }
                var rows = await open.GroupBy(s => s.LocationId).Select(g => new { locationId = g.Key, count = g.Count() }).OrderByDescending(x => x.count).Take(take).ToListAsync(ct);
                var names = await _db.Locations.Where(x => rows.Select(r => r.locationId).Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
                return rows.Select(r => new { label = names.GetValueOrDefault(r.locationId, "?"), count = r.count, r.locationId });
            }
            case "devices":
            {
                var rows = await scope.Devices(_db.Devices).GroupBy(d => d.Health).Select(g => new { health = g.Key.ToString(), count = g.Count() }).ToListAsync(ct);
                var offline = await scope.Devices(_db.Devices).Where(d => d.Health == DeviceHealth.Offline || d.Health == DeviceHealth.Degraded).OrderBy(d => d.Name).Take(8).Select(d => new { d.Id, d.Name, health = d.Health.ToString(), d.LastHeartbeatAt, d.LastSeenAt }).ToListAsync(ct);
                return new { total = rows.Sum(r => r.count), byHealth = rows, attention = offline };
            }
            case "stocktakes":
            {
                var take = Math.Clamp(Int(w, "take", 6), 1, 20);
                var rows = await scope.Stocktakes(_db.Stocktakes).Where(s => s.Status != StocktakeStatus.Cancelled).OrderByDescending(s => s.StartedAt).Take(take).Select(s => new { s.Id, s.Name, status = s.Status.ToString(), s.ExpectedCount, s.FoundCount, s.MissingCount, s.UnexpectedCount, s.StartedAt }).ToListAsync(ct);
                return rows.Select(s => new { s.Id, s.Name, s.status, s.ExpectedCount, s.FoundCount, s.MissingCount, s.UnexpectedCount, s.StartedAt, accuracy = s.ExpectedCount == 0 ? (double?)null : Math.Round(100.0 * s.FoundCount / s.ExpectedCount, 1) });
            }
            case "list":
            {
                var take = Math.Clamp(Int(w, "take", 8), 1, 50);
                if ((Str(w, "source") ?? "alerts") == "events")
                {
                    var evs = await scope.Events(_db.ItemEvents).OrderByDescending(e => e.OccurredAt).Take(take).ToListAsync(ct);
                    var ids = evs.Select(e => e.ItemId).Distinct().ToList();
                    var names = await _db.Items.Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Name, ct);
                    return evs.Select(e => new { e.Id, e.ItemId, itemName = names.GetValueOrDefault(e.ItemId), type = e.Type.ToString(), e.OccurredAt, e.ToState });
                }
                var alerts = await scope.Alerts(_db.Alerts).Where(a => a.Status == AlertStatus.Open).OrderByDescending(a => a.RaisedAt).Take(take).Select(a => new { a.Id, a.ItemId, severity = a.Severity.ToString(), a.Message, a.RaisedAt, a.Source, a.EscalationLevel }).ToListAsync(ct);
                return alerts;
            }
            case "fences":
            {
                var fences = await _db.GeoFences.Where(f => f.Enabled).OrderBy(f => f.Name).ToListAsync(ct);
                var inside = await _db.GeoFenceStates.Where(s => s.Inside).GroupBy(s => s.FenceId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
                return fences.Select(f => new { label = f.Name, count = inside.GetValueOrDefault(f.Id), f.Id });
            }
            case "text": return new { text = Str(w, "text") ?? "" };
            default: throw new DomainException($"Unknown widget type '{w.Type}'");
        }
    }

    /// <summary>The overview totals/breakdowns the web dashboard header uses (formerly computed in the controller), site-scoped.</summary>
    public async Task<object> OverviewAsync(SiteScope scope, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var all = scope.Items(_db.Items);
        var items = all.Where(i => i.Status != ItemStatus.Disposed);
        var byStatus = await all.GroupBy(i => i.Status).Select(g => new { Status = g.Key.ToString(), Count = g.Count() }).ToListAsync(ct);
        var byType = await items.GroupBy(i => i.ItemType!.Name).Select(g => new { Type = g.Key, Count = g.Count() }).OrderByDescending(x => x.Count).Take(12).ToListAsync(ct);
        var byState = await items.Where(i => i.State != null).GroupBy(i => i.State!).Select(g => new { State = g.Key, Count = g.Count() }).OrderByDescending(x => x.Count).Take(12).ToListAsync(ct);
        var byLocation = await items.Where(i => i.CurrentLocation != null).GroupBy(i => i.CurrentLocation!.Name).Select(g => new { Location = g.Key, Count = g.Count() }).OrderByDescending(x => x.Count).Take(10).ToListAsync(ct);
        var since = now.AddDays(-14);
        var readsPerDay = await scope.Reads(_db.TagReads).Where(r => r.ReadAt >= since).GroupBy(r => r.ReadAt.Date).Select(g => new { Day = g.Key, Count = g.Count() }).OrderBy(x => x.Day).ToListAsync(ct);
        var opsPerDay = await scope.Operations(_db.Operations).Where(o => o.StartedAt >= since).GroupBy(o => o.StartedAt.Date).Select(g => new { Day = g.Key, Count = g.Count() }).OrderBy(x => x.Day).ToListAsync(ct);
        var alerts = scope.Alerts(_db.Alerts);
        return new
        {
            totals = new
            {
                items = await items.CountAsync(ct),
                tags = await _db.Tags.CountAsync(t => t.Status == TagStatus.Active, ct),
                locations = await scope.Locations(_db.Locations).CountAsync(ct),
                devices = await scope.Devices(_db.Devices).CountAsync(ct),
                openAlerts = await alerts.CountAsync(a => a.Status == AlertStatus.Open, ct),
                criticalAlerts = await alerts.CountAsync(a => a.Status == AlertStatus.Open && a.Severity == Severity.Critical, ct),
                inCustody = await items.CountAsync(i => i.CustodianPartyId != null, ct),
                overdue = await items.CountAsync(i => i.DueBackAt != null && i.DueBackAt < now, ct),
                missing = await all.CountAsync(i => i.Status == ItemStatus.Missing, ct),
                expiring30d = await items.CountAsync(i => i.ExpiryDate != null && i.ExpiryDate <= DateOnly.FromDateTime(now.AddDays(30)), ct),
                inspectionDue14d = await items.CountAsync(i => i.NextInspectionDue != null && i.NextInspectionDue <= now.AddDays(14), ct),
                notSeen7d = await items.CountAsync(i => i.LastSeenAt == null || i.LastSeenAt < now.AddDays(-7), ct),
                readsToday = await scope.Reads(_db.TagReads).CountAsync(r => r.ReadAt >= now.Date, ct),
                operationsToday = await scope.Operations(_db.Operations).CountAsync(o => o.StartedAt >= now.Date, ct),
                openStocktakes = await scope.Stocktakes(_db.Stocktakes).CountAsync(s => s.Status == StocktakeStatus.Open, ct),
            },
            byStatus, byType, byState, byLocation, readsPerDay, opsPerDay,
        };
    }
}
