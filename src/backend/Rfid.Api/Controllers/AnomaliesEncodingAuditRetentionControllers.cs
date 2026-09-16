using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rfid.Application.Contracts;
using Rfid.Application.Services;
using Rfid.Domain;
using Rfid.Domain.Entities;
using Rfid.Infrastructure.Persistence;

namespace Rfid.Api.Controllers;

[ApiController, Route("api/anomalies"), Authorize]
public class AnomaliesController : ControllerBase
{
    private readonly AppDbContext _db; private readonly AnomalyService _svc;
    public AnomaliesController(AppDbContext db, AnomalyService svc) { _db = db; _svc = svc; }

    [HttpGet]
    public async Task<IActionResult> List(AnomalyStatus? status = AnomalyStatus.Open, int take = 200)
    {
        var q = _db.Anomalies.AsQueryable(); if (status.HasValue) q = q.Where(a => a.Status == status);
        var rows = await q.OrderByDescending(a => a.Score).ThenByDescending(a => a.LastSeenAt).Take(Math.Clamp(take, 1, 1000)).ToListAsync();
        var devices = await _db.Devices.Where(d => rows.Select(r => r.DeviceId).Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Name);
        var items = await _db.Items.Where(i => rows.Select(r => r.ItemId).Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Name);
        var locs = await _db.Locations.Where(l => rows.Select(r => r.LocationId).Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.Name);
        return Ok(rows.Select(a => new { a.Id, a.Kind, a.Status, a.Score, a.Observed, a.Expected, a.WindowStart, a.WindowEnd, a.Message, a.DetectedAt, a.LastSeenAt, a.Occurrences, a.AlertId, a.DeviceId, Device = a.DeviceId.HasValue ? devices.GetValueOrDefault(a.DeviceId.Value) : null, a.ItemId, Item = a.ItemId.HasValue ? items.GetValueOrDefault(a.ItemId.Value) : null, a.LocationId, Location = a.LocationId.HasValue ? locs.GetValueOrDefault(a.LocationId.Value) : null, a.Details }));
    }

    [HttpPost("run"), Authorize(Policy = "Operator")]
    public async Task<IActionResult> Run(CancellationToken ct) { var (created, updated) = await _svc.RunAsync(DateTime.UtcNow, ct); return Ok(new { created, updated }); }

    [HttpPost("{id:guid}/dismiss"), Authorize(Policy = "Operator")]
    public Task<IActionResult> Dismiss(Guid id) => SetStatus(id, AnomalyStatus.Dismissed);
    [HttpPost("{id:guid}/confirm"), Authorize(Policy = "Operator")]
    public Task<IActionResult> Confirm(Guid id) => SetStatus(id, AnomalyStatus.Confirmed);
    private async Task<IActionResult> SetStatus(Guid id, AnomalyStatus status)
    {
        var a = await _db.Anomalies.FindAsync(id); if (a == null) return NotFound();
        a.Status = status;
        if (a.AlertId.HasValue && status == AnomalyStatus.Dismissed) { var al = await _db.Alerts.FindAsync(a.AlertId); if (al != null && al.Status != AlertStatus.Closed) { al.Status = AlertStatus.Closed; al.ClosedAt = DateTime.UtcNow; al.NextEscalationAt = null; } }
        await _db.SaveChangesAsync(); return Ok(new { a.Id, a.Status });
    }

    /// <summary>Hour-of-day read profile (baseline average vs today) for a device or location.</summary>
    [HttpGet("profile")]
    public async Task<IActionResult> Profile(Guid? deviceId, Guid? locationId, CancellationToken ct) => Ok(await _svc.ProfileAsync(deviceId, locationId, null, ct));
}

[ApiController, Route("api/encoding"), Authorize(Policy = "Operator")]
public class EncodingController : ControllerBase
{
    private readonly AppDbContext _db; private readonly EncodingService _svc; private readonly ICurrentContext _ctx;
    public EncodingController(AppDbContext db, EncodingService svc, ICurrentContext ctx) { _db = db; _svc = svc; _ctx = ctx; }

    [HttpGet("pools")]
    public async Task<IActionResult> Pools()
    {
        var pools = await _db.SerialPools.OrderBy(p => p.Name).ToListAsync();
        var types = await _db.ItemTypes.ToDictionaryAsync(t => t.Id, t => t.Name);
        return Ok(pools.Select(p => new { p.Id, p.Name, p.Scheme, p.CompanyPrefix, p.Reference, p.Filter, p.NextSerial, p.MaxSerial, p.ItemTypeId, ItemType = p.ItemTypeId.HasValue ? types.GetValueOrDefault(p.ItemTypeId.Value) : null, p.Notes, Remaining = p.MaxSerial.HasValue ? (long?)(long)(p.MaxSerial.Value - p.NextSerial + 1) : null, SampleEpc = Sample(p) }));
    }
    private static string? Sample(SerialPool p) { try { return EncodingService.Encode(p.Scheme, p.CompanyPrefix, p.Reference, p.Filter, p.NextSerial); } catch { return null; } }

    public record PoolWrite(string Name, EpcScheme Scheme, string CompanyPrefix, string Reference, int Filter = 1, ulong NextSerial = 1, ulong? MaxSerial = null, Guid? ItemTypeId = null, string? Notes = null);

    [HttpPost("pools"), Authorize(Policy = "Admin")]
    public async Task<IActionResult> CreatePool(PoolWrite w)
    {
        try { EncodingService.Encode(w.Scheme, w.CompanyPrefix.Trim(), w.Reference.Trim(), w.Filter, w.NextSerial); } catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        var p = new SerialPool { TenantId = _ctx.TenantId, Name = w.Name, Scheme = w.Scheme, CompanyPrefix = w.CompanyPrefix.Trim(), Reference = w.Reference.Trim(), Filter = w.Filter, NextSerial = w.NextSerial, MaxSerial = w.MaxSerial, ItemTypeId = w.ItemTypeId, Notes = w.Notes };
        _db.SerialPools.Add(p); await _db.SaveChangesAsync(); return Ok(p);
    }

    [HttpPut("pools/{id:guid}"), Authorize(Policy = "Admin")]
    public async Task<IActionResult> UpdatePool(Guid id, PoolWrite w)
    {
        var p = await _db.SerialPools.FindAsync(id); if (p == null) return NotFound();
        if (w.NextSerial < p.NextSerial) return BadRequest(new { error = "Next serial can only move forward (serials are never reused)" });
        p.Name = w.Name; p.Filter = w.Filter; p.NextSerial = w.NextSerial; p.MaxSerial = w.MaxSerial; p.ItemTypeId = w.ItemTypeId; p.Notes = w.Notes; p.Version++;
        await _db.SaveChangesAsync(); return Ok(p);
    }

    [HttpDelete("pools/{id:guid}"), Authorize(Policy = "Admin")]
    public async Task<IActionResult> DeletePool(Guid id) { var p = await _db.SerialPools.FindAsync(id); if (p == null) return NotFound(); _db.SerialPools.Remove(p); await _db.SaveChangesAsync(); return NoContent(); }

    public record AllocateWrite(int Count = 1);
    /// <summary>Reserves serials from a pool and returns the EPCs (handheld commissioning, external printers).</summary>
    [HttpPost("pools/{id:guid}/allocate")]
    public async Task<IActionResult> Allocate(Guid id, AllocateWrite w, CancellationToken ct)
    {
        var (pool, first, last) = await _svc.AllocateAsync(id, Math.Clamp(w.Count, 1, 1000), ct);
        return Ok(new { epcs = Enumerable.Range(0, (int)(last - first + 1)).Select(i => EncodingService.Encode(pool.Scheme, pool.CompanyPrefix, pool.Reference, pool.Filter, first + (ulong)i)), firstSerial = first, lastSerial = last });
    }

    [HttpPost("preview")]
    public async Task<IActionResult> Preview(EncodingRequest r, CancellationToken ct) => Ok(await _svc.PreviewAsync(r, ct));

    [HttpPost("commit")]
    public async Task<IActionResult> Commit(EncodingRequest r, CancellationToken ct) => Ok(await _svc.CommitAsync(r, ct));

    [HttpGet("batches")]
    public async Task<IActionResult> Batches(int take = 100)
    {
        var rows = await _db.EncodingBatches.OrderByDescending(b => b.CreatedAt).Take(Math.Clamp(take, 1, 500)).ToListAsync();
        var users = await _db.Users.Where(u => rows.Select(r => r.UserId).Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName);
        return Ok(rows.Select(b => new { b.Id, b.Name, b.Scheme, b.CompanyPrefix, b.Reference, b.Filter, b.FirstSerial, b.LastSerial, b.Count, b.Mode, b.TagsCreated, b.ItemsBound, b.PrintJobs, b.FirstEpc, b.LastEpc, b.CreatedAt, User = b.UserId.HasValue ? users.GetValueOrDefault(b.UserId.Value) : null }));
    }

    [HttpGet("decode/{epc}"), AllowAnonymous]
    public IActionResult Decode(string epc) => Ok(EncodingService.Decode(epc));

    /// <summary>Digit rules per scheme for the given company prefix (drives the wizard's validation).</summary>
    [HttpGet("schemes")]
    public IActionResult Schemes(string companyPrefix = "0614141")
    {
        var len = companyPrefix.Trim().Length; var ok = len is >= 6 and <= 12;
        return Ok(new[]
        {
            new { scheme = "Sgtin96", label = "SGTIN-96 · trade items (retail, apparel, pharma)", referenceLabel = "Item reference (GTIN without prefix/check digit)", referenceDigits = ok ? 13 - len : 0, serialBits = 38, filterDefault = 1 },
            new { scheme = "Grai96", label = "GRAI-96 · returnable assets (crates, kegs, totes, cylinders)", referenceLabel = "Asset type", referenceDigits = ok ? 12 - len : 0, serialBits = 38, filterDefault = 0 },
            new { scheme = "Giai96", label = "GIAI-96 · individual assets (IT, tools, furniture, vehicles)", referenceLabel = "(none – the serial is the asset reference)", referenceDigits = 0, serialBits = ok ? 82 - new[] { 40, 37, 34, 30, 27, 24, 20 }[12 - len] : 0, filterDefault = 0 },
            new { scheme = "Sscc96", label = "SSCC-96 · logistic units (pallets, cartons)", referenceLabel = "Extension digit (0–9)", referenceDigits = 1, serialBits = ok ? Rfid.Domain.Epc.Sscc96.SerialDigitsFor(companyPrefix.Trim()) - 1 : 0, filterDefault = 0 },
        });
    }
}

[ApiController, Route("api/audit"), Authorize(Policy = "Admin")]
public class AuditController : ControllerBase
{
    private readonly AppDbContext _db;
    public AuditController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<Paged<object>> List(string? q, Guid? userId, string? action, string? entityType, Guid? entityId, DateTime? from, DateTime? to, int? page, int? pageSize)
    {
        var (p, s) = Query.Page(page, pageSize);
        var query = _db.AuditEntries.AsQueryable();
        if (userId.HasValue) query = query.Where(a => a.UserId == userId);
        if (!string.IsNullOrEmpty(action)) query = query.Where(a => a.Action.StartsWith(action));
        if (!string.IsNullOrEmpty(entityType)) query = query.Where(a => a.EntityType == entityType);
        if (entityId.HasValue) query = query.Where(a => a.EntityId == entityId);
        if (from.HasValue) query = query.Where(a => a.At >= from); if (to.HasValue) query = query.Where(a => a.At <= to);
        if (!string.IsNullOrWhiteSpace(q)) { var like = $"%{q}%"; query = query.Where(a => EF.Functions.ILike(a.Path, like) || EF.Functions.ILike(a.Action, like) || (a.Body != null && EF.Functions.ILike(a.Body, like)) || (a.UserName != null && EF.Functions.ILike(a.UserName, like))); }
        var total = await query.CountAsync();
        var rows = await query.OrderByDescending(a => a.At).Skip((p - 1) * s).Take(s).ToListAsync();
        return new Paged<object>(rows.Select(a => (object)new { a.Id, a.UserId, a.UserName, a.DeviceId, a.Method, a.Path, a.Action, a.EntityType, a.EntityId, a.StatusCode, a.Body, a.IpAddress, a.UserAgent, a.At, a.DurationMs }).ToList(), total, p, s);
    }

    [HttpGet("summary")]
    public async Task<IActionResult> Summary(int days = 7)
    {
        var since = DateTime.UtcNow.AddDays(-days);
        var byUser = await _db.AuditEntries.Where(a => a.At >= since).GroupBy(a => new { a.UserId, a.UserName }).Select(g => new { g.Key.UserId, g.Key.UserName, Count = g.Count(), Failed = g.Count(a => a.StatusCode >= 400), Last = g.Max(a => a.At) }).OrderByDescending(x => x.Count).Take(20).ToListAsync();
        var byAction = await _db.AuditEntries.Where(a => a.At >= since).GroupBy(a => a.Action).Select(g => new { Action = g.Key, Count = g.Count() }).OrderByDescending(x => x.Count).Take(15).ToListAsync();
        var actions = await _db.AuditEntries.Select(a => a.Action).Distinct().OrderBy(a => a).ToListAsync();
        return Ok(new { days, total = await _db.AuditEntries.CountAsync(a => a.At >= since), byUser, byAction, actions });
    }
}

[ApiController, Route("api/retention"), Authorize(Policy = "Admin")]
public class RetentionController : ControllerBase
{
    private readonly RetentionService _svc; private readonly AppDbContext _db;
    public RetentionController(RetentionService svc, AppDbContext db) { _svc = svc; _db = db; }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var policies = await _svc.EnsureDefaultsAsync(ct);
        var counts = await _svc.CountsAsync(DateTime.UtcNow, ct);
        return Ok(policies.Select(p => { var info = RetentionService.Datasets.First(d => d.Dataset == p.Dataset); var c = counts.GetValueOrDefault(p.Dataset); return new { p.Id, p.Dataset, info.Description, info.Protected, p.RetainDays, p.Enabled, p.LastRunAt, p.LastDeleted, p.TotalDeleted, Rows = c.total, Expired = c.expired }; }));
    }

    public record PolicyWrite(string Dataset, int? RetainDays, bool Enabled);
    [HttpPut]
    public async Task<IActionResult> Put(List<PolicyWrite> writes, CancellationToken ct)
    {
        var policies = await _svc.EnsureDefaultsAsync(ct);
        foreach (var w in writes)
        {
            var p = policies.FirstOrDefault(x => x.Dataset == w.Dataset); if (p == null) continue;
            if (w.RetainDays is < 1) return BadRequest(new { error = "Retention must be at least 1 day (or blank for forever)" });
            p.RetainDays = w.RetainDays; p.Enabled = w.Enabled && w.RetainDays != null;
        }
        await _db.SaveChangesAsync(ct);
        return await Get(ct);
    }

    [HttpPost("run")]
    public async Task<IActionResult> Run(CancellationToken ct) => Ok(await _svc.ApplyAsync(DateTime.UtcNow, ct));
}
