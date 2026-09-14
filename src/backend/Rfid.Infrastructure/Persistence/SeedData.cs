using Microsoft.EntityFrameworkCore;
using Rfid.Application.Services;
using Rfid.Application.Templates;
using Rfid.Domain;
using Rfid.Domain.Entities;

namespace Rfid.Infrastructure.Persistence;

/// <summary>Creates the demo tenant, admin user, template catalog and a small demo dataset on first run.</summary>
public static class SeedData
{
    public static readonly Guid DemoTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static async Task EnsureSeededAsync(AppDbContext db, string adminEmail, string adminPassword, CancellationToken ct = default)
    {
        // Templates are global; refresh them from the catalog so code changes flow through.
        var existing = await db.SolutionTemplates.IgnoreQueryFilters().ToDictionaryAsync(t => t.Code, ct);
        foreach (var tpl in SolutionTemplateCatalog.All)
        {
            if (existing.TryGetValue(tpl.Code, out var e)) { e.Name = tpl.Name; e.Vertical = tpl.Vertical; e.Description = tpl.Description; e.Definition = tpl.Definition; }
            else db.SolutionTemplates.Add(new SolutionTemplate { Code = tpl.Code, Name = tpl.Name, Vertical = tpl.Vertical, Description = tpl.Description, Definition = tpl.Definition });
        }
        await db.SaveChangesAsync(ct);

        if (await db.Tenants.AnyAsync(t => t.Id == DemoTenantId, ct)) return;
        var t = DemoTenantId;
        db.Tenants.Add(new Tenant { Id = t, Name = "Demo Tenant", Code = "demo" });
        db.Users.Add(new User { TenantId = t, Email = adminEmail, PasswordHash = PasswordHasher.Hash(adminPassword), DisplayName = "Administrator", Role = UserRole.Admin });
        db.Users.Add(new User { TenantId = t, Email = "operator@demo.local", PasswordHash = PasswordHasher.Hash("operator123"), DisplayName = "Warehouse Operator", Role = UserRole.Operator });

        // Locations
        var site = Loc(t, null, LocationKind.Site, "Main Site", "SITE1");
        var wh = Loc(t, site, LocationKind.Building, "Warehouse", "WH");
        var recv = Loc(t, wh, LocationKind.Zone, "Receiving", "RECV");
        var rackA = Loc(t, wh, LocationKind.Rack, "Rack A", "RACK-A");
        var dock1 = Loc(t, wh, LocationKind.Dock, "Dock Door 1", "DOCK1");
        var office = Loc(t, site, LocationKind.Building, "Office", "OFF");
        var itStore = Loc(t, office, LocationKind.Room, "IT Store", "IT-STORE");
        var crib = Loc(t, site, LocationKind.Room, "Tool Crib", "CRIB");
        var exit = Loc(t, site, LocationKind.Gate, "Main Exit Gate", "EXIT");
        var customer = Loc(t, null, LocationKind.Customer, "Customer: ACME Ltd", "CUST-ACME");
        db.Locations.AddRange(site, wh, recv, rackA, dock1, office, itStore, crib, exit, customer);

        // Parties
        var jane = new Party { TenantId = t, Kind = PartyKind.Employee, Name = "Jane Lee", Code = "EMP001", Email = "jane@demo.local" };
        var sam = new Party { TenantId = t, Kind = PartyKind.Employee, Name = "Sam Patel", Code = "EMP002" };
        var acme = new Party { TenantId = t, Kind = PartyKind.Customer, Name = "ACME Ltd", Code = "ACME" };
        db.Parties.AddRange(jane, sam, acme);

        // Item types from templates
        var asset = SolutionTemplateCatalog.All.First(x => x.Code == "asset-management").Definition.ItemTypes;
        var tools = SolutionTemplateCatalog.All.First(x => x.Code == "tool-tracking").Definition.ItemTypes;
        var wms = SolutionTemplateCatalog.All.First(x => x.Code == "warehouse").Definition.ItemTypes;
        var inv = SolutionTemplateCatalog.All.First(x => x.Code == "inventory").Definition.ItemTypes;
        ItemType Clone(ItemType s) => new()
        {
            TenantId = t, Name = s.Name, Code = s.Code, Category = s.Category, IsContainer = s.IsContainer, TracksExpiry = s.TracksExpiry, TracksCycles = s.TracksCycles,
            MaxCycles = s.MaxCycles, RequiresInspection = s.RequiresInspection, InspectionIntervalDays = s.InspectionIntervalDays, ReorderPoint = s.ReorderPoint, Unit = s.Unit,
            AttributeSchema = s.AttributeSchema, Lifecycle = s.Lifecycle, Vertical = "Demo",
        };
        var itAsset = Clone(asset.First(x => x.Code == "IT-ASSET"));
        var tool = Clone(tools.First(x => x.Code == "TOOL"));
        var pallet = Clone(wms.First(x => x.Code == "PALLET"));
        var carton = Clone(wms.First(x => x.Code == "CARTON"));
        var stock = Clone(inv.First(x => x.Code == "STOCK-LOT"));
        db.ItemTypes.AddRange(itAsset, tool, pallet, carton, stock);

        // Rules from those templates
        foreach (var tplCode in new[] { "asset-management", "tool-tracking", "warehouse", "inventory" })
            foreach (var r in SolutionTemplateCatalog.All.First(x => x.Code == tplCode).Definition.Rules)
                db.Rules.Add(new Rule { TenantId = t, Name = r.Name, Trigger = r.Trigger, Conditions = r.Conditions, Action = r.Action, Params = r.Params, Severity = r.Severity, Enabled = r.Enabled, Vertical = "Demo" });

        // Items + tags
        var n = 0;
        Item Mk(ItemType type, string ident, string name, Location loc, string? state, Party? custodian = null, Dictionary<string, object?>? attrs = null, decimal qty = 1)
        {
            var it = new Item { TenantId = t, ItemTypeId = type.Id, Identifier = ident, Name = name, CurrentLocationId = loc.Id, State = state ?? type.Lifecycle?.Initial, CustodianPartyId = custodian?.Id, Attributes = attrs ?? new(), Quantity = qty, Unit = type.Unit, LastSeenAt = DateTime.UtcNow.AddHours(-n), LastSeenLocationId = loc.Id };
            db.Items.Add(it);
            db.Tags.Add(new Tag { TenantId = t, Epc = $"3034F8B2{(++n):X4}000000{n:X6}".ToUpperInvariant(), ItemId = it.Id, Status = TagStatus.Active, EncodedAt = DateTime.UtcNow });
            db.ItemEvents.Add(new ItemEvent { TenantId = t, ItemId = it.Id, Type = ItemEventType.Created, ToLocationId = loc.Id, ToState = it.State, OccurredAt = DateTime.UtcNow.AddDays(-7) });
            return it;
        }
        Mk(itAsset, "LT-0001", "Dell Latitude 5540", itStore, "Stock", attrs: new() { ["hostname"] = "LT-0001", ["os"] = "Windows 11" });
        Mk(itAsset, "LT-0002", "MacBook Pro 14", itStore, "Assigned", jane, new() { ["hostname"] = "MBP-JLEE", ["os"] = "macOS" });
        Mk(itAsset, "MON-0001", "Dell U2723QE Monitor", itStore, "Stock");
        Mk(tool, "TL-0001", "Makita Drill 18V", crib, "InCrib");
        Mk(tool, "TL-0002", "Torque Wrench 3/8", crib, "CheckedOut", sam);
        Mk(tool, "TL-0003", "Fluke 87V Multimeter", crib, "InCrib");
        var p1 = Mk(pallet, "PLT-0001", "Pallet 0001", recv, "Received");
        var c1 = Mk(carton, "CTN-0001", "Carton 0001", recv, null); c1.ParentItemId = p1.Id;
        var c2 = Mk(carton, "CTN-0002", "Carton 0002", recv, null); c2.ParentItemId = p1.Id;
        Mk(pallet, "PLT-0002", "Pallet 0002", rackA, "PutAway");
        Mk(stock, "LOT-2409-A", "Nitrile gloves M (lot 2409-A)", rackA, null, attrs: new() { ["sku"] = "GLV-M" }, qty: 240);
        var expiring = Mk(stock, "LOT-2401-B", "Saline 500ml (lot 2401-B)", rackA, null, attrs: new() { ["sku"] = "SAL-500" }, qty: 8);
        expiring.ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(12));

        // Devices
        var handheld = new Device { TenantId = t, Name = "Handheld RFD40 #1", Kind = DeviceKind.Handheld, Model = "Zebra RFD40", SerialNumber = "RFD40-0001", SiteLocationId = site.Id, TokenHash = PasswordHasher.HashToken("demo-handheld-token") };
        var portal = new Device { TenantId = t, Name = "Dock Door 1 Portal", Kind = DeviceKind.Portal, Model = "Zebra FX9600", SerialNumber = "FX9600-0001", SiteLocationId = site.Id, TokenHash = PasswordHasher.HashToken("demo-portal-token") };
        portal.Antennas.Add(new Antenna { TenantId = t, Port = 1, LocationId = dock1.Id, Direction = AntennaDirection.In });
        portal.Antennas.Add(new Antenna { TenantId = t, Port = 2, LocationId = dock1.Id, Direction = AntennaDirection.Out });
        var gate = new Device { TenantId = t, Name = "Exit Gate Reader", Kind = DeviceKind.Gate, Model = "Impinj R700", SerialNumber = "R700-0001", SiteLocationId = site.Id, TokenHash = PasswordHasher.HashToken("demo-gate-token") };
        gate.Antennas.Add(new Antenna { TenantId = t, Port = 1, LocationId = exit.Id, Direction = AntennaDirection.Out });
        db.Devices.AddRange(handheld, portal, gate);

        await db.SaveChangesAsync(ct);
    }

    private static Location Loc(Guid t, Location? parent, LocationKind kind, string name, string code)
    {
        var l = new Location { TenantId = t, ParentId = parent?.Id, Kind = kind, Name = name, Code = code };
        l.Path = (parent?.Path ?? "/") + l.Id.ToString("N") + "/";
        return l;
    }
}
