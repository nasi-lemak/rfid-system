using Microsoft.EntityFrameworkCore;
using Rfid.Application.Contracts;
using Rfid.Domain;
using Rfid.Domain.Entities;

namespace Rfid.Application.Services;

/// <summary>Per-dataset retention: defaults are created on first use; ApplyAsync deletes expired rows in batches (never chain-of-custody events unless explicitly configured).</summary>
public class RetentionService
{
    private readonly IAppDb _db; private readonly ICurrentContext _ctx;
    public int BatchSize { get; set; } = 5000;
    public RetentionService(IAppDb db, ICurrentContext ctx) { _db = db; _ctx = ctx; }

    public record DatasetInfo(string Dataset, string Description, int? DefaultDays, bool Protected);
    public static readonly DatasetInfo[] Datasets =
    {
        new("tag_reads", "Raw tag reads (every antenna sighting)", 90, false),
        new("position_fixes", "Indoor RTLS position samples", 30, false),
        new("gps_fixes", "GPS position samples", 90, false),
        new("presence_sessions", "Closed zone presence sessions", 180, false),
        new("device_heartbeats", "Reader health samples", 30, false),
        new("notification_logs", "Notification deliveries", 90, false),
        new("audit_entries", "Audit trail of API changes", 365, false),
        new("alerts", "Closed alerts", 365, false),
        new("anomalies", "Dismissed/confirmed anomalies", 90, false),
        new("print_jobs", "Finished print jobs", 90, false),
        new("warehouse_runs", "Warehouse export run history", 180, false),
        new("outbox", "Delivered / dead-lettered outbox messages", 30, false),
        new("idempotency_keys", "Replay-detection keys for read batches and captures", 30, false),
        new("item_events", "Item events – the chain of custody (off by default)", null, true),
    };

    public async Task<List<RetentionPolicy>> EnsureDefaultsAsync(CancellationToken ct = default)
    {
        var existing = await _db.RetentionPolicies.ToListAsync(ct);
        var added = false;
        foreach (var d in Datasets) if (existing.All(p => p.Dataset != d.Dataset)) { var p = new RetentionPolicy { TenantId = _ctx.TenantId, Dataset = d.Dataset, RetainDays = d.DefaultDays, Enabled = d.DefaultDays != null }; _db.RetentionPolicies.Add(p); existing.Add(p); added = true; }
        if (added) await _db.SaveChangesAsync(ct);
        return existing.OrderBy(p => Array.FindIndex(Datasets, d => d.Dataset == p.Dataset)).ToList();
    }

    /// <summary>Rows that would be deleted now, per dataset (for the preview column).</summary>
    public async Task<Dictionary<string, (long total, long expired)>> CountsAsync(DateTime now, CancellationToken ct = default)
    {
        var policies = await EnsureDefaultsAsync(ct); var result = new Dictionary<string, (long, long)>();
        foreach (var p in policies)
        {
            var cutoff = p.RetainDays.HasValue ? now.AddDays(-p.RetainDays.Value) : (DateTime?)null;
            var (total, expired) = await CountAsync(p.Dataset, cutoff, ct);
            result[p.Dataset] = (total, p.Enabled && cutoff != null ? expired : 0);
        }
        return result;
    }

    private async Task<(long total, long expired)> CountAsync(string ds, DateTime? cutoff, CancellationToken ct)
    {
        var c = cutoff ?? DateTime.MinValue;
        return ds switch
        {
            "tag_reads" => (await _db.TagReads.LongCountAsync(ct), await _db.TagReads.LongCountAsync(r => r.ReadAt < c, ct)),
            "position_fixes" => (await _db.PositionFixes.LongCountAsync(ct), await _db.PositionFixes.LongCountAsync(r => r.At < c, ct)),
            "gps_fixes" => (await _db.GpsFixes.LongCountAsync(ct), await _db.GpsFixes.LongCountAsync(r => r.At < c, ct)),
            "presence_sessions" => (await _db.PresenceSessions.LongCountAsync(ct), await _db.PresenceSessions.LongCountAsync(r => r.ExitedAt != null && r.ExitedAt < c, ct)),
            "device_heartbeats" => (await _db.DeviceHeartbeats.LongCountAsync(ct), await _db.DeviceHeartbeats.LongCountAsync(r => r.At < c, ct)),
            "notification_logs" => (await _db.NotificationLogs.LongCountAsync(ct), await _db.NotificationLogs.LongCountAsync(r => r.SentAt < c, ct)),
            "audit_entries" => (await _db.AuditEntries.LongCountAsync(ct), await _db.AuditEntries.LongCountAsync(r => r.At < c, ct)),
            "alerts" => (await _db.Alerts.LongCountAsync(ct), await _db.Alerts.LongCountAsync(r => r.Status == AlertStatus.Closed && r.ClosedAt < c, ct)),
            "anomalies" => (await _db.Anomalies.LongCountAsync(ct), await _db.Anomalies.LongCountAsync(r => r.Status != AnomalyStatus.Open && r.LastSeenAt < c, ct)),
            "print_jobs" => (await _db.PrintJobs.LongCountAsync(ct), await _db.PrintJobs.LongCountAsync(r => r.Status != PrintJobStatus.Queued && r.Status != PrintJobStatus.Printing && r.CreatedAt < c, ct)),
            "warehouse_runs" => (await _db.WarehouseExportRuns.LongCountAsync(ct), await _db.WarehouseExportRuns.LongCountAsync(r => r.StartedAt < c, ct)),
            "outbox" => (await _db.Outbox.LongCountAsync(ct), await _db.Outbox.LongCountAsync(r => r.ProcessedAt != null && r.ProcessedAt < c, ct)),
            "idempotency_keys" => (await _db.IdempotencyKeys.LongCountAsync(ct), await _db.IdempotencyKeys.LongCountAsync(r => r.CreatedAt < c, ct)),
            "item_events" => (await _db.ItemEvents.LongCountAsync(ct), await _db.ItemEvents.LongCountAsync(r => r.OccurredAt < c, ct)),
            _ => (0, 0),
        };
    }

    /// <summary>Applies every enabled policy; returns rows deleted per dataset.</summary>
    public async Task<Dictionary<string, int>> ApplyAsync(DateTime now, CancellationToken ct = default)
    {
        var policies = await EnsureDefaultsAsync(ct); var deleted = new Dictionary<string, int>();
        foreach (var p in policies.Where(p => p.Enabled && p.RetainDays.HasValue))
        {
            var cutoff = now.AddDays(-p.RetainDays!.Value); var n = 0;
            for (var i = 0; i < 20; i++) // at most 20 batches per run so one huge table can't starve the loop
            {
                var removed = await DeleteBatchAsync(p.Dataset, cutoff, ct);
                n += removed; if (removed < BatchSize) break;
            }
            p.LastRunAt = now; p.LastDeleted = n; p.TotalDeleted += n; deleted[p.Dataset] = n;
        }
        await _db.SaveChangesAsync(ct);
        return deleted;
    }

    private async Task<int> DeleteBatchAsync(string ds, DateTime c, CancellationToken ct)
    {
        async Task<int> Del<T>(DbSet<T> set, IQueryable<T> q) where T : class { var rows = await q.Take(BatchSize).ToListAsync(ct); if (rows.Count == 0) return 0; set.RemoveRange(rows); await _db.SaveChangesAsync(ct); return rows.Count; }
        return ds switch
        {
            "tag_reads" => await Del(_db.TagReads, _db.TagReads.Where(r => r.ReadAt < c)),
            "position_fixes" => await Del(_db.PositionFixes, _db.PositionFixes.Where(r => r.At < c)),
            "gps_fixes" => await Del(_db.GpsFixes, _db.GpsFixes.Where(r => r.At < c)),
            "presence_sessions" => await Del(_db.PresenceSessions, _db.PresenceSessions.Where(r => r.ExitedAt != null && r.ExitedAt < c)),
            "device_heartbeats" => await Del(_db.DeviceHeartbeats, _db.DeviceHeartbeats.Where(r => r.At < c)),
            "notification_logs" => await Del(_db.NotificationLogs, _db.NotificationLogs.Where(r => r.SentAt < c)),
            "audit_entries" => await Del(_db.AuditEntries, _db.AuditEntries.Where(r => r.At < c)),
            "alerts" => await Del(_db.Alerts, _db.Alerts.Where(r => r.Status == AlertStatus.Closed && r.ClosedAt < c)),
            "anomalies" => await Del(_db.Anomalies, _db.Anomalies.Where(r => r.Status != AnomalyStatus.Open && r.LastSeenAt < c)),
            "print_jobs" => await Del(_db.PrintJobs, _db.PrintJobs.Where(r => r.Status != PrintJobStatus.Queued && r.Status != PrintJobStatus.Printing && r.CreatedAt < c)),
            "warehouse_runs" => await Del(_db.WarehouseExportRuns, _db.WarehouseExportRuns.Where(r => r.StartedAt < c)),
            "outbox" => await Del(_db.Outbox, _db.Outbox.Where(r => r.ProcessedAt != null && r.ProcessedAt < c)),
            "idempotency_keys" => await Del(_db.IdempotencyKeys, _db.IdempotencyKeys.Where(r => r.CreatedAt < c)),
            "item_events" => await Del(_db.ItemEvents, _db.ItemEvents.Where(r => r.OccurredAt < c)),
            _ => 0,
        };
    }
}
