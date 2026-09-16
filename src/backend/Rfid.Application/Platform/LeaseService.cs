using Microsoft.EntityFrameworkCore;
using Rfid.Application.Contracts;
using Rfid.Domain.Entities;

namespace Rfid.Application.Cluster;

/// <summary>Identity of this API process in the cluster.</summary>
public static class ClusterNode
{
    public static readonly string Id = $"{Environment.MachineName}-{Guid.NewGuid().ToString("N")[..6]}";
    public static readonly DateTime StartedAt = DateTime.UtcNow;
}

/// <summary>
/// Database-backed leases: a node acquires "job:print-queue" or "llrp:{deviceId}" before doing the work and
/// renews it while running; when a node dies its leases expire and another node takes over. Uses an
/// optimistic concurrency token so two nodes can never both hold the same lease.
/// </summary>
public class LeaseService
{
    private readonly IAppDb _db;
    public LeaseService(IAppDb db) => _db = db;

    public async Task<bool> TryAcquireAsync(string name, string owner, TimeSpan ttl, DateTime? now = null, CancellationToken ct = default)
    {
        var t = now ?? DateTime.UtcNow;
        var lease = await _db.WorkerLeases.FirstOrDefaultAsync(l => l.Name == name, ct);
        try
        {
            if (lease == null)
            {
                _db.WorkerLeases.Add(new WorkerLease { Name = name, Owner = owner, ExpiresAt = t + ttl, AcquiredAt = t });
                await _db.SaveChangesAsync(ct);
                return true;
            }
            if (lease.Owner != owner && lease.ExpiresAt > t) return false;
            if (lease.Owner != owner) lease.AcquiredAt = t;
            lease.Owner = owner; lease.ExpiresAt = t + ttl; lease.Version++;
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException) { return false; } // another node won the race
    }

    public async Task ReleaseAsync(string name, string owner, DateTime? now = null, CancellationToken ct = default)
    {
        var lease = await _db.WorkerLeases.FirstOrDefaultAsync(l => l.Name == name && l.Owner == owner, ct);
        if (lease == null) return;
        lease.ExpiresAt = (now ?? DateTime.UtcNow).AddSeconds(-1); lease.Version++;
        try { await _db.SaveChangesAsync(ct); } catch (DbUpdateException) { /* lost to another node – fine */ }
    }

    public Task<List<WorkerLease>> ListAsync(CancellationToken ct = default) => _db.WorkerLeases.OrderBy(l => l.Name).ToListAsync(ct);
}
