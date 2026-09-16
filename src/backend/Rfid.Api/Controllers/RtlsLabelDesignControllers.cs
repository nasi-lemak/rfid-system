using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rfid.Application.Labels;
using Rfid.Application.Services;
using Rfid.Domain;
using Rfid.Domain.Entities;
using Rfid.Infrastructure.Persistence;

namespace Rfid.Api.Controllers;

[ApiController, Route("api/positions"), Authorize]
public class PositionsController : ControllerBase
{
    private readonly PositionService _svc; private readonly AppDbContext _db;
    public PositionsController(PositionService svc, AppDbContext db) { _svc = svc; _db = db; }

    /// <summary>Locations that have positioned antennas (anchors) – i.e. floor plans worth drawing.</summary>
    [HttpGet("floor-plans")]
    public async Task<IActionResult> FloorPlans()
    {
        var ids = await _db.Antennas.Where(a => a.X != null && a.LocationId != null).Select(a => a.LocationId!.Value).Distinct().ToListAsync();
        var locs = await _db.Locations.Where(l => ids.Contains(l.Id)).OrderBy(l => l.Name).ToListAsync();
        return Ok(locs.Select(l => new { l.Id, l.Name, l.Kind, Bounds = PositionService.Bounds(l) is { } b ? new { w = b.w, h = b.h } : null }));
    }

    /// <summary>Anchors and last-known item positions inside a location (maxAgeMinutes default 720).</summary>
    [HttpGet("floor-plans/{locationId:guid}")]
    public async Task<IActionResult> FloorPlan(Guid locationId, int maxAgeMinutes = 720, CancellationToken ct = default) => Ok(await _svc.FloorPlanAsync(locationId, TimeSpan.FromMinutes(maxAgeMinutes), ct));

    /// <summary>Dwell-time heat map for a floor plan: seconds spent per grid cell in the window (default last 24h, 1 m cells).</summary>
    [HttpGet("heatmap")]
    public async Task<IActionResult> HeatMap(Guid locationId, DateTime? from, DateTime? to, double cellM = 1.0, CancellationToken ct = default)
    {
        var t = to ?? DateTime.UtcNow; var f = from ?? t.AddHours(-24);
        return Ok(await _svc.HeatMapAsync(locationId, f, t, Math.Clamp(cellM, 0.25, 20), ct));
    }

    /// <summary>Recorded path of an item (position fixes) in the window, oldest first.</summary>
    [HttpGet("history")]
    public async Task<IActionResult> History(Guid itemId, DateTime? from, DateTime? to, int take = 5000, CancellationToken ct = default)
    {
        var t = to ?? DateTime.UtcNow; var f = from ?? t.AddHours(-24);
        var points = await _svc.HistoryAsync(itemId, f, t, Math.Clamp(take, 1, 20000), ct);
        var item = await _db.Items.Where(i => i.Id == itemId).Select(i => new { i.Id, i.Name, i.Identifier }).FirstOrDefaultAsync(ct);
        return Ok(new { Item = item, From = f, To = t, Points = points });
    }

    /// <summary>Items that have recorded fixes in a location during the window – candidates for path replay.</summary>
    [HttpGet("history/items")]
    public async Task<IActionResult> HistoryItems(Guid locationId, DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        var t = to ?? DateTime.UtcNow; var f = from ?? t.AddHours(-24);
        var rows = await _db.PositionFixes.Where(p => p.LocationId == locationId && p.At >= f && p.At <= t).GroupBy(p => p.ItemId)
            .Select(g => new { ItemId = g.Key, Fixes = g.Count(), First = g.Min(p => p.At), Last = g.Max(p => p.At) }).OrderByDescending(x => x.Fixes).Take(200).ToListAsync(ct);
        var ids = rows.Select(r => r.ItemId).ToList();
        var names = await _db.Items.Where(i => ids.Contains(i.Id)).Select(i => new { i.Id, i.Name, i.Identifier }).ToDictionaryAsync(i => i.Id, ct);
        return Ok(rows.Select(r => new { r.ItemId, Name = names.GetValueOrDefault(r.ItemId)?.Name, Identifier = names.GetValueOrDefault(r.ItemId)?.Identifier, r.Fixes, r.First, r.Last }));
    }
}

[ApiController, Route("api/labels/designs"), Authorize(Policy = "Operator")]
public class LabelDesignsController : ControllerBase
{
    private readonly AppDbContext _db;
    public LabelDesignsController(AppDbContext db) => _db = db;

    [HttpGet("default")] public IActionResult Default() => Ok(LabelDesign.Default());

    /// <summary>Compiles a design to ZPL and renders a preview for a sample (or a given) item.</summary>
    [HttpPost("compile")]
    public async Task<IActionResult> Compile([FromBody] LabelDesign design, Guid? itemId)
    {
        var zpl = LabelCompiler.Compile(design);
        Item? item = itemId.HasValue ? await _db.Items.Include(i => i.ItemType).Include(i => i.CurrentLocation).Include(i => i.Tags).FirstOrDefaultAsync(i => i.Id == itemId) : null;
        item ??= new Item { Name = "Sample item", Identifier = "SAMPLE-0001", ItemType = new ItemType { Name = "Sample type", Code = "SAMPLE" }, CurrentLocation = new Location { Name = "Sample location" }, Attributes = new() { ["serialNumber"] = "SN-1" } };
        var epc = item.Tags.FirstOrDefault()?.Epc ?? "3034F8B2000000000000ABCD";
        return Ok(new { template = zpl, rendered = LabelService.Render(item, epc, zpl) });
    }

    /// <summary>Saves a design on an item type (stores both the design JSON and the compiled ZPL template).</summary>
    [HttpPut("item-types/{itemTypeId:guid}")]
    public async Task<IActionResult> Save(Guid itemTypeId, [FromBody] LabelDesign design)
    {
        var t = await _db.ItemTypes.FindAsync(itemTypeId); if (t == null) return NotFound();
        t.LabelDesign = design.ToJson(); t.LabelTemplate = LabelCompiler.Compile(design);
        await _db.SaveChangesAsync();
        return Ok(new { t.Id, t.LabelTemplate, design });
    }

    [HttpGet("item-types/{itemTypeId:guid}")]
    public async Task<IActionResult> Get(Guid itemTypeId)
    {
        var t = await _db.ItemTypes.FindAsync(itemTypeId); if (t == null) return NotFound();
        return Ok(LabelDesign.Parse(t.LabelDesign) ?? LabelDesign.Default());
    }

    [HttpDelete("item-types/{itemTypeId:guid}")]
    public async Task<IActionResult> Reset(Guid itemTypeId)
    {
        var t = await _db.ItemTypes.FindAsync(itemTypeId); if (t == null) return NotFound();
        t.LabelDesign = null; t.LabelTemplate = null; await _db.SaveChangesAsync(); return NoContent();
    }
}
