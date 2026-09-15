using Microsoft.EntityFrameworkCore;
using Rfid.Application.Contracts;
using Rfid.Domain;
using Rfid.Domain.Entities;

namespace Rfid.Application.Services;

public class HeartbeatRequest
{
    public string? FirmwareVersion { get; set; }
    public double? CpuPercent { get; set; }
    public double? MemoryPercent { get; set; }
    public double? TemperatureC { get; set; }
    public int? ReadsPerMinute { get; set; }
    public double? BatteryPercent { get; set; }
    public string? IpAddress { get; set; }
    public Dictionary<string, object?>? Metrics { get; set; }
    public DateTime? At { get; set; }
}

/// <summary>Reader health SLAs (heartbeat/last-read deadlines → Online/Degraded/Offline with alerts) and firmware rollouts.</summary>
public class DeviceHealthService
{
    private readonly IAppDb _db; private readonly ICurrentContext _ctx; private readonly NotificationService? _notifications;
    public int DefaultSlaMinutes { get; set; } = 15;
    public DeviceHealthService(IAppDb db, ICurrentContext ctx, NotificationService? notifications = null) { _db = db; _ctx = ctx; _notifications = notifications; }

    public static int SlaMinutesFor(Device d, int fallback) => d.HeartbeatSlaMinutes ?? (d.Kind is DeviceKind.Handheld or DeviceKind.Printer ? 24 * 60 : fallback);

    public async Task<DeviceHeartbeat> RecordAsync(Guid deviceId, HeartbeatRequest r, CancellationToken ct = default)
    {
        var d = await _db.Devices.FirstOrDefaultAsync(x => x.Id == deviceId, ct) ?? throw new NotFoundException("Device");
        var hb = new DeviceHeartbeat
        {
            TenantId = d.TenantId, DeviceId = d.Id, At = r.At?.ToUniversalTime() ?? DateTime.UtcNow, FirmwareVersion = r.FirmwareVersion, CpuPercent = r.CpuPercent, MemoryPercent = r.MemoryPercent,
            TemperatureC = r.TemperatureC, ReadsPerMinute = r.ReadsPerMinute, BatteryPercent = r.BatteryPercent, IpAddress = r.IpAddress, Metrics = r.Metrics ?? new(),
        };
        _db.DeviceHeartbeats.Add(hb);
        d.LastHeartbeatAt = hb.At;
        if (!string.IsNullOrWhiteSpace(r.FirmwareVersion)) d.FirmwareVersion = r.FirmwareVersion;
        await ApplyHealthAsync(d, ComputeHealth(d, hb.At), hb.At, ct);
        await _db.SaveChangesAsync(ct);
        return hb;
    }

    public DeviceHealth ComputeHealth(Device d, DateTime now)
    {
        if (d.Health == DeviceHealth.Updating) return DeviceHealth.Updating;
        var last = Max(d.LastHeartbeatAt, d.LastSeenAt);
        if (last == null) return DeviceHealth.Unknown;
        var sla = TimeSpan.FromMinutes(SlaMinutesFor(d, DefaultSlaMinutes));
        var age = now - last.Value;
        return age <= sla ? DeviceHealth.Online : age <= sla * 2 ? DeviceHealth.Degraded : DeviceHealth.Offline;
    }
    private static DateTime? Max(DateTime? a, DateTime? b) => a == null ? b : b == null ? a : a > b ? a : b;

    /// <summary>Re-evaluates every device; raises an alert when a device goes offline and closes it when it returns. Returns the number of state changes.</summary>
    public async Task<int> EvaluateAsync(DateTime now, CancellationToken ct = default)
    {
        var devices = await _db.Devices.ToListAsync(ct);
        var changes = 0;
        foreach (var d in devices) if (await ApplyHealthAsync(d, ComputeHealth(d, now), now, ct)) changes++;
        if (changes > 0) await _db.SaveChangesAsync(ct);
        return changes;
    }

    private async Task<bool> ApplyHealthAsync(Device d, DeviceHealth health, DateTime now, CancellationToken ct)
    {
        if (d.Health == health) return false;
        var prev = d.Health; d.Health = health; d.HealthChangedAt = now;
        if (health == DeviceHealth.Offline)
        {
            var open = await _db.Alerts.AnyAsync(a => a.DeviceId == d.Id && a.Source == "device-health" && a.Status != AlertStatus.Closed, ct);
            if (!open)
            {
                var alert = new Alert { TenantId = d.TenantId, DeviceId = d.Id, LocationId = d.SiteLocationId, Severity = Severity.Critical, Source = "device-health", Message = $"Reader offline: {d.Name} (no heartbeat/reads for more than {SlaMinutesFor(d, DefaultSlaMinutes) * 2} min)", RaisedAt = now };
                _db.Alerts.Add(alert);
                if (_notifications != null) await _notifications.OnAlertRaisedAsync(alert, null, ct);
            }
        }
        else if (health == DeviceHealth.Online && prev is DeviceHealth.Offline or DeviceHealth.Degraded or DeviceHealth.Updating)
        {
            var open = await _db.Alerts.Where(a => a.DeviceId == d.Id && a.Source == "device-health" && a.Status != AlertStatus.Closed).ToListAsync(ct);
            foreach (var a in open) { a.Status = AlertStatus.Closed; a.ClosedAt = now; a.NextEscalationAt = null; }
        }
        return true;
    }

    public record UptimeRow(Guid DeviceId, string Name, DeviceHealth Health, int Heartbeats, int Expected, double UptimePercent, DateTime? LastHeartbeatAt, string? FirmwareVersion, int SlaMinutes);

    /// <summary>Uptime over the window ≈ heartbeats received ÷ heartbeats expected at the device's SLA interval.</summary>
    public async Task<List<UptimeRow>> UptimeAsync(TimeSpan window, DateTime? now = null, CancellationToken ct = default)
    {
        var t = now ?? DateTime.UtcNow; var from = t - window;
        var devices = await _db.Devices.OrderBy(d => d.Name).ToListAsync(ct);
        var counts = await _db.DeviceHeartbeats.Where(h => h.At >= from && h.At <= t).GroupBy(h => h.DeviceId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return devices.Select(d =>
        {
            var sla = SlaMinutesFor(d, DefaultSlaMinutes);
            var expected = Math.Max(1, (int)(window.TotalMinutes / sla));
            var got = counts.GetValueOrDefault(d.Id);
            return new UptimeRow(d.Id, d.Name, d.Health, got, expected, Math.Round(Math.Min(100, 100.0 * got / expected), 1), d.LastHeartbeatAt, d.FirmwareVersion, sla);
        }).ToList();
    }

    // ── Firmware ──

    public async Task<List<FirmwareRollout>> RolloutAsync(Guid releaseId, IEnumerable<Guid> deviceIds, DateTime? scheduledAt = null, CancellationToken ct = default)
    {
        var release = await _db.FirmwareReleases.FirstOrDefaultAsync(r => r.Id == releaseId, ct) ?? throw new NotFoundException("Firmware release");
        var ids = deviceIds.Distinct().ToList();
        var devices = await _db.Devices.Where(d => ids.Contains(d.Id)).ToListAsync(ct);
        var pending = await _db.FirmwareRollouts.Where(r => ids.Contains(r.DeviceId) && (r.Status == FirmwareRolloutStatus.Pending || r.Status == FirmwareRolloutStatus.Sent)).ToListAsync(ct);
        foreach (var p in pending) p.Status = FirmwareRolloutStatus.Cancelled;
        var created = new List<FirmwareRollout>();
        foreach (var d in devices)
        {
            if (string.Equals(d.FirmwareVersion, release.Version, StringComparison.OrdinalIgnoreCase)) continue;
            var r = new FirmwareRollout { TenantId = d.TenantId, ReleaseId = release.Id, DeviceId = d.Id, ScheduledAt = scheduledAt ?? DateTime.UtcNow, FromVersion = d.FirmwareVersion };
            _db.FirmwareRollouts.Add(r); created.Add(r);
        }
        await _db.SaveChangesAsync(ct);
        return created;
    }

    public record PendingUpdate(Guid RolloutId, string Version, string? Url, string? Checksum, string? Notes);

    /// <summary>What a device should install next (it polls this; marking the rollout Sent).</summary>
    public async Task<PendingUpdate?> PendingForDeviceAsync(Guid deviceId, DateTime? now = null, CancellationToken ct = default)
    {
        var t = now ?? DateTime.UtcNow;
        var r = await _db.FirmwareRollouts.Where(x => x.DeviceId == deviceId && (x.Status == FirmwareRolloutStatus.Pending || x.Status == FirmwareRolloutStatus.Sent) && x.ScheduledAt <= t).OrderBy(x => x.ScheduledAt).FirstOrDefaultAsync(ct);
        if (r == null) return null;
        var rel = await _db.FirmwareReleases.FirstOrDefaultAsync(x => x.Id == r.ReleaseId, ct);
        if (rel == null) return null;
        if (r.Status == FirmwareRolloutStatus.Pending) { r.Status = FirmwareRolloutStatus.Sent; r.StartedAt = t; r.Attempts++; await _db.SaveChangesAsync(ct); }
        return new PendingUpdate(r.Id, rel.Version, rel.Url, rel.Checksum, rel.Notes);
    }

    /// <summary>Device progress report: Downloading → Installing → Done (or Failed with an error).</summary>
    public async Task<FirmwareRollout> ReportAsync(Guid rolloutId, FirmwareRolloutStatus status, string? error = null, DateTime? now = null, CancellationToken ct = default)
    {
        var t = now ?? DateTime.UtcNow;
        var r = await _db.FirmwareRollouts.FirstOrDefaultAsync(x => x.Id == rolloutId, ct) ?? throw new NotFoundException("Rollout");
        var d = await _db.Devices.FirstOrDefaultAsync(x => x.Id == r.DeviceId, ct);
        r.Status = status; r.Error = error;
        if (status is FirmwareRolloutStatus.Downloading or FirmwareRolloutStatus.Installing) { r.StartedAt ??= t; if (d != null && d.Health != DeviceHealth.Updating) { d.Health = DeviceHealth.Updating; d.HealthChangedAt = t; } }
        if (status is FirmwareRolloutStatus.Done or FirmwareRolloutStatus.Failed or FirmwareRolloutStatus.Cancelled)
        {
            r.CompletedAt = t;
            if (d != null)
            {
                if (status == FirmwareRolloutStatus.Done) { var rel = await _db.FirmwareReleases.FirstOrDefaultAsync(x => x.Id == r.ReleaseId, ct); if (rel != null) d.FirmwareVersion = rel.Version; }
                d.Health = DeviceHealth.Unknown; d.LastHeartbeatAt = t; // leave Updating; the next evaluation settles the real state
                d.Health = ComputeHealth(d, t);
                if (status == FirmwareRolloutStatus.Failed)
                {
                    var alert = new Alert { TenantId = d.TenantId, DeviceId = d.Id, LocationId = d.SiteLocationId, Severity = Severity.Warning, Source = "firmware", Message = $"Firmware update failed on {d.Name}: {error ?? "unknown error"}", RaisedAt = t };
                    _db.Alerts.Add(alert);
                    if (_notifications != null) await _notifications.OnAlertRaisedAsync(alert, null, ct);
                }
            }
        }
        await _db.SaveChangesAsync(ct);
        return r;
    }
}
