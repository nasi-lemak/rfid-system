using Microsoft.EntityFrameworkCore;
using Rfid.Application.Contracts;
using Rfid.Application.Services;
using Rfid.Application.Templates;
using Rfid.Domain;
using Rfid.Domain.Entities;
using Rfid.Domain.Epc;

namespace Rfid.Infrastructure.Persistence.Demo;

public class SeedResult
{
    public string Scenario { get; set; } = "";
    public bool Skipped { get; set; }
    public int Locations { get; set; }
    public int Parties { get; set; }
    public int Items { get; set; }
    public int Devices { get; set; }
    public int Operations { get; set; }
    public int RejectedLines { get; set; }
    public int Reads { get; set; }
    public int Alerts { get; set; }
    public List<string> Warnings { get; set; } = new();
}

/// <summary>
/// Materialises a <see cref="Scenario"/> into the current tenant. Master data is inserted directly;
/// history and reader traffic are replayed through the real OperationProcessor / ReadIngestionService so
/// events, lifecycle transitions, rules and alerts are produced exactly as in production.
/// </summary>
public class DemoSeeder
{
    private readonly AppDbContext _db;
    private readonly ICurrentContext _ctx;
    private readonly TemplateProvisioner _templates;
    private readonly OperationProcessor _ops;
    private readonly ReadIngestionService _ingest;
    private readonly RuleEngine _rules;

    public DemoSeeder(AppDbContext db, ICurrentContext ctx, TemplateProvisioner templates, OperationProcessor ops, ReadIngestionService ingest, RuleEngine rules)
    {
        _db = db; _ctx = ctx; _templates = templates; _ops = ops; _ingest = ingest; _rules = rules;
    }

    public static Scenario? Find(string templateCode) => DemoScenarios.All.FirstOrDefault(s => s.Template == templateCode);

    public async Task<SeedResult> SeedAsync(Scenario sc, CancellationToken ct = default)
    {
        var result = new SeedResult { Scenario = sc.Template };
        if (await _db.Locations.AnyAsync(l => l.Kind == LocationKind.Site && l.Code == sc.SiteCode, ct)) { result.Skipped = true; return result; }

        foreach (var tpl in new[] { sc.Template }.Concat(sc.AlsoRequires)) await _templates.ApplyAsync(tpl, ct);
        _rules.InvalidateCache();
        var types = await _db.ItemTypes.ToDictionaryAsync(t => t.Code, StringComparer.OrdinalIgnoreCase, ct);
        var now = DateTime.UtcNow;
        var tenant = _ctx.TenantId;

        // Locations (site first, then in declaration order so parents resolve).
        var site = new Location { TenantId = tenant, Kind = LocationKind.Site, Name = sc.Site, Code = sc.SiteCode };
        site.Path = "/" + site.Id.ToString("N") + "/";
        _db.Locations.Add(site);
        var locs = new Dictionary<string, Location>(StringComparer.OrdinalIgnoreCase) { [sc.Site] = site };
        foreach (var d in sc.Locations)
        {
            Location? parent = d.Parent != null ? locs.GetValueOrDefault(d.Parent) ?? throw new DomainException($"{sc.Template}: unknown parent location '{d.Parent}'")
                : d.Kind is LocationKind.Customer or LocationKind.External ? null : site;
            var l = new Location { TenantId = tenant, Kind = d.Kind, Name = d.Name, Code = d.Code ?? $"{sc.SiteCode}-{Slug(d.Name)}", ParentId = parent?.Id, IsMobile = d.Mobile, Attributes = d.Attrs ?? new() };
            l.Path = (parent?.Path ?? "/") + l.Id.ToString("N") + "/";
            _db.Locations.Add(l); locs[d.Name] = l; result.Locations++;
        }

        var parties = new Dictionary<string, Party>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in sc.Parties) { var e = new Party { TenantId = tenant, Kind = p.Kind, Name = p.Name, Code = p.Code }; _db.Parties.Add(e); parties[p.Name] = e; result.Parties++; }

        // Items + tags. EPCs are real SGTIN-96 values: one item reference per item type, serial per item.
        var items = new Dictionary<string, Item>(StringComparer.OrdinalIgnoreCase);
        var itemRefs = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var serial = 1000UL;
        foreach (var d in sc.Items)
        {
            var type = types.GetValueOrDefault(d.Type) ?? throw new DomainException($"{sc.Template}: item type '{d.Type}' is not provided by the template");
            var loc = d.Location != null ? locs.GetValueOrDefault(d.Location) ?? throw new DomainException($"{sc.Template}: unknown location '{d.Location}' for {d.Identifier}") : null;
            var state = d.State ?? type.Lifecycle?.Initial;
            if (type.Lifecycle != null && !type.Lifecycle.IsValidState(state)) throw new DomainException($"{sc.Template}: state '{state}' invalid for {d.Identifier}");
            var item = new Item
            {
                TenantId = tenant, ItemTypeId = type.Id, ItemType = type, Identifier = d.Identifier, Name = d.Name, State = state,
                CurrentLocationId = loc?.Id, CustodianPartyId = d.Custodian != null ? (parties.GetValueOrDefault(d.Custodian) ?? throw new DomainException($"{sc.Template}: unknown party '{d.Custodian}'")).Id : null,
                Quantity = d.Qty, Unit = type.Unit, LotNumber = d.Lot, ExpiryDate = d.ExpiryDays is int ed ? DateOnly.FromDateTime(now.AddDays(ed)) : null, CycleCount = d.Cycles,
                Attributes = d.Attrs ?? new(), Cost = d.Cost, DueBackAt = d.DueBackInDays is int db ? now.AddDays(db) : null,
                LastSeenAt = now.AddHours(-d.LastSeenHoursAgo), LastSeenLocationId = loc?.Id, CreatedAt = now.AddDays(-90),
            };
            if (type.RequiresInspection || d.LastInspectedDaysAgo.HasValue)
            {
                var interval = type.InspectionIntervalDays ?? 365;
                item.LastInspectedAt = d.LastInspectedDaysAgo is int li ? now.AddDays(-li) : null;
                item.NextInspectionDue = (item.LastInspectedAt ?? now).AddDays(interval);
            }
            if (!itemRefs.TryGetValue(type.Code, out var itemRef)) { itemRef = 100000 + itemRefs.Count + 1; itemRefs[type.Code] = itemRef; }
            var epc = Sgtin96.Encode(sc.CompanyPrefix, itemRef.ToString("D6"), serial++);
            _db.Items.Add(item);
            _db.Tags.Add(new Tag { TenantId = tenant, Epc = epc, Technology = TechnologyFor(type.Code), ItemId = item.Id, Status = TagStatus.Active, EncodedAt = now.AddDays(-90) });
            _db.ItemEvents.Add(new ItemEvent { TenantId = tenant, ItemId = item.Id, Type = ItemEventType.Created, ToLocationId = loc?.Id, ToState = state, OccurredAt = now.AddDays(-90), UserId = _ctx.UserId });
            items[d.Identifier] = item; result.Items++;
        }
        // Container membership after all items exist.
        foreach (var d in sc.Items.Where(x => x.Parent != null))
            items[d.Identifier].ParentItemId = (items.GetValueOrDefault(d.Parent!) ?? throw new DomainException($"{sc.Template}: unknown container '{d.Parent}'")).Id;

        var devices = new Dictionary<string, Device>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in sc.Devices)
        {
            var dev = new Device { TenantId = tenant, Name = d.Name, Kind = d.Kind, Model = d.Model, SerialNumber = $"{sc.SiteCode}-{Slug(d.Name)}", SiteLocationId = site.Id, TokenHash = d.Token != null ? PasswordHasher.HashToken(d.Token) : null, LastSeenAt = now.AddMinutes(-Random(sc, d.Name) % 180) };
            foreach (var a in d.Antennas) dev.Antennas.Add(new Antenna { TenantId = tenant, Port = a.Port, LocationId = (locs.GetValueOrDefault(a.Location) ?? throw new DomainException($"{sc.Template}: unknown antenna location '{a.Location}'")).Id, Direction = a.Direction, PowerDbm = 27 });
            _db.Devices.Add(dev); devices[d.Name] = dev; result.Devices++;
        }
        await _db.SaveChangesAsync(ct);

        // History: replay oldest first through the operation processor.
        foreach (var step in sc.History.OrderByDescending(s => s.DaysAgo))
        {
            var req = new OperationRequest
            {
                Type = step.Type, OccurredAt = now.AddDays(-step.DaysAgo), Reference = step.Reference, TargetState = step.State,
                ToLocationId = step.To != null ? (locs.GetValueOrDefault(step.To) ?? throw new DomainException($"{sc.Template}: unknown location '{step.To}'")).Id : null,
                PartyId = step.Party != null ? (parties.GetValueOrDefault(step.Party) ?? throw new DomainException($"{sc.Template}: unknown party '{step.Party}'")).Id : null,
                ContainerItemId = step.Container != null ? (items.GetValueOrDefault(step.Container) ?? throw new DomainException($"{sc.Template}: unknown container '{step.Container}'")).Id : null,
                DueBackAt = step.DueBackDays is int dd ? now.AddDays(-step.DaysAgo + dd) : null,
                DeviceId = devices.Values.FirstOrDefault(d => d.Kind == DeviceKind.Handheld)?.Id,
                Lines = step.Items.Select(id => new OperationLineRequest { ItemId = (items.GetValueOrDefault(id) ?? throw new DomainException($"{sc.Template}: unknown item '{id}'")).Id, Quantity = step.Qty }).ToList(),
            };
            try
            {
                var r = await _ops.ProcessAsync(req, ct);
                result.Operations++;
                result.RejectedLines += r.Rejected + r.Unknown;
                foreach (var l in r.Lines.Where(l => l.Result != LineResult.Ok)) result.Warnings.Add($"{step.Type} {l.ItemName ?? l.ItemId?.ToString()}: {l.Message}");
            }
            catch (DomainException ex) { result.Warnings.Add($"{step.Type}: {ex.Message}"); }
        }

        // Fixed-reader traffic, oldest first (chronology matters for zone in/out).
        var epcByItem = await _db.Tags.Where(t => t.ItemId != null).ToDictionaryAsync(t => t.ItemId!.Value, t => t.Epc, ct);
        foreach (var rd in sc.Reads.OrderByDescending(r => r.HoursAgo))
        {
            var dev = devices.GetValueOrDefault(rd.Device) ?? throw new DomainException($"{sc.Template}: unknown device '{rd.Device}'");
            var at = now.AddHours(-rd.HoursAgo);
            var batch = new ReadBatchRequest { DeviceId = dev.Id, SessionId = $"seed-{sc.SiteCode}", Reads = rd.Items.Select((id, i) => new ReadRequest { Epc = epcByItem[(items.GetValueOrDefault(id) ?? throw new DomainException($"{sc.Template}: unknown item '{id}' in reads")).Id], AntennaPort = rd.Port, Rssi = -48 - (Random(sc, id) % 20), ReadAt = at.AddSeconds(i) }).ToList() };
            var r = await _ingest.IngestAsync(batch, ReadSource.Fixed, ct);
            result.Reads += r.Received; result.Alerts += r.Alerts;
        }

        // Background read traffic over the last 14 days so dashboards have shape (no events, raw sightings only).
        // Background noise must not touch timing mats / muster points – those reads carry meaning.
        var noiseKinds = new[] { LocationKind.Checkpoint, LocationKind.MusterPoint };
        var fixedDevices = devices.Values.Where(d => d.Antennas.Any(a => a.LocationId.HasValue && !noiseKinds.Contains(locs.Values.First(l => l.Id == a.LocationId).Kind))).ToList();
        if (fixedDevices.Count > 0)
        {
            var rnd = new System.Random(sc.SiteCode.GetHashCode(StringComparison.Ordinal));
            var pool = items.Values.Where(i => i.CurrentLocationId != null).ToList();
            for (var day = 14; day >= 1; day--)
            {
                var n = 3 + rnd.Next(6);
                for (var k = 0; k < n && pool.Count > 0; k++)
                {
                    var it = pool[rnd.Next(pool.Count)]; var dev = fixedDevices[rnd.Next(fixedDevices.Count)];
                    var okAnts = dev.Antennas.Where(a => a.LocationId.HasValue && !noiseKinds.Contains(locs.Values.First(l => l.Id == a.LocationId).Kind)).ToList(); var ant = okAnts[rnd.Next(okAnts.Count)];
                    _db.TagReads.Add(new TagRead { TenantId = tenant, Epc = epcByItem[it.Id], ItemId = it.Id, DeviceId = dev.Id, AntennaPort = ant.Port, Rssi = -45 - rnd.Next(30), ReadAt = now.AddDays(-day).AddMinutes(rnd.Next(600)), LocationId = ant.LocationId, Source = ReadSource.Fixed, SessionId = "seed-background" });
                    result.Reads++;
                }
            }
            // A few uncommissioned tags seen by readers – populates the "unknown tags" commissioning list.
            for (var k = 0; k < 2; k++)
            {
                var dev = fixedDevices[k % fixedDevices.Count];
                _db.TagReads.Add(new TagRead { TenantId = tenant, Epc = $"E280689400004{sc.CompanyPrefix[^4..]}{k:D3}", DeviceId = dev.Id, AntennaPort = dev.Antennas[0].Port, Rssi = -61, ReadAt = now.AddMinutes(-15 - k * 40), LocationId = dev.Antennas[0].LocationId, Source = ReadSource.Fixed });
            }
            await _db.SaveChangesAsync(ct);
        }
        return result;
    }

    private static TagTechnology TechnologyFor(string typeCode) => typeCode switch
    {
        "ANIMAL" => TagTechnology.Lf, "BADGE" or "VISITOR" => TagTechnology.Active, "BIB" => TagTechnology.UhfGen2, "BOOK" => TagTechnology.HfNfc, _ => TagTechnology.UhfGen2,
    };
    private static int Random(Scenario sc, string key) => Math.Abs((sc.SiteCode + key).GetHashCode(StringComparison.Ordinal));
    private static string Slug(string s) => new string(s.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
}

/// <summary>Fixed tenant/user used when seeding at startup (outside an HTTP request).</summary>
public class SeedContext : ICurrentContext
{
    public SeedContext(Guid tenantId, Guid? userId) { TenantId = tenantId; UserId = userId; }
    public Guid TenantId { get; }
    public Guid? UserId { get; }
    public Guid? DeviceId => null;
}
