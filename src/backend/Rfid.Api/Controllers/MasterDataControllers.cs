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

[ApiController, Route("api/item-types"), Authorize]
public class ItemTypesController : ControllerBase
{
    private readonly AppDbContext _db; private readonly ICurrentContext _ctx;
    public ItemTypesController(AppDbContext db, ICurrentContext ctx) { _db = db; _ctx = ctx; }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var types = await _db.ItemTypes.OrderBy(t => t.Name).ToListAsync();
        var counts = await _db.Items.GroupBy(i => i.ItemTypeId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count);
        return Ok(types.Select(t => new { t.Id, t.Name, t.Code, t.Category, t.IsContainer, t.TracksExpiry, t.TracksCycles, t.MaxCycles, t.RequiresInspection, t.InspectionIntervalDays, t.ReorderPoint, t.Unit, t.AttributeSchema, t.Lifecycle, t.Vertical, t.UsefulLifeMonths, HasLabelDesign = t.LabelDesign != null, ItemCount = counts.GetValueOrDefault(t.Id) }));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id) => await _db.ItemTypes.FindAsync(id) is { } t ? Ok(t) : NotFound();

    [HttpPost, Authorize(Policy = "Admin")]
    public async Task<IActionResult> Create(ItemType t)
    {
        if (await _db.ItemTypes.AnyAsync(x => x.Code == t.Code)) return Conflict(new { error = "Code already exists" });
        t.Id = Guid.NewGuid(); t.TenantId = _ctx.TenantId; t.CreatedAt = DateTime.UtcNow;
        _db.ItemTypes.Add(t); await _db.SaveChangesAsync();
        return Ok(t);
    }

    [HttpPut("{id:guid}"), Authorize(Policy = "Admin")]
    public async Task<IActionResult> Update(Guid id, ItemType t)
    {
        var e = await _db.ItemTypes.FindAsync(id); if (e == null) return NotFound();
        e.Name = t.Name; e.Code = t.Code; e.Category = t.Category; e.IsContainer = t.IsContainer; e.TracksExpiry = t.TracksExpiry; e.TracksCycles = t.TracksCycles; e.MaxCycles = t.MaxCycles;
        e.RequiresInspection = t.RequiresInspection; e.InspectionIntervalDays = t.InspectionIntervalDays; e.ReorderPoint = t.ReorderPoint; e.Unit = t.Unit; e.AttributeSchema = t.AttributeSchema; e.Lifecycle = t.Lifecycle; e.ImageUrl = t.ImageUrl; e.UsefulLifeMonths = t.UsefulLifeMonths; if (t.LabelTemplate != null) e.LabelTemplate = t.LabelTemplate;
        await _db.SaveChangesAsync(); return Ok(e);
    }

    [HttpDelete("{id:guid}"), Authorize(Policy = "Admin")]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (await _db.Items.AnyAsync(i => i.ItemTypeId == id)) return Conflict(new { error = "Item type has items" });
        var e = await _db.ItemTypes.FindAsync(id); if (e == null) return NotFound();
        _db.ItemTypes.Remove(e); await _db.SaveChangesAsync(); return NoContent();
    }
}

[ApiController, Route("api/locations"), Authorize]
public class LocationsController : ControllerBase
{
    private readonly AppDbContext _db; private readonly ICurrentContext _ctx; private readonly ISiteAccess _sites;
    public LocationsController(AppDbContext db, ICurrentContext ctx, ISiteAccess sites) { _db = db; _ctx = ctx; _sites = sites; }

    [HttpGet]
    public async Task<IActionResult> List(LocationKind? kind = null)
    {
        var q = SiteAccess.Filter(_db.Locations.AsQueryable(), await _sites.AllowedPathsAsync());
        if (kind.HasValue) q = q.Where(l => l.Kind == kind);
        var locs = await q.OrderBy(l => l.Path).ToListAsync();
        var counts = await _db.Items.Where(i => i.CurrentLocationId != null && i.Status != ItemStatus.Disposed).GroupBy(i => i.CurrentLocationId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key!.Value, x => x.Count);
        return Ok(locs.Select(l => new { l.Id, l.ParentId, l.Kind, l.Name, l.Code, l.Path, l.Latitude, l.Longitude, l.IsMobile, l.Attributes, ItemCount = counts.GetValueOrDefault(l.Id) }));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var l = await _db.Locations.FindAsync(id); if (l == null) return NotFound();
        var subtreeCount = await _db.Items.Include(i => i.CurrentLocation).CountAsync(i => i.CurrentLocation != null && i.CurrentLocation.Path.StartsWith(l.Path) && i.Status != ItemStatus.Disposed);
        return Ok(new { Location = Dto.Location(l), SubtreeItemCount = subtreeCount });
    }

    public record LocationWrite(Guid? ParentId, LocationKind Kind, string Name, string? Code, double? Latitude, double? Longitude, bool IsMobile, Dictionary<string, object?>? Attributes);

    [HttpPost, Authorize(Policy = "Admin")]
    public async Task<IActionResult> Create(LocationWrite w)
    {
        var parent = w.ParentId.HasValue ? await _db.Locations.FindAsync(w.ParentId.Value) : null;
        if (w.ParentId.HasValue && parent == null) return BadRequest(new { error = "Parent not found" });
        var l = new Location { TenantId = _ctx.TenantId, ParentId = parent?.Id, Kind = w.Kind, Name = w.Name, Code = w.Code, Latitude = w.Latitude, Longitude = w.Longitude, IsMobile = w.IsMobile, Attributes = w.Attributes ?? new() };
        l.Path = (parent?.Path ?? "/") + l.Id.ToString("N") + "/";
        _db.Locations.Add(l); await _db.SaveChangesAsync();
        return Ok(Dto.Location(l));
    }

    [HttpPut("{id:guid}"), Authorize(Policy = "Admin")]
    public async Task<IActionResult> Update(Guid id, LocationWrite w)
    {
        var l = await _db.Locations.FindAsync(id); if (l == null) return NotFound();
        if (w.ParentId != l.ParentId)
        {
            var parent = w.ParentId.HasValue ? await _db.Locations.FindAsync(w.ParentId.Value) : null;
            if (parent != null && parent.Path.StartsWith(l.Path)) return BadRequest(new { error = "Cannot move a location under its own descendant" });
            var oldPath = l.Path;
            l.ParentId = parent?.Id; l.Path = (parent?.Path ?? "/") + l.Id.ToString("N") + "/";
            foreach (var d in await _db.Locations.Where(x => x.Path.StartsWith(oldPath) && x.Id != l.Id).ToListAsync())
                d.Path = l.Path + d.Path[oldPath.Length..];
        }
        l.Kind = w.Kind; l.Name = w.Name; l.Code = w.Code; l.Latitude = w.Latitude; l.Longitude = w.Longitude; l.IsMobile = w.IsMobile; if (w.Attributes != null) l.Attributes = w.Attributes;
        await _db.SaveChangesAsync(); return Ok(Dto.Location(l));
    }

    [HttpDelete("{id:guid}"), Authorize(Policy = "Admin")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var l = await _db.Locations.FindAsync(id); if (l == null) return NotFound();
        if (await _db.Locations.AnyAsync(x => x.ParentId == id)) return Conflict(new { error = "Location has children" });
        if (await _db.Items.AnyAsync(i => i.CurrentLocationId == id)) return Conflict(new { error = "Location has items" });
        _db.Locations.Remove(l); await _db.SaveChangesAsync(); return NoContent();
    }
}

[ApiController, Route("api/parties"), Authorize]
public class PartiesController : ControllerBase
{
    private readonly AppDbContext _db; private readonly ICurrentContext _ctx;
    public PartiesController(AppDbContext db, ICurrentContext ctx) { _db = db; _ctx = ctx; }

    [HttpGet]
    public async Task<IActionResult> List(string? q, PartyKind? kind)
    {
        var query = _db.Parties.AsQueryable();
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(p => EF.Functions.ILike(p.Name, $"%{q}%") || (p.Code != null && EF.Functions.ILike(p.Code, $"%{q}%")));
        if (kind.HasValue) query = query.Where(p => p.Kind == kind);
        var parties = await query.OrderBy(p => p.Name).Take(500).ToListAsync();
        var counts = await _db.Items.Where(i => i.CustodianPartyId != null).GroupBy(i => i.CustodianPartyId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key!.Value, x => x.Count);
        return Ok(parties.Select(p => new { p.Id, p.Kind, p.Name, p.Code, p.ExternalRef, p.Email, p.Attributes, ItemsInCustody = counts.GetValueOrDefault(p.Id) }));
    }

    [HttpPost, Authorize(Policy = "Operator")]
    public async Task<IActionResult> Create(Party p) { p.Id = Guid.NewGuid(); p.TenantId = _ctx.TenantId; p.CreatedAt = DateTime.UtcNow; _db.Parties.Add(p); await _db.SaveChangesAsync(); return Ok(p); }

    [HttpPut("{id:guid}"), Authorize(Policy = "Operator")]
    public async Task<IActionResult> Update(Guid id, Party p)
    {
        var e = await _db.Parties.FindAsync(id); if (e == null) return NotFound();
        e.Kind = p.Kind; e.Name = p.Name; e.Code = p.Code; e.ExternalRef = p.ExternalRef; e.Email = p.Email; e.Attributes = p.Attributes;
        await _db.SaveChangesAsync(); return Ok(e);
    }

    [HttpDelete("{id:guid}"), Authorize(Policy = "Admin")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var e = await _db.Parties.FindAsync(id); if (e == null) return NotFound();
        _db.Parties.Remove(e); await _db.SaveChangesAsync(); return NoContent();
    }
}

[ApiController, Route("api/tags"), Authorize]
public class TagsController : ControllerBase
{
    private readonly AppDbContext _db; private readonly ICurrentContext _ctx;
    public TagsController(AppDbContext db, ICurrentContext ctx) { _db = db; _ctx = ctx; }

    [HttpGet]
    public async Task<Paged<object>> List(string? q, bool? unassigned, int? page, int? pageSize)
    {
        var (p, s) = Query.Page(page, pageSize);
        var query = _db.Tags.Include(t => t.Item).AsQueryable();
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(t => EF.Functions.ILike(t.Epc, $"%{q}%"));
        if (unassigned == true) query = query.Where(t => t.ItemId == null);
        var total = await query.CountAsync();
        var tags = await query.OrderByDescending(t => t.CreatedAt).Skip((p - 1) * s).Take(s).ToListAsync();
        return new Paged<object>(tags.Select(t => (object)new { t.Id, t.Epc, t.Tid, t.Technology, t.Status, t.ItemId, ItemName = t.Item?.Name, t.EncodedAt }).ToList(), total, p, s);
    }

    /// <summary>Recent unknown (uncommissioned) EPCs seen by readers – useful for commissioning.</summary>
    [HttpGet("unknown-reads")]
    public async Task<IActionResult> UnknownReads(int take = 100)
    {
        var reads = await _db.TagReads.Where(r => r.ItemId == null).OrderByDescending(r => r.ReadAt).Take(take * 5).ToListAsync();
        return Ok(reads.GroupBy(r => r.Epc).Select(g => new { Epc = g.Key, LastSeen = g.Max(r => r.ReadAt), Count = g.Count(), DeviceId = g.First().DeviceId }).OrderByDescending(x => x.LastSeen).Take(take));
    }

    [HttpPost("encode/sgtin96"), Authorize(Policy = "Operator")]
    public IActionResult EncodeSgtin(string companyPrefix, string itemRef, ulong serial, int filter = 1)
        => Ok(new { epc = Rfid.Domain.Epc.Sgtin96.Encode(companyPrefix, itemRef, serial, filter) });

    [HttpGet("decode/{epc}")]
    public IActionResult Decode(string epc)
    {
        var d = Rfid.Domain.Epc.Sgtin96.Decode(TagResolver.Normalize(epc));
        return d == null ? Ok(new { scheme = "unknown" }) : Ok(new { scheme = "SGTIN-96", d.Value.companyPrefix, d.Value.itemRef, d.Value.serial });
    }
}

[ApiController, Route("api/devices"), Authorize]
public class DevicesController : ControllerBase
{
    private readonly AppDbContext _db; private readonly ICurrentContext _ctx; private readonly ISiteAccess _sites;
    public DevicesController(AppDbContext db, ICurrentContext ctx, ISiteAccess sites) { _db = db; _ctx = ctx; _sites = sites; }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var q = _db.Devices.Include(d => d.Antennas).ThenInclude(a => a.Location).AsQueryable();
        if (await _sites.AllowedPathsAsync() is { } paths)
        {
            var siteIds = await SiteAccess.Filter(_db.Locations, paths).Select(l => l.Id).ToListAsync();
            q = q.Where(d => (d.SiteLocationId != null && siteIds.Contains(d.SiteLocationId.Value)) || d.Antennas.Any(a => a.LocationId != null && siteIds.Contains(a.LocationId.Value)));
        }
        var devices = await q.OrderBy(d => d.Name).ToListAsync();
        return Ok(devices.Select(Map));
    }

    private static object Map(Device d) => new
    {
        d.Id, d.Name, d.Kind, d.SerialNumber, d.Model, d.SiteLocationId, d.LastSeenAt, d.Config, HasToken = d.TokenHash != null, d.Health, d.HealthChangedAt, d.LastHeartbeatAt, d.FirmwareVersion, d.HeartbeatSlaMinutes, d.TrackedItemId,
        Antennas = d.Antennas.OrderBy(a => a.Port).Select(a => new { a.Id, a.Port, a.LocationId, LocationName = a.Location?.Name, a.Direction, a.PowerDbm, a.X, a.Y, a.RssiAt1m, a.PathLossExponent }),
        Llrp = Rfid.Api.Background.LlrpReaderService.Endpoint(d) is { } ep ? new { ep.host, ep.port } : null,
    };

    public record AntennaWrite(int Port, Guid? LocationId, AntennaDirection Direction, double? PowerDbm, double? X = null, double? Y = null, double? RssiAt1m = null, double? PathLossExponent = null);
    public record DeviceWrite(string Name, DeviceKind Kind, string? SerialNumber, string? Model, Guid? SiteLocationId, Dictionary<string, object?>? Config, List<AntennaWrite>? Antennas);

    [HttpPost, Authorize(Policy = "Admin")]
    public async Task<IActionResult> Create(DeviceWrite w)
    {
        var d = new Device { TenantId = _ctx.TenantId, Name = w.Name, Kind = w.Kind, SerialNumber = w.SerialNumber, Model = w.Model, SiteLocationId = w.SiteLocationId, Config = w.Config ?? new() };
        foreach (var a in w.Antennas ?? new()) d.Antennas.Add(new Antenna { TenantId = _ctx.TenantId, Port = a.Port, LocationId = a.LocationId, Direction = a.Direction, PowerDbm = a.PowerDbm, X = a.X, Y = a.Y, RssiAt1m = a.RssiAt1m, PathLossExponent = a.PathLossExponent });
        _db.Devices.Add(d); await _db.SaveChangesAsync();
        return Ok(Map(d));
    }

    [HttpPut("{id:guid}"), Authorize(Policy = "Admin")]
    public async Task<IActionResult> Update(Guid id, DeviceWrite w)
    {
        var d = await _db.Devices.Include(x => x.Antennas).FirstOrDefaultAsync(x => x.Id == id); if (d == null) return NotFound();
        d.Name = w.Name; d.Kind = w.Kind; d.SerialNumber = w.SerialNumber; d.Model = w.Model; d.SiteLocationId = w.SiteLocationId; if (w.Config != null) d.Config = w.Config;
        if (w.Antennas != null)
        {
            _db.Antennas.RemoveRange(d.Antennas); d.Antennas.Clear();
            foreach (var a in w.Antennas) _db.Antennas.Add(new Antenna { TenantId = _ctx.TenantId, DeviceId = d.Id, Port = a.Port, LocationId = a.LocationId, Direction = a.Direction, PowerDbm = a.PowerDbm, X = a.X, Y = a.Y, RssiAt1m = a.RssiAt1m, PathLossExponent = a.PathLossExponent });
        }
        await _db.SaveChangesAsync();
        return Ok(Map(await _db.Devices.Include(x => x.Antennas).ThenInclude(a => a.Location).FirstAsync(x => x.Id == id)));
    }

    /// <summary>Generates a new provisioning token for the device. Shown once.</summary>
    [HttpPost("{id:guid}/token"), Authorize(Policy = "Admin")]
    public async Task<IActionResult> RotateToken(Guid id)
    {
        var d = await _db.Devices.FindAsync(id); if (d == null) return NotFound();
        var token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        d.TokenHash = PasswordHasher.HashToken(token);
        await _db.SaveChangesAsync();
        return Ok(new { token });
    }

    [HttpDelete("{id:guid}"), Authorize(Policy = "Admin")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var d = await _db.Devices.FindAsync(id); if (d == null) return NotFound();
        _db.Devices.Remove(d); await _db.SaveChangesAsync(); return NoContent();
    }
}

[ApiController, Route("api/users"), Authorize(Policy = "Admin")]
public class UsersController : ControllerBase
{
    private readonly AppDbContext _db; private readonly ICurrentContext _ctx;
    public UsersController(AppDbContext db, ICurrentContext ctx) { _db = db; _ctx = ctx; }

    [HttpGet]
    public async Task<IActionResult> List() => Ok(await _db.Users.OrderBy(u => u.Email).Select(u => new { u.Id, u.Email, u.DisplayName, u.Role, u.IsActive, u.CreatedAt, u.RestrictToSites, u.ExternalIssuer, u.LastLoginAt, u.PortalPartyId, SiteCount = _db.UserSiteAccess.Count(s => s.UserId == u.Id) }).ToListAsync());

    public record UserWrite(string Email, string DisplayName, UserRole Role, string? Password, bool IsActive = true, Guid? PortalPartyId = null);

    [HttpPost]
    public async Task<IActionResult> Create(UserWrite w)
    {
        if (string.IsNullOrEmpty(w.Password)) return BadRequest(new { error = "Password required" });
        var u = new User { TenantId = _ctx.TenantId, Email = w.Email.ToLowerInvariant(), DisplayName = w.DisplayName, Role = w.PortalPartyId.HasValue ? UserRole.Viewer : w.Role, PasswordHash = PasswordHasher.Hash(w.Password), IsActive = w.IsActive, PortalPartyId = w.PortalPartyId };
        _db.Users.Add(u); await _db.SaveChangesAsync();
        return Ok(new { u.Id, u.Email, u.DisplayName, u.Role, u.IsActive });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UserWrite w)
    {
        var u = await _db.Users.FindAsync(id); if (u == null) return NotFound();
        u.Email = w.Email.ToLowerInvariant(); u.DisplayName = w.DisplayName; u.Role = w.PortalPartyId.HasValue ? UserRole.Viewer : w.Role; u.IsActive = w.IsActive; u.PortalPartyId = w.PortalPartyId;
        if (!string.IsNullOrEmpty(w.Password)) u.PasswordHash = PasswordHasher.Hash(w.Password);
        await _db.SaveChangesAsync();
        return Ok(new { u.Id, u.Email, u.DisplayName, u.Role, u.IsActive });
    }
}

[ApiController, Route("api/lookups"), Authorize]
public class LookupsController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new
    {
        operationTypes = Enum.GetNames<OperationType>(), locationKinds = Enum.GetNames<LocationKind>(), partyKinds = Enum.GetNames<PartyKind>(),
        deviceKinds = Enum.GetNames<DeviceKind>(), tagTechnologies = Enum.GetNames<TagTechnology>(), eventTypes = Enum.GetNames<ItemEventType>(),
        ruleActions = Enum.GetNames<RuleAction>(), severities = Enum.GetNames<Severity>(), itemStatuses = Enum.GetNames<ItemStatus>(),
        ruleFields = new[] { "item.state", "item.status", "item.cycleCount", "item.cyclesRemaining", "item.quantity", "item.reorderPoint", "item.daysUntilExpiry", "item.daysUntilInspection", "item.hasCustodian", "item.overdue", "item.lotNumber", "item.attributes.<name>", "itemType.code", "itemType.category", "toLocation.kind", "toLocation.code", "toLocation.name", "toLocation.<attribute>", "fromLocation.kind", "party.kind", "event.type", "data.direction", "data.result" },
        ruleOps = new[] { "eq", "ne", "gt", "gte", "lt", "lte", "in", "nin", "contains", "exists", "notexists" },
    });
}
