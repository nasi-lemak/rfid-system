using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rfid.Application.Contracts;
using Rfid.Application.Security;
using Rfid.Application.Services;
using Rfid.Domain;
using Rfid.Domain.Entities;
using Rfid.Infrastructure.Persistence;

namespace Rfid.Api.Controllers;

[ApiController, Route("api/items"), Authorize]
public class ItemsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ICurrentContext _ctx; private readonly ISiteAccess _sites;
    public ItemsController(AppDbContext db, ICurrentContext ctx, ISiteAccess sites) { _db = db; _ctx = ctx; _sites = sites; }

    private IQueryable<Item> Base() => _db.Items.Include(i => i.ItemType).Include(i => i.CurrentLocation).Include(i => i.CustodianParty).Include(i => i.ParentItem).Include(i => i.Tags);

    [HttpGet]
    public async Task<Paged<object>> List(string? q, Guid? itemTypeId, Guid? locationId, bool includeSubtree = true, string? state = null, ItemStatus? status = null,
        Guid? custodianPartyId = null, Guid? parentItemId = null, string? filter = null, int? page = null, int? pageSize = null)
    {
        var (p, s) = Query.Page(page, pageSize);
        var query = SiteAccess.Filter(Base(), await _sites.AllowedPathsAsync());
        if (!string.IsNullOrWhiteSpace(q))
        {
            var like = $"%{q}%";
            query = query.Where(i => EF.Functions.ILike(i.Name, like) || EF.Functions.ILike(i.Identifier, like) || i.Tags.Any(t => EF.Functions.ILike(t.Epc, like)) || (i.LotNumber != null && EF.Functions.ILike(i.LotNumber, like)));
        }
        if (itemTypeId.HasValue) query = query.Where(i => i.ItemTypeId == itemTypeId);
        if (locationId.HasValue)
        {
            if (includeSubtree)
            {
                var loc = await _db.Locations.FindAsync(locationId.Value);
                if (loc != null) query = query.Where(i => i.CurrentLocation != null && i.CurrentLocation.Path.StartsWith(loc.Path));
            }
            else query = query.Where(i => i.CurrentLocationId == locationId);
        }
        if (!string.IsNullOrEmpty(state)) query = query.Where(i => i.State == state);
        if (status.HasValue) query = query.Where(i => i.Status == status);
        if (custodianPartyId.HasValue) query = query.Where(i => i.CustodianPartyId == custodianPartyId);
        if (parentItemId.HasValue) query = query.Where(i => i.ParentItemId == parentItemId);
        var now = DateTime.UtcNow;
        query = filter switch
        {
            "expiring" => query.Where(i => i.ExpiryDate != null && i.ExpiryDate <= DateOnly.FromDateTime(now.AddDays(30))),
            "inspectionDue" => query.Where(i => i.NextInspectionDue != null && i.NextInspectionDue <= now.AddDays(14)),
            "overdue" => query.Where(i => i.DueBackAt != null && i.DueBackAt < now),
            "inCustody" => query.Where(i => i.CustodianPartyId != null),
            "notSeen7d" => query.Where(i => i.LastSeenAt == null || i.LastSeenAt < now.AddDays(-7)),
            "lowStock" => query.Where(i => i.ItemType!.ReorderPoint != null && i.Quantity < i.ItemType.ReorderPoint),
            _ => query,
        };
        var total = await query.CountAsync();
        var items = await query.OrderBy(i => i.Name).Skip((p - 1) * s).Take(s).ToListAsync();
        return new Paged<object>(items.Select(Dto.Item).ToList(), total, p, s);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var item = await Base().FirstOrDefaultAsync(i => i.Id == id);
        if (item == null) return NotFound();
        var children = await _db.Items.Include(i => i.ItemType).Where(i => i.ParentItemId == id).Select(i => new { i.Id, i.Name, i.Identifier, i.State, ItemType = i.ItemType!.Name }).ToListAsync();
        return Ok(new { item = Dto.Item(item), children });
    }

    [HttpGet("by-epc/{epc}")]
    public async Task<IActionResult> ByEpc(string epc)
    {
        var e = TagResolver.Normalize(epc);
        var tag = await _db.Tags.FirstOrDefaultAsync(t => t.Epc == e);
        if (tag == null) return NotFound(new { error = "Unknown tag", epc = e });
        if (tag.ItemId == null) return Ok(new { tag = new { tag.Id, tag.Epc, tag.Status }, item = (object?)null });
        var item = await Base().FirstOrDefaultAsync(i => i.Id == tag.ItemId);
        return Ok(new { tag = new { tag.Id, tag.Epc, tag.Status }, item = item == null ? null : Dto.Item(item) });
    }

    [HttpGet("{id:guid}/events")]
    public async Task<IActionResult> Events(Guid id, int take = 100)
    {
        var events = await _db.ItemEvents.Where(e => e.ItemId == id).OrderByDescending(e => e.OccurredAt).Take(take).ToListAsync();
        return Ok(events.Select(e => Dto.Event(e)));
    }

    public class ItemWrite
    {
        public Guid ItemTypeId { get; set; }
        public string Identifier { get; set; } = "";
        public string Name { get; set; } = "";
        public string? State { get; set; }
        public Guid? CurrentLocationId { get; set; }
        public Guid? CustodianPartyId { get; set; }
        public Guid? ParentItemId { get; set; }
        public decimal? Quantity { get; set; }
        public string? LotNumber { get; set; }
        public DateOnly? ExpiryDate { get; set; }
        public decimal? Cost { get; set; }
        public DateOnly? PurchasedAt { get; set; }
        public Dictionary<string, object?>? Attributes { get; set; }
        public string? Epc { get; set; }
        public TagTechnology? Technology { get; set; }
    }

    [HttpPost, Authorize(Policy = "Operator")]
    public async Task<IActionResult> Create(ItemWrite w)
    {
        var type = await _db.ItemTypes.FindAsync(w.ItemTypeId);
        if (type == null) return BadRequest(new { error = "Item type not found" });
        if (await _db.Items.AnyAsync(i => i.Identifier == w.Identifier)) return Conflict(new { error = "Identifier already exists" });
        var state = w.State ?? type.Lifecycle?.Initial;
        if (type.Lifecycle != null && !type.Lifecycle.IsValidState(state)) return BadRequest(new { error = $"Invalid state {state}" });
        var item = new Item
        {
            TenantId = _ctx.TenantId, ItemTypeId = type.Id, Identifier = w.Identifier, Name = string.IsNullOrEmpty(w.Name) ? w.Identifier : w.Name, State = state,
            CurrentLocationId = w.CurrentLocationId, CustodianPartyId = w.CustodianPartyId, ParentItemId = w.ParentItemId, Quantity = w.Quantity ?? 1, Unit = type.Unit,
            LotNumber = w.LotNumber, ExpiryDate = w.ExpiryDate, Cost = w.Cost, PurchasedAt = w.PurchasedAt, Attributes = w.Attributes ?? new(),
            NextInspectionDue = type.RequiresInspection && type.InspectionIntervalDays is int d ? DateTime.UtcNow.AddDays(d) : null,
        };
        _db.Items.Add(item);
        _db.ItemEvents.Add(new ItemEvent { TenantId = _ctx.TenantId, ItemId = item.Id, Type = ItemEventType.Created, ToLocationId = item.CurrentLocationId, ToState = state, UserId = _ctx.UserId });
        if (!string.IsNullOrWhiteSpace(w.Epc))
        {
            var epc = TagResolver.Normalize(w.Epc);
            var tag = await _db.Tags.FirstOrDefaultAsync(t => t.Epc == epc);
            if (tag?.ItemId != null) return Conflict(new { error = $"EPC {epc} already bound to another item" });
            tag ??= new Tag { TenantId = _ctx.TenantId, Epc = epc, Technology = w.Technology ?? TagTechnology.UhfGen2 };
            tag.ItemId = item.Id; tag.Status = TagStatus.Active; tag.EncodedAt = DateTime.UtcNow;
            if (_db.Entry(tag).State == EntityState.Detached) _db.Tags.Add(tag);
            _db.ItemEvents.Add(new ItemEvent { TenantId = _ctx.TenantId, ItemId = item.Id, Type = ItemEventType.Commissioned, UserId = _ctx.UserId, Data = new() { ["epc"] = epc } });
        }
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = item.Id }, Dto.Item((await Base().FirstAsync(i => i.Id == item.Id))));
    }

    [HttpPut("{id:guid}"), Authorize(Policy = "Operator")]
    public async Task<IActionResult> Update(Guid id, ItemWrite w)
    {
        var item = await Base().FirstOrDefaultAsync(i => i.Id == id);
        if (item == null) return NotFound();
        var prevLoc = item.CurrentLocationId; var prevParty = item.CustodianPartyId; var prevState = item.State;
        item.Name = w.Name; item.Identifier = w.Identifier;
        if (w.State != null) item.State = w.State;
        item.CurrentLocationId = w.CurrentLocationId; item.CustodianPartyId = w.CustodianPartyId; item.ParentItemId = w.ParentItemId;
        if (w.Quantity.HasValue) item.Quantity = w.Quantity.Value;
        item.LotNumber = w.LotNumber; item.ExpiryDate = w.ExpiryDate; item.Cost = w.Cost; item.PurchasedAt = w.PurchasedAt;
        if (w.Attributes != null) item.Attributes = w.Attributes;
        if (prevLoc != item.CurrentLocationId) _db.ItemEvents.Add(new ItemEvent { TenantId = _ctx.TenantId, ItemId = id, Type = ItemEventType.Moved, FromLocationId = prevLoc, ToLocationId = item.CurrentLocationId, UserId = _ctx.UserId, Data = new() { ["manual"] = true } });
        if (prevParty != item.CustodianPartyId) _db.ItemEvents.Add(new ItemEvent { TenantId = _ctx.TenantId, ItemId = id, Type = ItemEventType.CustodyChanged, FromPartyId = prevParty, ToPartyId = item.CustodianPartyId, UserId = _ctx.UserId, Data = new() { ["manual"] = true } });
        if (prevState != item.State) _db.ItemEvents.Add(new ItemEvent { TenantId = _ctx.TenantId, ItemId = id, Type = ItemEventType.StateChanged, FromState = prevState, ToState = item.State, UserId = _ctx.UserId, Data = new() { ["manual"] = true } });
        await _db.SaveChangesAsync();
        return Ok(Dto.Item(item));
    }

    [HttpPost("{id:guid}/tags"), Authorize(Policy = "Operator")]
    public async Task<IActionResult> BindTag(Guid id, [FromBody] TagBind body)
    {
        var item = await _db.Items.FindAsync(id);
        if (item == null) return NotFound();
        var epc = TagResolver.Normalize(body.Epc);
        var tag = await _db.Tags.FirstOrDefaultAsync(t => t.Epc == epc);
        if (tag?.ItemId != null && tag.ItemId != id) return Conflict(new { error = "EPC bound to another item" });
        tag ??= new Tag { TenantId = _ctx.TenantId, Epc = epc, Technology = body.Technology ?? TagTechnology.UhfGen2 };
        tag.ItemId = id; tag.Tid = body.Tid ?? tag.Tid; tag.Status = TagStatus.Active; tag.EncodedAt = DateTime.UtcNow;
        if (_db.Entry(tag).State == EntityState.Detached) _db.Tags.Add(tag);
        _db.ItemEvents.Add(new ItemEvent { TenantId = _ctx.TenantId, ItemId = id, Type = ItemEventType.Commissioned, UserId = _ctx.UserId, Data = new() { ["epc"] = epc } });
        await _db.SaveChangesAsync();
        return Ok(new { tag.Id, tag.Epc, tag.Status });
    }
    public record TagBind(string Epc, string? Tid, TagTechnology? Technology);

    [HttpDelete("{id:guid}/tags/{tagId:guid}"), Authorize(Policy = "Operator")]
    public async Task<IActionResult> UnbindTag(Guid id, Guid tagId)
    {
        var tag = await _db.Tags.FirstOrDefaultAsync(t => t.Id == tagId && t.ItemId == id);
        if (tag == null) return NotFound();
        tag.ItemId = null; tag.Status = TagStatus.Retired;
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
