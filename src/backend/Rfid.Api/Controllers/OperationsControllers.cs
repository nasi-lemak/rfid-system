using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rfid.Application.Contracts;
using Rfid.Application.Services;
using Rfid.Application.Templates;
using Rfid.Domain;
using Rfid.Domain.Entities;
using Rfid.Infrastructure.Persistence;

namespace Rfid.Api.Controllers;

[ApiController, Route("api/operations"), Authorize]
public class OperationsController : ControllerBase
{
    private readonly AppDbContext _db; private readonly OperationProcessor _processor;
    public OperationsController(AppDbContext db, OperationProcessor processor) { _db = db; _processor = processor; }

    /// <summary>Executes an operation (receive, transfer, issue, return, count, dispatch, inspect, maintain, dispose, pack, unpack, processStage, commission, adjust).</summary>
    [HttpPost, Authorize(Policy = "Operator")]
    public async Task<ActionResult<OperationResult>> Process(OperationRequest req, CancellationToken ct) => Ok(await _processor.ProcessAsync(req, ct));

    /// <summary>Batch endpoint for offline handhelds syncing a queue of operations.</summary>
    [HttpPost("batch"), Authorize(Policy = "Operator")]
    public async Task<IActionResult> Batch(List<OperationRequest> reqs, CancellationToken ct)
    {
        var results = new List<object>();
        foreach (var r in reqs)
        {
            try { results.Add(new { clientId = r.ClientId, ok = true, result = await _processor.ProcessAsync(r, ct) }); }
            catch (DomainException ex) { results.Add(new { clientId = r.ClientId, ok = false, error = ex.Message }); }
        }
        return Ok(results);
    }

    [HttpGet]
    public async Task<Paged<object>> List(OperationType? type, int? page, int? pageSize)
    {
        var (p, s) = Query.Page(page, pageSize);
        var q = _db.Operations.Include(o => o.Lines).AsQueryable();
        if (type.HasValue) q = q.Where(o => o.Type == type);
        var total = await q.CountAsync();
        var ops = await q.OrderByDescending(o => o.StartedAt).Skip((p - 1) * s).Take(s).ToListAsync();
        var locIds = ops.SelectMany(o => new[] { o.FromLocationId, o.ToLocationId }).Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList();
        var locs = await _db.Locations.Where(l => locIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.Name);
        var partyIds = ops.Where(o => o.PartyId.HasValue).Select(o => o.PartyId!.Value).Distinct().ToList();
        var parties = await _db.Parties.Where(x => partyIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name);
        var userIds = ops.Where(o => o.UserId.HasValue).Select(o => o.UserId!.Value).Distinct().ToList();
        var users = await _db.Users.Where(x => userIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.DisplayName);
        return new Paged<object>(ops.Select(o => (object)new
        {
            o.Id, o.Type, o.Status, o.FromLocationId, FromLocation = o.FromLocationId.HasValue ? locs.GetValueOrDefault(o.FromLocationId.Value) : null,
            o.ToLocationId, ToLocation = o.ToLocationId.HasValue ? locs.GetValueOrDefault(o.ToLocationId.Value) : null,
            o.PartyId, Party = o.PartyId.HasValue ? parties.GetValueOrDefault(o.PartyId.Value) : null, o.TargetState, o.Reference, o.Notes, o.DeviceId,
            o.UserId, User = o.UserId.HasValue ? users.GetValueOrDefault(o.UserId.Value) : null, o.StartedAt, o.CompletedAt,
            LineCount = o.Lines.Count, Ok = o.Lines.Count(l => l.Result == LineResult.Ok), Rejected = o.Lines.Count(l => l.Result == LineResult.Rejected), Unknown = o.Lines.Count(l => l.Result == LineResult.Unknown),
        }).ToList(), total, p, s);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var o = await _db.Operations.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id); if (o == null) return NotFound();
        var itemIds = o.Lines.Where(l => l.ItemId.HasValue).Select(l => l.ItemId!.Value).ToList();
        var items = await _db.Items.Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id);
        return Ok(new
        {
            o.Id, o.Type, o.Status, o.FromLocationId, o.ToLocationId, o.PartyId, o.ContainerItemId, o.TargetState, o.Reference, o.Notes, o.DeviceId, o.UserId, o.StartedAt, o.CompletedAt, o.DueBackAt,
            Lines = o.Lines.Select(l => new { l.Id, l.Epc, l.ItemId, ItemName = l.ItemId.HasValue ? items.GetValueOrDefault(l.ItemId.Value)?.Name : null, ItemIdentifier = l.ItemId.HasValue ? items.GetValueOrDefault(l.ItemId.Value)?.Identifier : null, l.Quantity, l.Result, l.Message }),
        });
    }
}

[ApiController, Route("api/ingest"), Authorize(Policy = "Operator")]
public class IngestController : ControllerBase
{
    private readonly ReadIngestionService _svc;
    public IngestController(ReadIngestionService svc) => _svc = svc;

    /// <summary>Fixed readers, portals, cabinets and gateways post batches of reads here (device JWT).</summary>
    [HttpPost("reads")]
    public async Task<ActionResult<IngestResult>> Reads(ReadBatchRequest batch, CancellationToken ct) => Ok(await _svc.IngestAsync(batch, ReadSource.Fixed, ct));

    /// <summary>Handheld free-scan (inventory mode) reads: same pipeline, tagged as handheld, location taken from the request.</summary>
    [HttpPost("handheld")]
    public async Task<ActionResult<IngestResult>> Handheld(ReadBatchRequest batch, CancellationToken ct) => Ok(await _svc.IngestAsync(batch, ReadSource.Handheld, ct));
}

[ApiController, Route("api/stocktakes"), Authorize]
public class StocktakesController : ControllerBase
{
    private readonly AppDbContext _db; private readonly StocktakeService _svc;
    public StocktakesController(AppDbContext db, StocktakeService svc) { _db = db; _svc = svc; }

    public record CreateRequest(string Name, Guid LocationId, Guid? ItemTypeId);

    [HttpPost, Authorize(Policy = "Operator")]
    public async Task<IActionResult> Create(CreateRequest r, CancellationToken ct) => Ok(StocktakeService.Summarize(await _svc.CreateAsync(r.Name, r.LocationId, r.ItemTypeId, ct)));

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var sts = await _db.Stocktakes.Include(s => s.Lines).OrderByDescending(s => s.StartedAt).Take(200).ToListAsync();
        var locs = await _db.Locations.ToDictionaryAsync(l => l.Id, l => l.Name);
        return Ok(sts.Select(s => new { Summary = StocktakeService.Summarize(s), s.LocationId, Location = locs.GetValueOrDefault(s.LocationId), s.ItemTypeId, s.StartedAt, s.CompletedAt }));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var s = await _db.Stocktakes.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id); if (s == null) return NotFound();
        var ids = s.Lines.Where(l => l.ItemId.HasValue).Select(l => l.ItemId!.Value).ToList();
        var items = await _db.Items.Include(i => i.ItemType).Include(i => i.CurrentLocation).Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id);
        var loc = await _db.Locations.FindAsync(s.LocationId);
        return Ok(new
        {
            Summary = StocktakeService.Summarize(s), s.LocationId, Location = loc?.Name, s.ItemTypeId, s.StartedAt, s.CompletedAt,
            Lines = s.Lines.OrderBy(l => l.Result).ThenBy(l => l.ItemId).Select(l =>
            {
                var it = l.ItemId.HasValue ? items.GetValueOrDefault(l.ItemId.Value) : null;
                return new { l.Id, l.ItemId, l.Epc, l.Expected, l.Result, l.FoundAt, l.FoundLocationId, ItemName = it?.Name, ItemIdentifier = it?.Identifier, ItemType = it?.ItemType?.Name, ExpectedLocation = it?.CurrentLocation?.Name };
            }),
        });
    }

    [HttpPost("{id:guid}/scans"), Authorize(Policy = "Operator")]
    public async Task<IActionResult> Scans(Guid id, StocktakeScanRequest r, CancellationToken ct) => Ok(await _svc.AddScansAsync(id, r, ct));

    [HttpPost("{id:guid}/reconcile"), Authorize(Policy = "Operator")]
    public async Task<IActionResult> Reconcile(Guid id, CancellationToken ct) => Ok(await _svc.ReconcileAsync(id, ct));

    [HttpPost("{id:guid}/apply"), Authorize(Policy = "Operator")]
    public async Task<IActionResult> Apply(Guid id, CancellationToken ct) => Ok(await _svc.ApplyAsync(id, ct));

    [HttpPost("{id:guid}/cancel"), Authorize(Policy = "Operator")]
    public async Task<IActionResult> Cancel(Guid id)
    {
        var s = await _db.Stocktakes.FindAsync(id); if (s == null) return NotFound();
        s.Status = StocktakeStatus.Cancelled; s.CompletedAt = DateTime.UtcNow; await _db.SaveChangesAsync(); return Ok();
    }
}

[ApiController, Route("api/rules"), Authorize]
public class RulesController : ControllerBase
{
    private readonly AppDbContext _db; private readonly ICurrentContext _ctx;
    public RulesController(AppDbContext db, ICurrentContext ctx) { _db = db; _ctx = ctx; }

    [HttpGet]
    public async Task<IActionResult> List() => Ok(await _db.Rules.OrderBy(r => r.Name).ToListAsync());

    [HttpPost, Authorize(Policy = "Admin")]
    public async Task<IActionResult> Create(Rule r) { r.Id = Guid.NewGuid(); r.TenantId = _ctx.TenantId; r.CreatedAt = DateTime.UtcNow; _db.Rules.Add(r); await _db.SaveChangesAsync(); return Ok(r); }

    [HttpPut("{id:guid}"), Authorize(Policy = "Admin")]
    public async Task<IActionResult> Update(Guid id, Rule r)
    {
        var e = await _db.Rules.FindAsync(id); if (e == null) return NotFound();
        e.Name = r.Name; e.Enabled = r.Enabled; e.Trigger = r.Trigger; e.Conditions = r.Conditions; e.Action = r.Action; e.Params = r.Params; e.Severity = r.Severity;
        await _db.SaveChangesAsync(); return Ok(e);
    }

    [HttpDelete("{id:guid}"), Authorize(Policy = "Admin")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var e = await _db.Rules.FindAsync(id); if (e == null) return NotFound();
        _db.Rules.Remove(e); await _db.SaveChangesAsync(); return NoContent();
    }
}

[ApiController, Route("api/alerts"), Authorize]
public class AlertsController : ControllerBase
{
    private readonly AppDbContext _db; private readonly ICurrentContext _ctx;
    public AlertsController(AppDbContext db, ICurrentContext ctx) { _db = db; _ctx = ctx; }

    [HttpGet]
    public async Task<IActionResult> List(AlertStatus? status = AlertStatus.Open, int take = 200)
    {
        var q = _db.Alerts.AsQueryable();
        if (status.HasValue) q = q.Where(a => a.Status == status);
        var alerts = await q.OrderByDescending(a => a.RaisedAt).Take(take).ToListAsync();
        var ids = alerts.Where(a => a.ItemId.HasValue).Select(a => a.ItemId!.Value).Distinct().ToList();
        var items = await _db.Items.Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id);
        return Ok(alerts.Select(a => Dto.Alert(a, a.ItemId.HasValue ? items.GetValueOrDefault(a.ItemId.Value) : null)));
    }

    [HttpPost("{id:guid}/acknowledge"), Authorize(Policy = "Operator")]
    public async Task<IActionResult> Ack(Guid id)
    {
        var a = await _db.Alerts.FindAsync(id); if (a == null) return NotFound();
        a.Status = AlertStatus.Acknowledged; a.AcknowledgedBy = _ctx.UserId; await _db.SaveChangesAsync(); return Ok(Dto.Alert(a));
    }

    [HttpPost("{id:guid}/close"), Authorize(Policy = "Operator")]
    public async Task<IActionResult> Close(Guid id)
    {
        var a = await _db.Alerts.FindAsync(id); if (a == null) return NotFound();
        a.Status = AlertStatus.Closed; a.ClosedAt = DateTime.UtcNow; a.AcknowledgedBy ??= _ctx.UserId; await _db.SaveChangesAsync(); return Ok(Dto.Alert(a));
    }
}

[ApiController, Route("api/templates"), Authorize]
public class TemplatesController : ControllerBase
{
    private readonly AppDbContext _db; private readonly TemplateProvisioner _prov; private readonly Rfid.Infrastructure.Persistence.Demo.DemoSeeder _demo;
    public TemplatesController(AppDbContext db, TemplateProvisioner prov, Rfid.Infrastructure.Persistence.Demo.DemoSeeder demo) { _db = db; _prov = prov; _demo = demo; }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var tpls = await _db.SolutionTemplates.OrderBy(t => t.Vertical).ThenBy(t => t.Name).ToListAsync();
        var existing = await _db.ItemTypes.Select(t => t.Code).ToListAsync();
        var seededSites = await _db.Locations.Where(l => l.Kind == LocationKind.Site).Select(l => l.Code).ToListAsync();
        return Ok(tpls.Select(t => new
        {
            t.Id, t.Code, t.Name, t.Vertical, t.Description,
            HasDemo = Rfid.Infrastructure.Persistence.Demo.DemoSeeder.Find(t.Code) != null,
            DemoSeeded = Rfid.Infrastructure.Persistence.Demo.DemoSeeder.Find(t.Code) is { } sc && seededSites.Contains(sc.SiteCode),
            DemoSite = Rfid.Infrastructure.Persistence.Demo.DemoSeeder.Find(t.Code)?.Site,
            ItemTypes = t.Definition.ItemTypes.Select(i => new { i.Name, i.Code, i.Category, i.IsContainer, i.TracksCycles, i.TracksExpiry, i.RequiresInspection, States = i.Lifecycle?.States, Installed = existing.Contains(i.Code) }),
            Rules = t.Definition.Rules.Select(r => new { r.Name, r.Trigger, r.Severity, r.Action }),
            Operations = t.Definition.Operations, t.Definition.LocationKinds,
            Installed = t.Definition.ItemTypes.All(i => existing.Contains(i.Code)),
        }));
    }

    [HttpGet("{code}")]
    public async Task<IActionResult> Get(string code) => await _db.SolutionTemplates.FirstOrDefaultAsync(t => t.Code == code) is { } t ? Ok(t) : NotFound();

    [HttpPost("{code}/apply"), Authorize(Policy = "Admin")]
    public async Task<IActionResult> Apply(string code, CancellationToken ct) => Ok(await _prov.ApplyAsync(code, ct));

    /// <summary>Installs the template and loads its demo dataset (site, locations, parties, tagged items, readers, history) into the current tenant.</summary>
    [HttpPost("{code}/demo"), Authorize(Policy = "Admin")]
    public async Task<IActionResult> Demo(string code, CancellationToken ct)
    {
        var sc = Rfid.Infrastructure.Persistence.Demo.DemoSeeder.Find(code);
        if (sc == null) return NotFound(new { error = $"No demo scenario for template {code}" });
        return Ok(await _demo.SeedAsync(sc, ct));
    }
}

[ApiController, Route("api/events"), Authorize]
public class EventsController : ControllerBase
{
    private readonly AppDbContext _db;
    public EventsController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> Recent(ItemEventType? type, Guid? locationId, int take = 100)
    {
        var q = _db.ItemEvents.AsQueryable();
        if (type.HasValue) q = q.Where(e => e.Type == type);
        if (locationId.HasValue) q = q.Where(e => e.ToLocationId == locationId || e.FromLocationId == locationId);
        var events = await q.OrderByDescending(e => e.OccurredAt).Take(Math.Clamp(take, 1, 1000)).ToListAsync();
        var ids = events.Select(e => e.ItemId).Distinct().ToList();
        var items = await _db.Items.Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id);
        return Ok(events.Select(e => Dto.Event(e, items.GetValueOrDefault(e.ItemId))));
    }

    [HttpGet("reads")]
    public async Task<IActionResult> Reads(Guid? deviceId, int take = 200)
    {
        var q = _db.TagReads.AsQueryable();
        if (deviceId.HasValue) q = q.Where(r => r.DeviceId == deviceId);
        return Ok(await q.OrderByDescending(r => r.ReadAt).Take(Math.Clamp(take, 1, 2000)).Select(r => new { r.Id, r.Epc, r.ItemId, r.DeviceId, r.AntennaPort, r.Rssi, r.ReadAt, r.LocationId, r.Source }).ToListAsync());
    }
}

[ApiController, Route("api/dashboard"), Authorize]
public class DashboardController : ControllerBase
{
    private readonly AppDbContext _db;
    public DashboardController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> Summary()
    {
        var now = DateTime.UtcNow;
        var items = _db.Items.Where(i => i.Status != ItemStatus.Disposed);
        var byStatus = await _db.Items.GroupBy(i => i.Status).Select(g => new { Status = g.Key.ToString(), Count = g.Count() }).ToListAsync();
        var byType = await items.Include(i => i.ItemType).GroupBy(i => i.ItemType!.Name).Select(g => new { Type = g.Key, Count = g.Count() }).OrderByDescending(x => x.Count).Take(12).ToListAsync();
        var byState = await items.Where(i => i.State != null).GroupBy(i => i.State!).Select(g => new { State = g.Key, Count = g.Count() }).OrderByDescending(x => x.Count).Take(12).ToListAsync();
        var byLocation = await items.Include(i => i.CurrentLocation).Where(i => i.CurrentLocation != null).GroupBy(i => i.CurrentLocation!.Name).Select(g => new { Location = g.Key, Count = g.Count() }).OrderByDescending(x => x.Count).Take(10).ToListAsync();
        var since = now.AddDays(-14);
        var readsPerDay = await _db.TagReads.Where(r => r.ReadAt >= since).GroupBy(r => r.ReadAt.Date).Select(g => new { Day = g.Key, Count = g.Count() }).OrderBy(x => x.Day).ToListAsync();
        var opsPerDay = await _db.Operations.Where(o => o.StartedAt >= since).GroupBy(o => o.StartedAt.Date).Select(g => new { Day = g.Key, Count = g.Count() }).OrderBy(x => x.Day).ToListAsync();
        return Ok(new
        {
            totals = new
            {
                items = await items.CountAsync(),
                tags = await _db.Tags.CountAsync(t => t.Status == TagStatus.Active),
                locations = await _db.Locations.CountAsync(),
                devices = await _db.Devices.CountAsync(),
                openAlerts = await _db.Alerts.CountAsync(a => a.Status == AlertStatus.Open),
                criticalAlerts = await _db.Alerts.CountAsync(a => a.Status == AlertStatus.Open && a.Severity == Severity.Critical),
                inCustody = await items.CountAsync(i => i.CustodianPartyId != null),
                overdue = await items.CountAsync(i => i.DueBackAt != null && i.DueBackAt < now),
                missing = await _db.Items.CountAsync(i => i.Status == ItemStatus.Missing),
                expiring30d = await items.CountAsync(i => i.ExpiryDate != null && i.ExpiryDate <= DateOnly.FromDateTime(now.AddDays(30))),
                inspectionDue14d = await items.CountAsync(i => i.NextInspectionDue != null && i.NextInspectionDue <= now.AddDays(14)),
                notSeen7d = await items.CountAsync(i => i.LastSeenAt == null || i.LastSeenAt < now.AddDays(-7)),
                readsToday = await _db.TagReads.CountAsync(r => r.ReadAt >= now.Date),
                operationsToday = await _db.Operations.CountAsync(o => o.StartedAt >= now.Date),
                openStocktakes = await _db.Stocktakes.CountAsync(s => s.Status == StocktakeStatus.Open),
            },
            byStatus, byType, byState, byLocation, readsPerDay, opsPerDay,
        });
    }
}
