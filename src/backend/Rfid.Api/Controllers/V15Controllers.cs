using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rfid.Application.Contracts;
using Rfid.Application.Services;
using Rfid.Domain;
using Rfid.Domain.Entities;
using Rfid.Infrastructure.Persistence;

namespace Rfid.Api.Controllers;

// ───────────────────────────── Device health & firmware ─────────────────────────────

[ApiController, Route("api/devices"), Authorize]
public class DeviceHealthController : ControllerBase
{
    private readonly AppDbContext _db; private readonly DeviceHealthService _svc; private readonly ICurrentContext _ctx;
    public DeviceHealthController(AppDbContext db, DeviceHealthService svc, ICurrentContext ctx) { _db = db; _svc = svc; _ctx = ctx; }

    /// <summary>Health SLA overview: state, uptime over the window, firmware, last heartbeat.</summary>
    [HttpGet("health")]
    public async Task<IActionResult> Health(int days = 7, CancellationToken ct = default)
    {
        var rows = await _svc.UptimeAsync(TimeSpan.FromDays(Math.Clamp(days, 1, 90)), null, ct);
        return Ok(new { days, summary = rows.GroupBy(r => r.Health).ToDictionary(g => g.Key.ToString(), g => g.Count()), rows });
    }

    /// <summary>Devices post their own health here (device JWT or operator token). Missing device id → the caller's device.</summary>
    [HttpPost("{id:guid}/heartbeat"), Authorize(Policy = "Operator")]
    public async Task<IActionResult> Heartbeat(Guid id, HeartbeatRequest r, CancellationToken ct) => Ok(await _svc.RecordAsync(id, r, ct));

    [HttpPost("heartbeat"), Authorize(Policy = "Operator")]
    public async Task<IActionResult> OwnHeartbeat(HeartbeatRequest r, CancellationToken ct)
        => _ctx.DeviceId is Guid d ? Ok(await _svc.RecordAsync(d, r, ct)) : BadRequest(new { error = "Not a device token; use POST /api/devices/{id}/heartbeat" });

    [HttpGet("{id:guid}/heartbeats")]
    public async Task<IActionResult> Heartbeats(Guid id, int take = 200) => Ok(await _db.DeviceHeartbeats.Where(h => h.DeviceId == id).OrderByDescending(h => h.At).Take(Math.Clamp(take, 1, 2000)).ToListAsync());

    /// <summary>Devices poll for a pending firmware update.</summary>
    [HttpGet("{id:guid}/firmware/pending"), Authorize(Policy = "Operator")]
    public async Task<IActionResult> Pending(Guid id, CancellationToken ct) => Ok(await _svc.PendingForDeviceAsync(id, null, ct) ?? (object)new { });

    public record SlaWrite(int? HeartbeatSlaMinutes, Guid? TrackedItemId);
    [HttpPut("{id:guid}/sla"), Authorize(Policy = "Admin")]
    public async Task<IActionResult> Sla(Guid id, SlaWrite w)
    {
        var d = await _db.Devices.FindAsync(id); if (d == null) return NotFound();
        d.HeartbeatSlaMinutes = w.HeartbeatSlaMinutes; d.TrackedItemId = w.TrackedItemId; await _db.SaveChangesAsync();
        return Ok(new { d.Id, d.HeartbeatSlaMinutes, d.TrackedItemId });
    }
}

[ApiController, Route("api/firmware"), Authorize]
public class FirmwareController : ControllerBase
{
    private readonly AppDbContext _db; private readonly DeviceHealthService _svc; private readonly ICurrentContext _ctx;
    public FirmwareController(AppDbContext db, DeviceHealthService svc, ICurrentContext ctx) { _db = db; _svc = svc; _ctx = ctx; }

    [HttpGet("releases")]
    public async Task<IActionResult> Releases()
    {
        var rels = await _db.FirmwareReleases.OrderBy(r => r.Vendor).ThenBy(r => r.Model).ThenByDescending(r => r.ReleasedAt).ToListAsync();
        var counts = await _db.FirmwareRollouts.GroupBy(r => new { r.ReleaseId, r.Status }).Select(g => new { g.Key.ReleaseId, g.Key.Status, Count = g.Count() }).ToListAsync();
        var onVersion = await _db.Devices.Where(d => d.FirmwareVersion != null).GroupBy(d => d.FirmwareVersion!).Select(g => new { Version = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Version, x => x.Count);
        return Ok(rels.Select(r => new { r.Id, r.Vendor, r.Model, r.Version, r.Url, r.Checksum, r.Notes, r.ReleasedAt, r.IsActive, DevicesOnVersion = onVersion.GetValueOrDefault(r.Version), Rollouts = counts.Where(c => c.ReleaseId == r.Id).ToDictionary(c => c.Status.ToString(), c => c.Count) }));
    }

    public record ReleaseWrite(string Vendor, string Model, string Version, string? Url, string? Checksum, string? Notes, bool IsActive = true);

    [HttpPost("releases"), Authorize(Policy = "Admin")]
    public async Task<IActionResult> Create(ReleaseWrite w)
    {
        var r = new FirmwareRelease { TenantId = _ctx.TenantId, Vendor = w.Vendor.Trim(), Model = w.Model.Trim(), Version = w.Version.Trim(), Url = w.Url, Checksum = w.Checksum, Notes = w.Notes, IsActive = w.IsActive };
        _db.FirmwareReleases.Add(r); await _db.SaveChangesAsync(); return Ok(r);
    }

    [HttpPut("releases/{id:guid}"), Authorize(Policy = "Admin")]
    public async Task<IActionResult> Update(Guid id, ReleaseWrite w)
    {
        var r = await _db.FirmwareReleases.FindAsync(id); if (r == null) return NotFound();
        r.Vendor = w.Vendor.Trim(); r.Model = w.Model.Trim(); r.Version = w.Version.Trim(); r.Url = w.Url; r.Checksum = w.Checksum; r.Notes = w.Notes; r.IsActive = w.IsActive;
        await _db.SaveChangesAsync(); return Ok(r);
    }

    [HttpDelete("releases/{id:guid}"), Authorize(Policy = "Admin")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var r = await _db.FirmwareReleases.FindAsync(id); if (r == null) return NotFound();
        if (await _db.FirmwareRollouts.AnyAsync(x => x.ReleaseId == id && (x.Status == FirmwareRolloutStatus.Pending || x.Status == FirmwareRolloutStatus.Sent))) return BadRequest(new { error = "Release has pending rollouts" });
        _db.FirmwareReleases.Remove(r); await _db.SaveChangesAsync(); return NoContent();
    }

    public record RolloutWrite(List<Guid> DeviceIds, DateTime? ScheduledAt);
    [HttpPost("releases/{id:guid}/rollout"), Authorize(Policy = "Admin")]
    public async Task<IActionResult> Rollout(Guid id, RolloutWrite w, CancellationToken ct) => Ok(await _svc.RolloutAsync(id, w.DeviceIds, w.ScheduledAt, ct));

    [HttpGet("rollouts")]
    public async Task<IActionResult> Rollouts(FirmwareRolloutStatus? status, int take = 200)
    {
        var q = _db.FirmwareRollouts.AsQueryable(); if (status.HasValue) q = q.Where(r => r.Status == status);
        var rows = await q.OrderByDescending(r => r.ScheduledAt).Take(Math.Clamp(take, 1, 1000)).ToListAsync();
        var devices = await _db.Devices.Where(d => rows.Select(r => r.DeviceId).Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Name);
        var releases = await _db.FirmwareReleases.Where(r => rows.Select(x => x.ReleaseId).Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Version);
        return Ok(rows.Select(r => new { r.Id, r.ReleaseId, Version = releases.GetValueOrDefault(r.ReleaseId), r.DeviceId, Device = devices.GetValueOrDefault(r.DeviceId), r.Status, r.ScheduledAt, r.StartedAt, r.CompletedAt, r.Error, r.Attempts, r.FromVersion }));
    }

    public record ReportWrite(FirmwareRolloutStatus Status, string? Error);
    /// <summary>Devices (or an operator simulating them) report rollout progress.</summary>
    [HttpPost("rollouts/{id:guid}/report"), Authorize(Policy = "Operator")]
    public async Task<IActionResult> Report(Guid id, ReportWrite w, CancellationToken ct) => Ok(await _svc.ReportAsync(id, w.Status, w.Error, null, ct));

    [HttpPost("rollouts/{id:guid}/cancel"), Authorize(Policy = "Admin")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct) => Ok(await _svc.ReportAsync(id, FirmwareRolloutStatus.Cancelled, "Cancelled by operator", null, ct));
}

// ───────────────────────────── Geofencing & map ─────────────────────────────

[ApiController, Route("api/geo"), Authorize]
public class GeoController : ControllerBase
{
    private readonly AppDbContext _db; private readonly GeoService _svc; private readonly ICurrentContext _ctx;
    public GeoController(AppDbContext db, GeoService svc, ICurrentContext ctx) { _db = db; _svc = svc; _ctx = ctx; }

    [HttpGet("map")]
    public async Task<IActionResult> Map(int hours = 168, CancellationToken ct = default) => Ok(await _svc.MapAsync(TimeSpan.FromHours(Math.Clamp(hours, 1, 24 * 365)), ct));

    [HttpGet("track")]
    public async Task<IActionResult> Track(Guid itemId, DateTime? from, DateTime? to, int take = 5000, CancellationToken ct = default)
    {
        var t = to ?? DateTime.UtcNow; var f = from ?? t.AddHours(-24);
        var item = await _db.Items.Where(i => i.Id == itemId).Select(i => new { i.Id, i.Name, i.Identifier }).FirstOrDefaultAsync(ct);
        return Ok(new { item, from = f, to = t, points = await _svc.TrackAsync(itemId, f, t, take, ct) });
    }

    [HttpGet("fences")]
    public async Task<IActionResult> Fences(CancellationToken ct) => Ok((await _svc.MapAsync(TimeSpan.FromHours(1), ct)).Fences);

    public record FenceWrite(string Name, GeoFenceKind Kind, double? CenterLat, double? CenterLng, double? RadiusM, List<GeoPoint>? Points, Guid? LocationId, GeoFenceTrigger Trigger, Guid? ItemTypeId, Severity Severity, bool Enabled = true, string? Color = null, int? MaxDwellMinutes = null);

    private static void Validate(FenceWrite w)
    {
        if (w.Kind == GeoFenceKind.Circle && (w.CenterLat == null || w.CenterLng == null || w.RadiusM is null or <= 0)) throw new DomainException("Circle fences need centerLat, centerLng and radiusM");
        if (w.Kind == GeoFenceKind.Polygon && (w.Points == null || w.Points.Count < 3)) throw new DomainException("Polygon fences need at least 3 points");
    }

    [HttpPost("fences"), Authorize(Policy = "Admin")]
    public async Task<IActionResult> Create(FenceWrite w)
    {
        Validate(w);
        var f = new GeoFence { TenantId = _ctx.TenantId, Name = w.Name, Kind = w.Kind, CenterLat = w.CenterLat, CenterLng = w.CenterLng, RadiusM = w.RadiusM, Points = w.Points ?? new(), LocationId = w.LocationId, Trigger = w.Trigger, ItemTypeId = w.ItemTypeId, Severity = w.Severity, Enabled = w.Enabled, Color = w.Color, MaxDwellMinutes = w.MaxDwellMinutes };
        _db.GeoFences.Add(f); await _db.SaveChangesAsync(); return Ok(f);
    }

    [HttpPut("fences/{id:guid}"), Authorize(Policy = "Admin")]
    public async Task<IActionResult> Update(Guid id, FenceWrite w)
    {
        Validate(w);
        var f = await _db.GeoFences.FindAsync(id); if (f == null) return NotFound();
        f.Name = w.Name; f.Kind = w.Kind; f.CenterLat = w.CenterLat; f.CenterLng = w.CenterLng; f.RadiusM = w.RadiusM; f.Points = w.Points ?? new(); f.LocationId = w.LocationId; f.Trigger = w.Trigger; f.ItemTypeId = w.ItemTypeId; f.Severity = w.Severity; f.Enabled = w.Enabled; f.Color = w.Color; f.MaxDwellMinutes = w.MaxDwellMinutes;
        await _db.SaveChangesAsync(); return Ok(f);
    }

    [HttpDelete("fences/{id:guid}"), Authorize(Policy = "Admin")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var f = await _db.GeoFences.FindAsync(id); if (f == null) return NotFound();
        _db.GeoFenceStates.RemoveRange(_db.GeoFenceStates.Where(s => s.FenceId == id));
        _db.GeoFences.Remove(f); await _db.SaveChangesAsync(); return NoContent();
    }
}

[ApiController, Route("api/ingest"), Authorize(Policy = "Operator")]
public class GpsIngestController : ControllerBase
{
    private readonly GeoService _svc;
    public GpsIngestController(GeoService svc) => _svc = svc;

    /// <summary>Telematics units, GPS trackers and handhelds post positions here (device JWT). Fixes resolve to items by itemId, EPC, identifier or the device's tracked item.</summary>
    [HttpPost("gps")]
    public async Task<ActionResult<GpsIngestResult>> Gps(GpsBatchRequest batch, CancellationToken ct) => Ok(await _svc.IngestAsync(batch, ct));
}

// ───────────────────────────── Dashboards ─────────────────────────────

[ApiController, Route("api/dashboards"), Authorize]
public class DashboardsController : ControllerBase
{
    private readonly AppDbContext _db; private readonly DashboardService _svc; private readonly ICurrentContext _ctx;
    public DashboardsController(AppDbContext db, DashboardService svc, ICurrentContext ctx) { _db = db; _svc = svc; _ctx = ctx; }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var mine = await _db.Dashboards.Where(d => d.OwnerUserId == null || d.OwnerUserId == _ctx.UserId).OrderByDescending(d => d.IsDefault).ThenBy(d => d.Name).ToListAsync();
        if (mine.Count == 0)
        {
            var def = new Dashboard { TenantId = _ctx.TenantId, Name = "Operations overview", IsDefault = true, Widgets = DashboardService.DefaultWidgets() };
            _db.Dashboards.Add(def); await _db.SaveChangesAsync(); mine.Add(def);
        }
        return Ok(mine.Select(d => new { d.Id, d.Name, d.IsDefault, d.OwnerUserId, Shared = d.OwnerUserId == null, d.Widgets, Editable = d.OwnerUserId == _ctx.UserId || User.IsInRole("Admin") }));
    }

    [HttpGet("widget-types")]
    public IActionResult WidgetTypes() => Ok(new
    {
        types = new[] { "stat", "breakdown", "trend", "presence", "devices", "stocktakes", "list", "fences", "text" },
        statFilters = DashboardService.StatFilters,
        breakdowns = new[] { "type", "state", "location", "status", "custodian", "severity", "alertSource" },
        trendMetrics = new[] { "reads", "operations", "events", "alerts", "gps", "notifications" },
        listSources = new[] { "alerts", "events" },
    });

    public record DashboardWrite(string Name, bool IsDefault, bool Shared, List<DashboardWidget> Widgets);

    [HttpPost, Authorize(Policy = "Operator")]
    public async Task<IActionResult> Create(DashboardWrite w)
    {
        var d = new Dashboard { TenantId = _ctx.TenantId, Name = w.Name, IsDefault = w.IsDefault, OwnerUserId = w.Shared && User.IsInRole("Admin") ? null : _ctx.UserId, Widgets = w.Widgets };
        if (d.IsDefault) foreach (var o in await _db.Dashboards.Where(x => x.IsDefault && (x.OwnerUserId == d.OwnerUserId)).ToListAsync()) o.IsDefault = false;
        _db.Dashboards.Add(d); await _db.SaveChangesAsync(); return Ok(d);
    }

    [HttpPut("{id:guid}"), Authorize(Policy = "Operator")]
    public async Task<IActionResult> Update(Guid id, DashboardWrite w)
    {
        var d = await _db.Dashboards.FindAsync(id); if (d == null) return NotFound();
        if (d.OwnerUserId != null && d.OwnerUserId != _ctx.UserId && !User.IsInRole("Admin")) return Forbid();
        if (d.OwnerUserId == null && !User.IsInRole("Admin")) return BadRequest(new { error = "Only admins edit shared dashboards; save a copy instead" });
        d.Name = w.Name; d.IsDefault = w.IsDefault; d.Widgets = w.Widgets; d.OwnerUserId = w.Shared && User.IsInRole("Admin") ? null : d.OwnerUserId ?? _ctx.UserId;
        if (d.IsDefault) foreach (var o in await _db.Dashboards.Where(x => x.IsDefault && x.Id != id && x.OwnerUserId == d.OwnerUserId).ToListAsync()) o.IsDefault = false;
        await _db.SaveChangesAsync(); return Ok(d);
    }

    [HttpDelete("{id:guid}"), Authorize(Policy = "Operator")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var d = await _db.Dashboards.FindAsync(id); if (d == null) return NotFound();
        if (d.OwnerUserId != _ctx.UserId && !User.IsInRole("Admin")) return Forbid();
        _db.Dashboards.Remove(d); await _db.SaveChangesAsync(); return NoContent();
    }

    /// <summary>Evaluates a list of widget configs (saved or not) and returns their data.</summary>
    [HttpPost("evaluate")]
    public async Task<IActionResult> Evaluate(List<DashboardWidget> widgets, CancellationToken ct) => Ok(await _svc.EvaluateAsync(widgets, ct));
}

// ───────────────────────────── Notifications & escalation ─────────────────────────────

[ApiController, Route("api/notifications"), Authorize(Policy = "Admin")]
public class NotificationsController : ControllerBase
{
    private readonly AppDbContext _db; private readonly NotificationService _svc; private readonly ICurrentContext _ctx;
    public NotificationsController(AppDbContext db, NotificationService svc, ICurrentContext ctx) { _db = db; _svc = svc; _ctx = ctx; }

    private static object Mask(NotificationChannel c) => new
    {
        c.Id, c.Name, c.Kind, c.Enabled, c.CatchAll, c.MinSeverity, c.CreatedAt,
        Config = c.Config.ToDictionary(kv => kv.Key, kv => kv.Key is "password" or "authToken" && kv.Value != null ? "••••••" : kv.Value),
    };

    [HttpGet("channels")]
    public async Task<IActionResult> Channels()
    {
        var chs = await _db.NotificationChannels.OrderBy(c => c.Name).ToListAsync();
        var since = DateTime.UtcNow.AddDays(-7);
        var stats = await _db.NotificationLogs.Where(l => l.SentAt >= since).GroupBy(l => new { l.ChannelId, l.Status }).Select(g => new { g.Key.ChannelId, g.Key.Status, Count = g.Count() }).ToListAsync();
        return Ok(chs.Select(c => new { channel = Mask(c), sent7d = stats.Where(s => s.ChannelId == c.Id && s.Status == NotificationStatus.Sent).Sum(s => s.Count), failed7d = stats.Where(s => s.ChannelId == c.Id && s.Status == NotificationStatus.Failed).Sum(s => s.Count) }));
    }

    public record ChannelWrite(string Name, NotificationKind Kind, Dictionary<string, object?> Config, bool Enabled = true, bool CatchAll = false, Severity MinSeverity = Severity.Critical);

    [HttpPost("channels")]
    public async Task<IActionResult> CreateChannel(ChannelWrite w)
    {
        var c = new NotificationChannel { TenantId = _ctx.TenantId, Name = w.Name, Kind = w.Kind, Config = w.Config, Enabled = w.Enabled, CatchAll = w.CatchAll, MinSeverity = w.MinSeverity };
        _db.NotificationChannels.Add(c); await _db.SaveChangesAsync(); return Ok(Mask(c));
    }

    [HttpPut("channels/{id:guid}")]
    public async Task<IActionResult> UpdateChannel(Guid id, ChannelWrite w)
    {
        var c = await _db.NotificationChannels.FindAsync(id); if (c == null) return NotFound();
        foreach (var secret in new[] { "password", "authToken" }) if (w.Config.TryGetValue(secret, out var v) && v?.ToString() == "••••••" && c.Config.TryGetValue(secret, out var old)) w.Config[secret] = old;
        c.Name = w.Name; c.Kind = w.Kind; c.Config = w.Config; c.Enabled = w.Enabled; c.CatchAll = w.CatchAll; c.MinSeverity = w.MinSeverity;
        await _db.SaveChangesAsync(); return Ok(Mask(c));
    }

    [HttpDelete("channels/{id:guid}")]
    public async Task<IActionResult> DeleteChannel(Guid id)
    {
        var c = await _db.NotificationChannels.FindAsync(id); if (c == null) return NotFound();
        _db.NotificationChannels.Remove(c); await _db.SaveChangesAsync(); return NoContent();
    }

    [HttpPost("channels/{id:guid}/test")]
    public async Task<IActionResult> Test(Guid id, CancellationToken ct)
    {
        var c = await _db.NotificationChannels.FindAsync(id); if (c == null) return NotFound();
        try { var to = await _svc.TestAsync(c, ct); return Ok(new { ok = true, recipient = to }); }
        catch (Exception ex) { return Ok(new { ok = false, error = ex.Message }); }
    }

    [HttpGet("policies")]
    public async Task<IActionResult> Policies() => Ok(await _db.EscalationPolicies.OrderBy(p => p.Name).ToListAsync());

    public record PolicyWrite(string Name, List<EscalationStep> Steps, bool RepeatLastStep = false, bool IsDefault = false, Severity MinSeverity = Severity.Warning);

    [HttpPost("policies")]
    public async Task<IActionResult> CreatePolicy(PolicyWrite w)
    {
        var p = new EscalationPolicy { TenantId = _ctx.TenantId, Name = w.Name, Steps = w.Steps, RepeatLastStep = w.RepeatLastStep, IsDefault = w.IsDefault, MinSeverity = w.MinSeverity };
        _db.EscalationPolicies.Add(p); await _db.SaveChangesAsync(); return Ok(p);
    }

    [HttpPut("policies/{id:guid}")]
    public async Task<IActionResult> UpdatePolicy(Guid id, PolicyWrite w)
    {
        var p = await _db.EscalationPolicies.FindAsync(id); if (p == null) return NotFound();
        p.Name = w.Name; p.Steps = w.Steps; p.RepeatLastStep = w.RepeatLastStep; p.IsDefault = w.IsDefault; p.MinSeverity = w.MinSeverity;
        await _db.SaveChangesAsync(); return Ok(p);
    }

    [HttpDelete("policies/{id:guid}")]
    public async Task<IActionResult> DeletePolicy(Guid id)
    {
        var p = await _db.EscalationPolicies.FindAsync(id); if (p == null) return NotFound();
        _db.EscalationPolicies.Remove(p); await _db.SaveChangesAsync(); return NoContent();
    }

    [HttpGet("log")]
    public async Task<IActionResult> Log(Guid? alertId, int take = 200)
    {
        var q = _db.NotificationLogs.AsQueryable(); if (alertId.HasValue) q = q.Where(l => l.AlertId == alertId);
        var rows = await q.OrderByDescending(l => l.SentAt).Take(Math.Clamp(take, 1, 2000)).ToListAsync();
        var names = await _db.NotificationChannels.ToDictionaryAsync(c => c.Id, c => c.Name);
        return Ok(rows.Select(l => new { l.Id, l.ChannelId, Channel = names.GetValueOrDefault(l.ChannelId), l.AlertId, l.Recipient, l.Subject, l.Status, l.Error, l.SentAt, l.EscalationLevel }));
    }
}

// ───────────────────────────── Warehouse export & analytics ─────────────────────────────

[ApiController, Route("api/warehouse"), Authorize(Policy = "Admin")]
public class WarehouseController : ControllerBase
{
    private readonly AppDbContext _db; private readonly WarehouseExportService _svc; private readonly IConfiguration _cfg;
    public WarehouseController(AppDbContext db, WarehouseExportService svc, IConfiguration cfg) { _db = db; _svc = svc; _cfg = cfg; }

    [HttpGet]
    public async Task<IActionResult> Status()
    {
        var runs = await _db.WarehouseExportRuns.OrderByDescending(r => r.StartedAt).Take(100).ToListAsync();
        var last = runs.Where(r => r.Error == null && !r.Manual).GroupBy(r => r.Dataset).ToDictionary(g => g.Key, g => g.Max(r => r.To));
        return Ok(new
        {
            enabled = _cfg.GetValue("Warehouse:Enabled", false), path = _svc.RootPath, format = _svc.DefaultFormat, intervalMinutes = _cfg.GetValue("Warehouse:IntervalMinutes", 60),
            datasets = WarehouseExportService.Datasets.Select(d => new { name = d, snapshot = WarehouseExportService.IsSnapshot(d), lastExportedTo = last.TryGetValue(d, out var lt) ? lt : (DateTime?)null }),
            runs,
        });
    }

    public record ExportWrite(string Dataset, DateTime? From, DateTime? To, string? Format);

    /// <summary>Writes a dataset window to the landing zone now (recorded as a manual run).</summary>
    [HttpPost("export")]
    public async Task<IActionResult> Export(ExportWrite w, CancellationToken ct)
    {
        var to = w.To ?? DateTime.UtcNow; var from = w.From ?? to.AddDays(-30);
        return Ok(await _svc.ExportAsync(w.Dataset, from, to, w.Format, true, ct));
    }

    /// <summary>Runs the incremental export of every dataset immediately.</summary>
    [HttpPost("run")]
    public async Task<IActionResult> Run(CancellationToken ct) => Ok(await _svc.RunIncrementalAsync(DateTime.UtcNow, null, ct));

    /// <summary>Streams a dataset window straight to the caller as Parquet or CSV (no run is recorded).</summary>
    [HttpGet("download")]
    public async Task<IActionResult> Download(string dataset, DateTime? from, DateTime? to, string format = "parquet", CancellationToken ct = default)
    {
        var t = to ?? DateTime.UtcNow; var f = from ?? t.AddDays(-30);
        var (type, rows) = await _svc.QueryAsync(dataset, f, t, ct);
        var fmt = format.ToLowerInvariant() == "csv" ? "csv" : "parquet";
        var bytes = await WarehouseExportService.SerializeAsync(type, rows, fmt, ct);
        return File(bytes, fmt == "csv" ? "text/csv" : "application/vnd.apache.parquet", $"{dataset}-{f:yyyyMMdd}-{t:yyyyMMdd}.{fmt}");
    }
}

[ApiController, Route("api/analytics"), Authorize]
public class AnalyticsController : ControllerBase
{
    private readonly AnalyticsService _svc; private readonly DeviceHealthService _health;
    public AnalyticsController(AnalyticsService svc, DeviceHealthService health) { _svc = svc; _health = health; }

    [HttpGet("trend")] public async Task<IActionResult> Trend(string metric = "events", int days = 90, string bucket = "day", CancellationToken ct = default) => Ok(await _svc.TrendAsync(metric, Math.Clamp(days, 1, 730), bucket, null, ct));
    [HttpGet("utilization")] public async Task<IActionResult> Utilization(int days = 90, CancellationToken ct = default) => Ok(await _svc.UtilizationAsync(Math.Clamp(days, 1, 730), null, ct));
    [HttpGet("dwell")] public async Task<IActionResult> Dwell(int days = 90, CancellationToken ct = default) => Ok(await _svc.DwellAsync(Math.Clamp(days, 1, 730), null, ct));
    [HttpGet("accuracy")] public async Task<IActionResult> Accuracy(int days = 365, CancellationToken ct = default) => Ok(await _svc.AccuracyAsync(Math.Clamp(days, 1, 730), null, ct));
    [HttpGet("alert-response")] public async Task<IActionResult> AlertResponse(int days = 90, CancellationToken ct = default) => Ok(await _svc.AlertResponseAsync(Math.Clamp(days, 1, 730), null, ct));
    [HttpGet("uptime")] public async Task<IActionResult> Uptime(int days = 30, CancellationToken ct = default) => Ok(await _health.UptimeAsync(TimeSpan.FromDays(Math.Clamp(days, 1, 365)), null, ct));
}
