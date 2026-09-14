using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Rfid.Application.Contracts;
using Rfid.Domain;
using Rfid.Domain.Entities;

namespace Rfid.Application.Services;

/// <summary>One asset/stock record from an external master (ERP, EAM, CMDB). Unknown columns become attributes.</summary>
public class ImportRow
{
    public string Identifier { get; set; } = "";
    public string? Name { get; set; }
    public string? Type { get; set; }
    public string? Location { get; set; }
    public string? Custodian { get; set; }
    public string? State { get; set; }
    public decimal? Quantity { get; set; }
    public string? Lot { get; set; }
    public DateOnly? Expiry { get; set; }
    public decimal? Cost { get; set; }
    public DateOnly? PurchasedAt { get; set; }
    public string? Epc { get; set; }
    public Dictionary<string, object?> Attributes { get; set; } = new();
}

public class ImportOptions
{
    public bool DryRun { get; set; }
    public bool CreateMissingLocations { get; set; } = true;
    public bool CreateMissingParties { get; set; } = true;
    public bool UpdateExisting { get; set; } = true;
    /// <summary>Fallback item type code when a row has none.</summary>
    public string? DefaultType { get; set; }
    public string Source { get; set; } = "import";
}

public class ImportRowResult { public string Identifier { get; set; } = ""; public string Action { get; set; } = ""; public List<string> Changes { get; set; } = new(); public string? Error { get; set; } }
public class ImportResult { public int Created { get; set; } public int Updated { get; set; } public int Unchanged { get; set; } public int Errors { get; set; } public bool DryRun { get; set; } public List<ImportRowResult> Rows { get; set; } = new(); }

public class ReconcileResult
{
    public int Matched { get; set; } public List<string> MissingInPlatform { get; set; } = new(); public List<string> MissingInErp { get; set; } = new();
    public List<(string identifier, string field, string? erp, string? platform)> Differences { get; set; } = new();
}

/// <summary>Inbound ERP/EAM sync: upsert items (and tags, locations, custodians) from CSV/JSON master data, or reconcile without writing.</summary>
public class ImportService
{
    private readonly IAppDb _db;
    private readonly ICurrentContext _ctx;
    public ImportService(IAppDb db, ICurrentContext ctx) { _db = db; _ctx = ctx; }

    private static readonly HashSet<string> Known = new(StringComparer.OrdinalIgnoreCase) { "identifier", "assetnumber", "asset", "serial", "serialnumber", "name", "description", "type", "itemtype", "assetclass", "location", "room", "custodian", "owner", "assignedto", "state", "status", "quantity", "qty", "lot", "batch", "expiry", "expirydate", "cost", "value", "acquisitionvalue", "purchasedat", "purchasedate", "capitalizationdate", "epc", "rfid", "tag" };

    public static List<ImportRow> ParseCsv(string csv)
    {
        var lines = csv.Replace("\r\n", "\n").Split('\n').Where(l => l.Trim().Length > 0).ToList();
        if (lines.Count == 0) return new();
        var header = SplitCsv(lines[0]).Select(h => h.Trim()).ToList();
        return lines.Skip(1).Select(l => { var cells = SplitCsv(l); var d = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase); for (var i = 0; i < header.Count; i++) d[header[i]] = i < cells.Count ? cells[i].Trim() : null; return FromDictionary(d); }).ToList();
    }

    public static List<ImportRow> ParseJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("rows", out var r) ? r : doc.RootElement;
        return root.EnumerateArray().Select(o => FromDictionary(o.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.ValueKind == JsonValueKind.Null ? null : p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.ToString(), StringComparer.OrdinalIgnoreCase))).ToList();
    }

    public static ImportRow FromDictionary(IDictionary<string, string?> d)
    {
        string? G(params string[] keys) { foreach (var k in keys) if (d.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v)) return v; return null; }
        decimal? Dec(string? s) => decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : null;
        DateOnly? Day(string? s) => DateOnly.TryParse(s, CultureInfo.InvariantCulture, out var v) ? v : DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt) ? DateOnly.FromDateTime(dt) : null;
        var row = new ImportRow
        {
            Identifier = G("identifier", "assetnumber", "asset", "serialnumber", "serial") ?? "", Name = G("name", "description"), Type = G("type", "itemtype", "assetclass"), Location = G("location", "room"),
            Custodian = G("custodian", "owner", "assignedto"), State = G("state"), Quantity = Dec(G("quantity", "qty")), Lot = G("lot", "batch"), Expiry = Day(G("expiry", "expirydate")),
            Cost = Dec(G("cost", "value", "acquisitionvalue")), PurchasedAt = Day(G("purchasedat", "purchasedate", "capitalizationdate")), Epc = G("epc", "rfid", "tag"),
        };
        foreach (var kv in d.Where(kv => !Known.Contains(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value)))
            row.Attributes[kv.Key.StartsWith("attr.", StringComparison.OrdinalIgnoreCase) ? kv.Key[5..] : kv.Key] = kv.Value;
        return row;
    }

    private static List<string> SplitCsv(string line)
    {
        var cells = new List<string>(); var cur = new System.Text.StringBuilder(); var q = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (q) { if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; } else if (c == '"') q = false; else cur.Append(c); }
            else if (c == '"') q = true; else if (c == ',' || c == ';' || c == '\t') { cells.Add(cur.ToString()); cur.Clear(); } else cur.Append(c);
        }
        cells.Add(cur.ToString());
        return cells;
    }

    public async Task<ImportResult> ImportAsync(List<ImportRow> rows, ImportOptions o, CancellationToken ct = default)
    {
        var result = new ImportResult { DryRun = o.DryRun };
        var types = await _db.ItemTypes.ToListAsync(ct);
        var locs = await _db.Locations.ToListAsync(ct);
        var parties = await _db.Parties.ToListAsync(ct);
        var ids = rows.Select(r => r.Identifier).Where(i => i.Length > 0).ToList();
        var existing = await _db.Items.Include(i => i.ItemType).Include(i => i.Tags).Where(i => ids.Contains(i.Identifier)).ToDictionaryAsync(i => i.Identifier, StringComparer.OrdinalIgnoreCase, ct);
        var epcs = rows.Where(r => r.Epc != null).Select(r => TagResolver.Normalize(r.Epc!)).ToList();
        var tags = await _db.Tags.Where(t => epcs.Contains(t.Epc)).ToDictionaryAsync(t => t.Epc, ct);
        var now = DateTime.UtcNow;

        foreach (var r in rows)
        {
            var rr = new ImportRowResult { Identifier = r.Identifier };
            result.Rows.Add(rr);
            try
            {
                if (string.IsNullOrWhiteSpace(r.Identifier)) throw new DomainException("identifier is required");
                var typeCode = r.Type ?? o.DefaultType;
                var type = types.FirstOrDefault(t => t.Code.Equals(typeCode, StringComparison.OrdinalIgnoreCase) || t.Name.Equals(typeCode, StringComparison.OrdinalIgnoreCase));
                Location? loc = null;
                if (r.Location != null)
                {
                    loc = locs.FirstOrDefault(l => (l.Code != null && l.Code.Equals(r.Location, StringComparison.OrdinalIgnoreCase)) || l.Name.Equals(r.Location, StringComparison.OrdinalIgnoreCase));
                    if (loc == null && o.CreateMissingLocations) { loc = new Location { TenantId = _ctx.TenantId, Kind = LocationKind.Area, Name = r.Location, Code = r.Location, Attributes = new() { ["source"] = o.Source } }; loc.Path = "/" + loc.Id.ToString("N") + "/"; locs.Add(loc); if (!o.DryRun) _db.Locations.Add(loc); rr.Changes.Add($"location '{r.Location}' created"); }
                }
                Party? party = null;
                if (r.Custodian != null)
                {
                    party = parties.FirstOrDefault(p => (p.Code != null && p.Code.Equals(r.Custodian, StringComparison.OrdinalIgnoreCase)) || p.Name.Equals(r.Custodian, StringComparison.OrdinalIgnoreCase));
                    if (party == null && o.CreateMissingParties) { party = new Party { TenantId = _ctx.TenantId, Kind = PartyKind.Person, Name = r.Custodian, Code = r.Custodian }; parties.Add(party); if (!o.DryRun) _db.Parties.Add(party); rr.Changes.Add($"party '{r.Custodian}' created"); }
                }

                if (!existing.TryGetValue(r.Identifier, out var item))
                {
                    if (type == null) throw new DomainException($"item type '{typeCode}' not found (set a default type)");
                    item = new Item
                    {
                        TenantId = _ctx.TenantId, ItemTypeId = type.Id, ItemType = type, Identifier = r.Identifier, Name = r.Name ?? r.Identifier, State = r.State ?? type.Lifecycle?.Initial, CurrentLocationId = loc?.Id, CustodianPartyId = party?.Id,
                        Quantity = r.Quantity ?? 1, Unit = type.Unit, LotNumber = r.Lot, ExpiryDate = r.Expiry, Cost = r.Cost, PurchasedAt = r.PurchasedAt, Attributes = new(r.Attributes),
                    };
                    rr.Action = "Created"; result.Created++;
                    if (!o.DryRun) { _db.Items.Add(item); _db.ItemEvents.Add(new ItemEvent { TenantId = _ctx.TenantId, ItemId = item.Id, Type = ItemEventType.Imported, ToLocationId = loc?.Id, ToPartyId = party?.Id, ToState = item.State, OccurredAt = now, UserId = _ctx.UserId, Data = new() { ["source"] = o.Source, ["action"] = "created" } }); }
                    existing[r.Identifier] = item;
                }
                else
                {
                    if (!o.UpdateExisting) { rr.Action = "Skipped"; result.Unchanged++; continue; }
                    void Set<T>(string field, T? current, T? incoming, Action apply) where T : notnull { if (incoming != null && !Equals(current, incoming)) { rr.Changes.Add($"{field}: {current} → {incoming}"); if (!o.DryRun) apply(); } }
                    Set("name", item.Name, r.Name, () => item.Name = r.Name!);
                    if (loc != null && item.CurrentLocationId != loc.Id) { rr.Changes.Add($"location: {locs.FirstOrDefault(l => l.Id == item.CurrentLocationId)?.Name ?? "—"} → {loc.Name}"); if (!o.DryRun) item.CurrentLocationId = loc.Id; }
                    if (party != null && item.CustodianPartyId != party.Id) { rr.Changes.Add($"custodian: {parties.FirstOrDefault(x => x.Id == item.CustodianPartyId)?.Name ?? "—"} → {party.Name}"); if (!o.DryRun) item.CustodianPartyId = party.Id; }
                    Set("state", item.State, r.State, () => item.State = r.State);
                    Set("quantity", item.Quantity, r.Quantity, () => item.Quantity = r.Quantity!.Value);
                    Set("lot", item.LotNumber, r.Lot, () => item.LotNumber = r.Lot);
                    Set("expiry", item.ExpiryDate, r.Expiry, () => item.ExpiryDate = r.Expiry);
                    Set("cost", item.Cost, r.Cost, () => item.Cost = r.Cost);
                    Set("purchasedAt", item.PurchasedAt, r.PurchasedAt, () => item.PurchasedAt = r.PurchasedAt);
                    foreach (var kv in r.Attributes) { var cur = item.Attributes.GetValueOrDefault(kv.Key)?.ToString(); if (cur != kv.Value?.ToString()) { rr.Changes.Add($"attr {kv.Key}: {cur} → {kv.Value}"); if (!o.DryRun) item.Attributes[kv.Key] = kv.Value; } }
                    if (rr.Changes.Count == 0) { rr.Action = "Unchanged"; result.Unchanged++; }
                    else { rr.Action = "Updated"; result.Updated++; if (!o.DryRun) _db.ItemEvents.Add(new ItemEvent { TenantId = _ctx.TenantId, ItemId = item.Id, Type = ItemEventType.Imported, ToLocationId = item.CurrentLocationId, ToPartyId = item.CustodianPartyId, ToState = item.State, OccurredAt = now, UserId = _ctx.UserId, Data = new() { ["source"] = o.Source, ["changes"] = string.Join("; ", rr.Changes) } }); }
                }

                if (r.Epc != null)
                {
                    var epc = TagResolver.Normalize(r.Epc);
                    if (tags.TryGetValue(epc, out var tag)) { if (tag.ItemId != null && tag.ItemId != item.Id) throw new DomainException($"EPC {epc} is bound to another item"); if (tag.ItemId == null) { rr.Changes.Add($"tag {epc} bound"); if (!o.DryRun) { tag.ItemId = item.Id; tag.Status = TagStatus.Active; } } }
                    else if (!item.Tags.Any(t => t.Epc == epc)) { rr.Changes.Add($"tag {epc} created"); var t = new Tag { TenantId = _ctx.TenantId, Epc = epc, ItemId = item.Id, Status = TagStatus.Active, EncodedAt = now }; tags[epc] = t; if (!o.DryRun) _db.Tags.Add(t); if (rr.Action == "Unchanged") { rr.Action = "Updated"; result.Unchanged--; result.Updated++; } }
                }
            }
            catch (DomainException ex) { rr.Action = "Error"; rr.Error = ex.Message; result.Errors++; }
        }
        if (!o.DryRun) await _db.SaveChangesAsync(ct);
        return result;
    }

    /// <summary>Compares an external asset list with the platform without writing anything.</summary>
    public async Task<ReconcileResult> ReconcileAsync(List<ImportRow> rows, CancellationToken ct = default)
    {
        var res = new ReconcileResult();
        var byId = rows.Where(r => r.Identifier.Length > 0).GroupBy(r => r.Identifier, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var typeCodes = rows.Select(r => r.Type).Where(t => t != null).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var items = await _db.Items.Include(i => i.ItemType).Include(i => i.CurrentLocation).Include(i => i.CustodianParty).Where(i => i.Status != ItemStatus.Disposed).ToListAsync(ct);
        var scope = typeCodes.Count > 0 ? items.Where(i => typeCodes.Contains(i.ItemType!.Code, StringComparer.OrdinalIgnoreCase)).ToList() : items;
        var platformById = items.ToDictionary(i => i.Identifier, StringComparer.OrdinalIgnoreCase);
        foreach (var (id, r) in byId)
        {
            if (!platformById.TryGetValue(id, out var item)) { res.MissingInPlatform.Add(id); continue; }
            res.Matched++;
            void Cmp(string field, string? erp, string? platform) { if (erp != null && !string.Equals(erp, platform, StringComparison.OrdinalIgnoreCase)) res.Differences.Add((id, field, erp, platform)); }
            Cmp("name", r.Name, item.Name);
            Cmp("location", r.Location, item.CurrentLocation?.Code ?? item.CurrentLocation?.Name);
            if (r.Location != null && item.CurrentLocation != null && (r.Location.Equals(item.CurrentLocation.Name, StringComparison.OrdinalIgnoreCase) || r.Location.Equals(item.CurrentLocation.Code, StringComparison.OrdinalIgnoreCase))) res.Differences.RemoveAll(d => d.identifier == id && d.field == "location");
            Cmp("custodian", r.Custodian, item.CustodianParty?.Code ?? item.CustodianParty?.Name);
            if (r.Custodian != null && item.CustodianParty != null && (r.Custodian.Equals(item.CustodianParty.Name, StringComparison.OrdinalIgnoreCase) || r.Custodian.Equals(item.CustodianParty.Code, StringComparison.OrdinalIgnoreCase))) res.Differences.RemoveAll(d => d.identifier == id && d.field == "custodian");
            Cmp("state", r.State, item.State);
            if (r.Cost.HasValue && r.Cost != item.Cost) res.Differences.Add((id, "cost", r.Cost.Value.ToString("0.##", CultureInfo.InvariantCulture), item.Cost?.ToString("0.##", CultureInfo.InvariantCulture)));
            if (r.Quantity.HasValue && r.Quantity != item.Quantity) res.Differences.Add((id, "quantity", r.Quantity.Value.ToString("0.##", CultureInfo.InvariantCulture), item.Quantity.ToString("0.##", CultureInfo.InvariantCulture)));
        }
        res.MissingInErp = scope.Where(i => !byId.ContainsKey(i.Identifier)).Select(i => i.Identifier).OrderBy(x => x).ToList();
        return res;
    }
}
