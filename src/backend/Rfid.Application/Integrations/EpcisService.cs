using Rfid.Protocols;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Rfid.Application.Contracts;
using Rfid.Domain;
using Rfid.Domain.Entities;
using Rfid.Domain.Epc;

namespace Rfid.Application.Services;

/// <summary>
/// EPCIS 2.0 (JSON/JSON-LD) view of the event log: ItemEvents become ObjectEvents / AggregationEvents with CBV business
/// steps and dispositions; inbound EPCISDocuments are captured into reads (OBSERVE) or transfers (ADD/DELETE with a bizLocation).
/// </summary>
public class EpcisService
{
    private readonly IAppDb _db; private readonly ICurrentContext _ctx; private readonly ReadIngestionService? _ingest; private readonly OperationProcessor? _ops;
    public string BaseUrl { get; set; } = "https://rfid-platform.local";
    private readonly Operations.OperationDefinitions? _definitions;
    public EpcisService(IAppDb db, ICurrentContext ctx, ReadIngestionService? ingest = null, OperationProcessor? ops = null, Operations.OperationDefinitions? definitions = null) { _db = db; _ctx = ctx; _ingest = ingest; _ops = ops; _definitions = definitions; }

    /// <summary>Inbound bizStep → operation: the tenant's or built-in definition whose EventData.bizStep matches, else Transfer.</summary>
    public async Task<string> OperationForBizStepAsync(string bizStep, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(bizStep)) return OperationType.Transfer.ToString();
        var defs = _definitions != null ? await _definitions.ListAsync(ct) : Operations.OperationCatalog.BuiltIn.ToList();
        var match = defs.Where(d => d.Enabled && d.BaseType != OperationType.Commission && d.EventData.TryGetValue("bizStep", out var v) && string.Equals(v?.ToString(), bizStep, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(d => d.IsBuiltIn ? 1 : 0).FirstOrDefault();   // tenant/template definitions win over built-ins
        return match?.Code ?? OperationType.Transfer.ToString();
    }

    public const string Context = "https://ref.gs1.org/standards/epcis/2.0.0/epcis-context.jsonld";

    // ── URNs ──
    public static string EpcUrn(string epc)
    {
        if (Sgtin96.Decode(epc) is { } s) return $"urn:epc:id:sgtin:{s.companyPrefix}.{s.itemRef}.{s.serial}";
        if (Gs1.DecodeGrai96(epc) is { } g) return $"urn:epc:id:grai:{g.companyPrefix}.{g.assetType}.{g.serial}";
        if (Gs1.DecodeGiai96(epc) is { } a) return $"urn:epc:id:giai:{a.companyPrefix}.{a.assetReference}";
        if (Sscc96.Decode(epc) is { } c) return $"urn:epc:id:sscc:{c.companyPrefix}.{c.extensionDigit}{c.serial}";
        return TagResolver.IsHexEpc(epc) ? $"urn:epc:raw:{epc.Length * 4}.x{epc}" : $"urn:epc:id:code:{Uri.EscapeDataString(epc)}";
    }
    public string LocationUrn(Location? l) => l == null ? "" : l.Attributes.TryGetValue("sgln", out var sg) && sg != null ? $"urn:epc:id:sgln:{sg}" : $"{BaseUrl.TrimEnd('/')}/locations/{l.Id}";
    /// <summary>
    /// CBV vocabulary for an event. The operation definition that produced the event may carry <c>bizStep</c> /
    /// <c>disposition</c> in its EventData (merged into the event's Data), which wins; otherwise the event type's default.
    /// </summary>
    public static (string bizStep, string disposition, string action) Cbv(ItemEvent e)
    {
        var (bizStep, disposition, action) = DefaultCbv(e);
        if (e.Data.TryGetValue("bizStep", out var bs) && bs != null && !string.IsNullOrWhiteSpace(bs.ToString())) bizStep = Strip(bs.ToString()!, "BizStep");
        if (e.Data.TryGetValue("disposition", out var ds) && ds != null && !string.IsNullOrWhiteSpace(ds.ToString())) disposition = Strip(ds.ToString()!, "Disp");
        return (bizStep, disposition, action);
    }

    public static (string bizStep, string disposition, string action) DefaultCbv(ItemEvent e) => e.Type switch
    {
        ItemEventType.Created or ItemEventType.Commissioned => ("commissioning", "active", "ADD"),
        ItemEventType.Seen => ("inspecting", "in_progress", "OBSERVE"),
        ItemEventType.Moved when e.Data.ContainsKey("dispatch") => ("shipping", "in_transit", "OBSERVE"),
        ItemEventType.Moved => ("storing", "in_progress", "OBSERVE"),
        ItemEventType.CustodyChanged when e.ToPartyId != null => ("shipping", "in_transit", "OBSERVE"),
        ItemEventType.CustodyChanged => ("receiving", "in_progress", "OBSERVE"),
        ItemEventType.Counted => ("cycle_counting", "in_progress", "OBSERVE"),
        ItemEventType.Inspected => ("inspecting", e.Data.TryGetValue("result", out var r) && r?.ToString()?.Contains("fail", StringComparison.OrdinalIgnoreCase) == true ? "non_conformant" : "conformant", "OBSERVE"),
        ItemEventType.Maintained => ("repairing", "active", "OBSERVE"),
        ItemEventType.Disposed => ("destroying", "destroyed", "DELETE"),
        ItemEventType.Packed => ("packing", "in_progress", "ADD"),
        ItemEventType.Unpacked => ("unpacking", "in_progress", "DELETE"),
        ItemEventType.StateChanged => ("transforming", "active", "OBSERVE"),
        ItemEventType.QuantityChanged => ("stock_taking", "in_progress", "OBSERVE"),
        ItemEventType.GeofenceEntered => ("arriving", "in_progress", "OBSERVE"),
        ItemEventType.GeofenceExited => ("departing", "in_transit", "OBSERVE"),
        _ => ("other", "active", "OBSERVE"),
    };

    public record Query(string? BizStep = null, string? Disposition = null, string? Action = null, string? Epc = null, Guid? ItemId = null, DateTime? From = null, DateTime? To = null, string? EventType = null, Guid? ReadPointId = null, int PerPage = 100, int Page = 1);

    public async Task<(JsonObject document, int total)> QueryAsync(Query q, CancellationToken ct = default)
    {
        var events = _db.ItemEvents.AsQueryable();
        if (q.From.HasValue) events = events.Where(e => e.OccurredAt >= q.From); if (q.To.HasValue) events = events.Where(e => e.OccurredAt < q.To);
        if (q.ItemId.HasValue) events = events.Where(e => e.ItemId == q.ItemId);
        if (q.ReadPointId.HasValue) events = events.Where(e => e.ToLocationId == q.ReadPointId);
        if (!string.IsNullOrEmpty(q.Epc))
        {
            var epc = TagResolver.Normalize(q.Epc.StartsWith("urn:epc:raw:") ? q.Epc[(q.Epc.IndexOf(".x", StringComparison.Ordinal) + 2)..] : q.Epc);
            var ids = await _db.Tags.Where(t => t.Epc == epc && t.ItemId != null).Select(t => t.ItemId!.Value).ToListAsync(ct);
            events = events.Where(e => ids.Contains(e.ItemId));
        }
        if (!string.IsNullOrEmpty(q.EventType)) events = q.EventType == "AggregationEvent" ? events.Where(e => e.Type == ItemEventType.Packed || e.Type == ItemEventType.Unpacked) : events.Where(e => e.Type != ItemEventType.Packed && e.Type != ItemEventType.Unpacked);
        var all = await events.OrderByDescending(e => e.OccurredAt).ToListAsync(ct);
        // CBV filters need the mapping, so they apply in memory.
        var mapped = all.Select(e => (e, cbv: Cbv(e))).Where(x => (q.BizStep == null || x.cbv.bizStep == Strip(q.BizStep, "BizStep")) && (q.Disposition == null || x.cbv.disposition == Strip(q.Disposition, "Disp")) && (q.Action == null || x.cbv.action == q.Action.ToUpperInvariant())).ToList();
        var page = mapped.Skip((Math.Max(1, q.Page) - 1) * q.PerPage).Take(q.PerPage).Select(x => x.e).ToList();
        var doc = await DocumentAsync(page, ct);
        return (doc, mapped.Count);
    }
    private static string Strip(string v, string kind) => v.Replace($"urn:epcglobal:cbv:{kind.ToLower()}:", "").Replace($"cbv:{kind}-", "").Replace("https://ref.gs1.org/cbv/", "").Replace($"{kind}-", "");

    public async Task<JsonObject> DocumentAsync(List<ItemEvent> page, CancellationToken ct = default)
    {
        var itemIds = page.Select(e => e.ItemId).Distinct().ToList();
        var tags = await _db.Tags.Where(t => t.ItemId != null && itemIds.Contains(t.ItemId.Value)).ToListAsync(ct);
        var items = await _db.Items.Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        var locIds = page.SelectMany(e => new[] { e.FromLocationId, e.ToLocationId }).Where(x => x != null).Select(x => x!.Value).Distinct().ToList();
        var locs = await _db.Locations.Where(l => locIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, ct);
        var opIds = page.Where(e => e.OperationId != null).Select(e => e.OperationId!.Value).Distinct().ToList();
        var ops = await _db.Operations.Where(o => opIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, ct);
        var containerIds = page.Where(e => e.Type is ItemEventType.Packed or ItemEventType.Unpacked).Select(e => e.Data.TryGetValue("container", out var c) ? c?.ToString() : null).Where(c => c != null).Distinct().ToList();
        var containers = containerIds.Count == 0 ? new() : await _db.Items.Include(i => i.Tags).Where(i => containerIds.Contains(i.Identifier)).ToDictionaryAsync(i => i.Identifier, ct);
        var list = new JsonArray();
        foreach (var e in page) list.Add(ToEpcis(e, tags.Where(t => t.ItemId == e.ItemId).Select(t => t.Epc).ToList(), items.GetValueOrDefault(e.ItemId), locs, ops, containers));
        return new JsonObject
        {
            ["@context"] = new JsonArray(Context), ["type"] = "EPCISDocument", ["schemaVersion"] = "2.0", ["creationDate"] = DateTime.UtcNow.ToString("O"),
            ["epcisBody"] = new JsonObject { ["eventList"] = list },
        };
    }

    public JsonObject ToEpcis(ItemEvent e, List<string> epcs, Item? item, Dictionary<Guid, Location> locs, Dictionary<Guid, Operation> ops, Dictionary<string, Item> containers)
    {
        var (bizStep, disp, action) = Cbv(e);
        var epcList = new JsonArray(); foreach (var epc in epcs.DefaultIfEmpty(item != null ? "ID:" + item.Identifier : "ID:" + e.ItemId)) epcList.Add(epc.StartsWith("ID:") ? $"{BaseUrl.TrimEnd('/')}/items/{e.ItemId}" : EpcUrn(epc));
        var isAgg = e.Type is ItemEventType.Packed or ItemEventType.Unpacked;
        var o = new JsonObject
        {
            ["type"] = isAgg ? "AggregationEvent" : "ObjectEvent", ["eventID"] = $"urn:uuid:{e.Id}", ["eventTime"] = e.OccurredAt.ToString("O"), ["eventTimeZoneOffset"] = "+00:00", ["recordTime"] = e.CreatedAt.ToString("O"),
            ["action"] = action, ["bizStep"] = bizStep, ["disposition"] = disp,
        };
        if (isAgg)
        {
            var parent = e.Data.TryGetValue("container", out var c) && c != null && containers.TryGetValue(c.ToString()!, out var ci) ? ci.Tags.FirstOrDefault()?.Epc : null;
            o["parentID"] = parent != null ? EpcUrn(parent) : $"{BaseUrl.TrimEnd('/')}/items/{c}"; o["childEPCs"] = epcList;
        }
        else o["epcList"] = epcList;
        var readPoint = e.ToLocationId.HasValue ? locs.GetValueOrDefault(e.ToLocationId.Value) : null;
        if (readPoint != null) { o["readPoint"] = new JsonObject { ["id"] = LocationUrn(readPoint) }; o["bizLocation"] = new JsonObject { ["id"] = LocationUrn(readPoint) }; }
        if (e.OperationId.HasValue && ops.TryGetValue(e.OperationId.Value, out var op)) o["bizTransactionList"] = new JsonArray(new JsonObject { ["type"] = op.Type switch { OperationType.Receive => "po", OperationType.Dispatch or OperationType.Issue => "desadv", OperationType.Return => "rma", _ => "prodorder" }, ["bizTransaction"] = op.Reference ?? $"urn:epcglobal:cbv:bt:{_ctx.TenantId:N}:{op.Id}" });
        if (e.ToPartyId.HasValue || e.FromPartyId.HasValue) o["sourceList"] = new JsonArray(new JsonObject { ["type"] = "owning_party", ["source"] = $"{BaseUrl.TrimEnd('/')}/parties/{e.FromPartyId ?? e.ToPartyId}" });
        if (e.ToPartyId.HasValue) o["destinationList"] = new JsonArray(new JsonObject { ["type"] = "owning_party", ["destination"] = $"{BaseUrl.TrimEnd('/')}/parties/{e.ToPartyId}" });
        var ext = new JsonObject { ["rfid:eventType"] = e.Type.ToString(), ["rfid:itemId"] = e.ItemId.ToString() };
        if (e.FromState != null || e.ToState != null) { ext["rfid:fromState"] = e.FromState; ext["rfid:toState"] = e.ToState; }
        if (e.DeviceId.HasValue) ext["rfid:deviceId"] = e.DeviceId.ToString();
        foreach (var kv in e.Data) ext["rfid:" + kv.Key] = kv.Value == null ? null : JsonValue.Create(kv.Value.ToString());
        o["rfid:extension"] = ext;
        return o;
    }

    public record CaptureResult(int Events, int Applied, int Rejected, List<string> Errors);

    /// <summary>Captures an EPCISDocument: OBSERVE events become reads at the read point; ADD/DELETE with a bizLocation become transfers.</summary>
    public async Task<CaptureResult> CaptureAsync(JsonNode document, CancellationToken ct = default)
    {
        var events = document["epcisBody"]?["eventList"]?.AsArray() ?? document["eventList"]?.AsArray() ?? new JsonArray();
        var errors = new List<string>(); int applied = 0;
        foreach (var ev in events)
        {
            try
            {
                if (ev == null) continue;
                var type = ev["type"]?.ToString() ?? "ObjectEvent";
                var epcs = (ev["epcList"]?.AsArray() ?? ev["childEPCs"]?.AsArray() ?? new JsonArray()).Select(x => UrnToEpc(x?.ToString() ?? "")).Where(x => x != null).Select(x => x!).ToList();
                if (epcs.Count == 0) { errors.Add($"{ev["eventID"]}: no EPCs"); continue; }
                var at = DateTime.TryParse(ev["eventTime"]?.ToString(), null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var t) ? t : DateTime.UtcNow;
                var loc = await ResolveLocationAsync(ev["bizLocation"]?["id"]?.ToString() ?? ev["readPoint"]?["id"]?.ToString(), ct);
                var action = ev["action"]?.ToString()?.ToUpperInvariant() ?? "OBSERVE";
                var bizStep = Strip(ev["bizStep"]?.ToString() ?? "", "BizStep");
                if (_ops != null && loc != null && (action != "OBSERVE" || bizStep is "receiving" or "shipping" or "storing"))
                {
                    var opCode = await OperationForBizStepAsync(bizStep, ct);
                    var r = await _ops.ProcessAsync(new OperationRequest { Operation = opCode, ToLocationId = loc.Id, Reference = ev["bizTransactionList"]?.AsArray().FirstOrDefault()?["bizTransaction"]?.ToString() ?? $"EPCIS {ev["eventID"]}", Lines = epcs.Select(e => new OperationLineRequest { Epc = e }).ToList() }, ct);
                    applied += r.Ok; if (r.Unknown > 0) errors.Add($"{ev["eventID"]}: {r.Unknown} unknown EPC(s)");
                }
                else if (_ingest != null)
                {
                    var r = await _ingest.IngestAsync(new ReadBatchRequest { SessionId = "epcis", Reads = epcs.Select(e => new ReadRequest { Epc = e, ReadAt = at, LocationId = loc?.Id }).ToList() }, ReadSource.Fixed, ct);
                    applied += r.Resolved; if (r.Unknown > 0) errors.Add($"{ev["eventID"]}: {r.Unknown} unknown EPC(s)");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { errors.Add($"{ev?["eventID"]}: {ex.Message}"); }
        }
        var capture = new EpcisCapture { TenantId = _ctx.TenantId, Events = events.Count, Applied = applied, Rejected = errors.Count, Status = errors.Count == 0 ? "Completed" : applied > 0 ? "Partial" : "Failed", Errors = errors.Count == 0 ? null : string.Join("\n", errors.Take(50)), UserId = _ctx.UserId, DeviceId = _ctx.DeviceId };
        _db.EpcisCaptures.Add(capture); await _db.SaveChangesAsync(ct);
        return new CaptureResult(events.Count, applied, errors.Count, errors);
    }

    private async Task<Location?> ResolveLocationAsync(string? id, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(id)) return null;
        var tail = id.Split('/').Last();
        if (Guid.TryParse(tail, out var g)) return await _db.Locations.FirstOrDefaultAsync(l => l.Id == g, ct);
        if (id.StartsWith("urn:epc:id:sgln:")) { var sgln = id["urn:epc:id:sgln:".Length..]; var all = await _db.Locations.ToListAsync(ct); return all.FirstOrDefault(l => l.Attributes.TryGetValue("sgln", out var v) && v?.ToString() == sgln); }
        return await _db.Locations.FirstOrDefaultAsync(l => l.Code == tail || l.Name == tail, ct);
    }

    /// <summary>urn:epc:id:sgtin/grai/giai/sscc → 96-bit hex; urn:epc:raw → hex; other strings pass through as codes.</summary>
    public static string? UrnToEpc(string urn)
    {
        if (string.IsNullOrWhiteSpace(urn)) return null;
        try
        {
            if (urn.StartsWith("urn:epc:raw:")) return urn[(urn.IndexOf(".x", StringComparison.Ordinal) + 2)..].ToUpperInvariant();
            if (urn.StartsWith("urn:epc:id:sgtin:")) { var p = urn[17..].Split('.'); return Sgtin96.Encode(p[0], p[1], ulong.Parse(p[2]), 1); }
            if (urn.StartsWith("urn:epc:id:grai:")) { var p = urn[16..].Split('.'); return Gs1.EncodeGrai96(p[0], p[1], ulong.Parse(p[2]), 0); }
            if (urn.StartsWith("urn:epc:id:giai:")) { var p = urn[16..].Split('.'); return Gs1.EncodeGiai96(p[0], ulong.Parse(p[1]), 0); }
            if (urn.StartsWith("urn:epc:id:sscc:")) { var p = urn[16..].Split('.'); return Sscc96.Encode(p[0], p[1][0] - '0', ulong.Parse(p[1][1..]), 0); }
            if (urn.StartsWith("urn:epc:id:code:")) return Uri.UnescapeDataString(urn[16..]);
        }
        catch { return null; }
        return urn.Contains("/items/") ? null : urn;
    }
}
