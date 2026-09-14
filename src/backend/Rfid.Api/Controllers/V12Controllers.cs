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
