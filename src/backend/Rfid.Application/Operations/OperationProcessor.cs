using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Rfid.Application.Contracts;
using Rfid.Application.Operations;
using Rfid.Domain;
using Rfid.Domain.Entities;
using static Rfid.Domain.Entities.OperationEffectKinds;

namespace Rfid.Application.Services;

/// <summary>
/// Executes a business operation against the items behind the scanned codes: resolves the operation definition
/// (built-in or tenant-defined), applies the item type's lifecycle transition, runs the definition's effects in order,
/// writes the item event, evaluates rules and queues live/webhook side effects in the outbox. One unit of work per
/// operation; retries with the same ClientId replay the stored result.
/// </summary>
public class OperationProcessor
{
    private readonly IAppDb _db;
    private readonly ICurrentContext _ctx;
    private readonly TagResolver _tags;
    private readonly RuleEngine _rules;
    private readonly ILivePublisher _live;
    private readonly OperationDefinitions _definitions;
    public const int MaxContainerDepth = ContainerRules.MaxDepth;

    public OperationProcessor(IAppDb db, ICurrentContext ctx, TagResolver tags, RuleEngine rules, ILivePublisher live, OperationDefinitions? definitions = null)
    {
        _db = db; _ctx = ctx; _tags = tags; _rules = rules; _live = live; _definitions = definitions ?? new OperationDefinitions(db);
    }

    public async Task<OperationResult> ProcessAsync(OperationRequest req, CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(req.ClientId))
        {
            var legacyNote = "client:" + req.ClientId;
            var existing = await _db.Operations.Include(o => o.Lines).FirstOrDefaultAsync(o => o.ClientId == req.ClientId || (o.Notes == legacyNote && o.Reference == req.Reference), ct);
            if (existing != null) return ToResult(existing, new());
        }

        var def = await _definitions.ResolveAsync(req.Operation, req.Type, ct);
        var now = req.OccurredAt ?? DateTime.UtcNow;
        Validate(def, req);

        var from = req.FromLocationId.HasValue ? await _db.Locations.FindAsync(new object[] { req.FromLocationId.Value }, ct) : null;
        var to = req.ToLocationId.HasValue ? await _db.Locations.FindAsync(new object[] { req.ToLocationId.Value }, ct) : null;
        var party = req.PartyId.HasValue ? await _db.Parties.FindAsync(new object[] { req.PartyId.Value }, ct) : null;
        if (req.ToLocationId.HasValue && to == null) throw new NotFoundException("To location");
        if (req.FromLocationId.HasValue && from == null) throw new NotFoundException("From location");
        if (req.PartyId.HasValue && party == null) throw new NotFoundException("Party");

        Item? container = null;
        if (req.ContainerItemId.HasValue)
        {
            container = await _db.Items.Include(i => i.ItemType).FirstOrDefaultAsync(i => i.Id == req.ContainerItemId, ct) ?? throw new NotFoundException("Container item");
            if (container.ItemType?.IsContainer != true) throw new DomainException($"{container.Name} is not a container");
            if (container.Status == ItemStatus.Disposed) throw new DomainException($"{container.Name} is disposed");
        }

        var op = new Operation
        {
            TenantId = _ctx.TenantId, Type = def.BaseType, DefinitionCode = def.Code, FromLocationId = from?.Id, ToLocationId = to?.Id,
            PartyId = party?.Id, ContainerItemId = container?.Id, TargetState = req.TargetState,
            Reference = req.Reference, Notes = req.Notes, ClientId = string.IsNullOrEmpty(req.ClientId) ? null : req.ClientId,
            DeviceId = req.DeviceId ?? _ctx.DeviceId, UserId = _ctx.UserId, StartedAt = now, DueBackAt = req.DueBackAt,
        };
        _db.Operations.Add(op);

        var epcs = req.Lines.Where(l => !string.IsNullOrWhiteSpace(l.Epc)).Select(l => l.Epc!).ToList();
        var tagMap = await _tags.ResolveAsync(epcs, ct);
        var itemIds = req.Lines.Where(l => l.ItemId.HasValue).Select(l => l.ItemId!.Value).ToList();
        var identifiers = req.Lines.Where(l => !string.IsNullOrWhiteSpace(l.Identifier)).Select(l => l.Identifier!).ToList();
        var itemsById = itemIds.Count == 0 ? new() : await _db.Items.Include(i => i.ItemType).Include(i => i.CurrentLocation).Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        var itemsByIdent = identifiers.Count == 0 ? new() : await _db.Items.Include(i => i.ItemType).Include(i => i.CurrentLocation).Where(i => identifiers.Contains(i.Identifier)).ToDictionaryAsync(i => i.Identifier, ct);

        var results = new List<OperationLineResult>();
        var touched = new HashSet<Guid>();
        var ctx = new EffectContext(def, req, op, from, to, party, container, now);
        foreach (var lineReq in req.Lines)
        {
            var epc = string.IsNullOrWhiteSpace(lineReq.Epc) ? null : TagResolver.Normalize(lineReq.Epc);
            var line = new OperationLine { OperationId = op.Id, Epc = epc, Quantity = lineReq.Quantity };
            op.Lines.Add(line);
            var res = new OperationLineResult { Epc = epc };
            results.Add(res);
            try
            {
                Item? item;
                if (def.BaseType == OperationType.Commission) item = await CommissionAsync(lineReq, epc, tagMap, to, now, op, ct);
                else
                {
                    item = null;
                    if (lineReq.ItemId.HasValue) itemsById.TryGetValue(lineReq.ItemId.Value, out item);
                    if (item == null && epc != null && tagMap.TryGetValue(epc, out var tag)) item = tag.Item;
                    if (item == null && lineReq.Identifier != null) itemsByIdent.TryGetValue(lineReq.Identifier, out item);
                    if (item == null) { Reject(line, res, LineResult.Unknown, "Tag not commissioned"); continue; }
                    if (!touched.Add(item.Id)) { Reject(line, res, LineResult.Rejected, "Duplicate in operation"); res.ItemId = item.Id; continue; }
                    await ApplyAsync(ctx, item, lineReq, ct);
                }
                line.ItemId = item.Id; line.Result = LineResult.Ok;
                res.ItemId = item.Id; res.ItemName = item.Name; res.Result = LineResult.Ok; res.NewState = item.State;
            }
            catch (DomainException ex) { Reject(line, res, LineResult.Rejected, ex.Message); }
        }

        op.Status = OperationStatus.Completed;
        op.CompletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return ToResult(op, results);
    }

    private static void Reject(OperationLine line, OperationLineResult res, LineResult result, string message) { line.Result = result; line.Message = message; res.Result = result; res.Message = message; }

    private static void Validate(OperationDefinition def, OperationRequest req)
    {
        var r = def.Requires;
        if (r.ToLocation && !req.ToLocationId.HasValue) throw new DomainException($"{def.Name} requires a destination location");
        if (r.Party && !req.PartyId.HasValue) throw new DomainException($"{def.Name} requires a party");
        if (r.Container && !req.ContainerItemId.HasValue) throw new DomainException($"{def.Name} requires a container item");
        if (r.TargetState && string.IsNullOrEmpty(req.TargetState)) throw new DomainException($"{def.Name} requires a target state");
        if (r.Quantity && req.Lines.Count > 0 && req.Lines.All(l => l.Quantity == null)) throw new DomainException($"{def.Name} requires a quantity on its lines");
        if (def.BaseType == OperationType.Commission && req.Lines.Any(l => l.NewItem == null && !l.ItemId.HasValue && string.IsNullOrEmpty(l.Identifier)))
            throw new DomainException("Commission lines need a NewItem, ItemId or Identifier to bind the tag to");
    }

    private sealed record EffectContext(OperationDefinition Def, OperationRequest Req, Operation Op, Location? From, Location? To, Party? Party, Item? Container, DateTime Now);

    private async Task ApplyAsync(EffectContext c, Item item, OperationLineRequest lineReq, CancellationToken ct)
    {
        var def = c.Def;
        if (item.Status == ItemStatus.Disposed) throw new DomainException($"{item.Name} is disposed");
        if (def.ItemTypeCodes.Count > 0 && !def.ItemTypeCodes.Contains(item.ItemType?.Code ?? "", StringComparer.OrdinalIgnoreCase)) throw new DomainException($"{def.Name} does not apply to {item.ItemType?.Name}");
        if (def.Requires.FromStates.Count > 0 && !def.Requires.FromStates.Contains("*") && !def.Requires.FromStates.Contains(item.State ?? "", StringComparer.OrdinalIgnoreCase)) throw new DomainException($"{def.Name} not allowed while {item.Name} is '{item.State ?? "(none)"}'");

        // 1. Lifecycle transition keyed by the operation code (custom definitions fall back to their base type's transitions).
        var prevState = item.State; var stateChanged = false;
        var lifecycle = item.ItemType?.Lifecycle;
        var setState = def.Effects.FirstOrDefault(e => e.Kind == SetState);
        var free = setState != null && Bool(setState, "free");
        var target = setState?.Params.TryGetValue("state", out var st) == true && st != null ? st.ToString() : c.Req.TargetState;
        if (lifecycle != null)
        {
            var key = lifecycle.HasTransitionsFor(def.Code) || def.IsBuiltIn ? def.Code : def.BaseType.ToString();
            var hasAny = lifecycle.HasTransitionsFor(key);
            var tr = lifecycle.FindTransition(item.State, key, target);
            if (tr != null) { item.State = tr.To; if (tr.IncrementCycle) item.CycleCount++; stateChanged = !string.Equals(prevState, tr.To, StringComparison.OrdinalIgnoreCase); }
            else if (hasAny && !free) throw new DomainException($"{def.Name} not allowed while {item.Name} is '{item.State ?? "(none)"}'");
            else if (free && target != null)
            {
                if (!lifecycle.IsValidState(target)) throw new DomainException($"Invalid state '{target}'");
                if (hasAny) throw new DomainException($"No transition from '{item.State}' to '{target}'");
                item.State = target; stateChanged = prevState != target;
            }
        }
        else if (setState != null && target != null) { item.State = target; stateChanged = prevState != target; }

        // 2. Effects.
        var prevLoc = item.CurrentLocationId; var prevParty = item.CustodianPartyId;
        var data = new Dictionary<string, object?>(def.EventData);
        foreach (var effect in def.Effects) await RunEffectAsync(effect, c, item, lineReq, data, ct);

        item.LastSeenAt = c.Now; item.LastSeenDeviceId = c.Op.DeviceId;
        if (item.CurrentLocationId.HasValue) item.LastSeenLocationId = item.CurrentLocationId;

        var evType = def.EventType ?? OperationCatalog.DefaultEvent(def.BaseType);
        var ev = new ItemEvent
        {
            ItemId = item.Id, Type = evType, FromLocationId = prevLoc, ToLocationId = item.CurrentLocationId, FromPartyId = prevParty, ToPartyId = item.CustodianPartyId,
            FromState = prevState, ToState = item.State, OperationId = c.Op.Id, DeviceId = c.Op.DeviceId, UserId = _ctx.UserId, OccurredAt = c.Now, Data = data,
        };
        if (!def.IsBuiltIn) ev.Data["operation"] = def.Code;
        await AddEventAsync(ev, item, c.From, c.To ?? item.CurrentLocation, c.Party, ct);
        if (stateChanged && evType != ItemEventType.StateChanged)
            await AddEventAsync(new ItemEvent { ItemId = item.Id, Type = ItemEventType.StateChanged, FromState = prevState, ToState = item.State, OperationId = c.Op.Id, OccurredAt = c.Now }, item, c.From, c.To, c.Party, ct);
    }

    private static bool Bool(OperationEffect e, string key) => e.Params.TryGetValue(key, out var v) && v != null && (v is bool b ? b : v is JsonElement je ? je.ValueKind == JsonValueKind.True : string.Equals(v.ToString(), "true", StringComparison.OrdinalIgnoreCase));
    private static string? Str(OperationEffect e, string key) => e.Params.TryGetValue(key, out var v) && v != null ? (v is JsonElement je ? je.ToString() : v.ToString()) : null;

    private async Task RunEffectAsync(OperationEffect e, EffectContext c, Item item, OperationLineRequest lineReq, Dictionary<string, object?> data, CancellationToken ct)
    {
        switch (e.Kind)
        {
            case Move:
                if (c.To == null) { if (Bool(e, "optional")) return; throw new DomainException($"{c.Def.Name} requires a destination location"); }
                await MoveAsync(item, c.To, c.Now, ct, 0, c.Op); break;
            case SetCustodian:
                if (c.Party == null) { if (Bool(e, "optional")) return; throw new DomainException($"{c.Def.Name} requires a party"); }
                item.CustodianPartyId = c.Party.Id; break;
            case ClearCustodian: item.CustodianPartyId = null; break;
            case SetDueBack: if (c.Op.DueBackAt.HasValue || !Bool(e, "keep")) item.DueBackAt = c.Op.DueBackAt; break;
            case ClearDueBack: item.DueBackAt = null; break;
            case SetState: break; // applied in the lifecycle step
            case IncrementCycle: item.CycleCount++; break;
            case RecordSeen:
                item.LastSeenAt = c.Now; item.LastSeenLocationId = c.To?.Id ?? item.CurrentLocationId; item.LastSeenDeviceId = c.Op.DeviceId;
                if (item.Status == ItemStatus.Missing) item.Status = ItemStatus.Active;
                if (c.To != null && item.CurrentLocationId != c.To.Id) data["foundElsewhere"] = true;
                if (lineReq.Quantity.HasValue && item.ItemType?.Category == ItemCategory.Quantity) { data["counted"] = lineReq.Quantity; data["variance"] = lineReq.Quantity.Value - item.Quantity; }
                break;
            case RecordInspection:
                item.LastInspectedAt = c.Now;
                item.NextInspectionDue = item.ItemType?.InspectionIntervalDays is int d ? c.Now.AddDays(d) : null;
                data["result"] = c.Req.TargetState ?? Str(e, "result") ?? "Passed"; break;
            case Activate: item.Status = ItemStatus.Active; break;
            case Dispose:
                item.Status = ItemStatus.Disposed; item.CustodianPartyId = null;
                foreach (var t in await _db.Tags.Where(t => t.ItemId == item.Id).ToListAsync(ct)) t.Status = TagStatus.Retired;
                break;
            case Pack:
            {
                var container = c.Container ?? throw new DomainException($"{c.Def.Name} requires a container item");
                if (container.Id == item.Id) throw new DomainException("Cannot pack a container into itself");
                if (await ContainerRules.IsDescendantAsync(_db, container, item.Id, ct)) throw new DomainException($"Cannot pack {item.Name} into {container.Name}: it already contains {item.Name} (cycle)");
                if (item.ParentItemId.HasValue && item.ParentItemId != container.Id) data["repackedFrom"] = item.ParentItemId;
                item.ParentItemId = container.Id;
                if (container.CurrentLocationId.HasValue && item.CurrentLocationId != container.CurrentLocationId)
                    await MoveAsync(item, (await _db.Locations.FindAsync(new object[] { container.CurrentLocationId.Value }, ct))!, c.Now, ct, 0, c.Op);
                data["container"] = container.Identifier; data["containerId"] = container.Id; break;
            }
            case Unpack:
                if (item.ParentItemId.HasValue) { var parent = await _db.Items.Where(i => i.Id == item.ParentItemId).Select(i => new { i.Identifier }).FirstOrDefaultAsync(ct); data["container"] = parent?.Identifier; data["containerId"] = item.ParentItemId; }
                item.ParentItemId = null; break;
            case AdjustQuantity:
                if (lineReq.Quantity == null) throw new DomainException($"{c.Def.Name} requires a quantity delta");
                data["delta"] = lineReq.Quantity; data["before"] = item.Quantity;
                item.Quantity += lineReq.Quantity.Value;
                if (item.Quantity < 0) throw new DomainException("Quantity cannot go negative");
                break;
            case SetAttribute:
            {
                var key = Str(e, "key") ?? throw new DomainException("SetAttribute needs a key");
                var raw = e.Params.TryGetValue("value", out var rv) ? rv : null;
                // Params round-trip through JSON (JSONB / templates), so normalise JsonElement first, then apply placeholders.
                object? value = raw switch
                {
                    null => c.Req.TargetState,
                    JsonElement je when je.ValueKind == JsonValueKind.True => true,
                    JsonElement je when je.ValueKind == JsonValueKind.False => false,
                    JsonElement je when je.ValueKind == JsonValueKind.Number => je.GetDouble(),
                    JsonElement je when je.ValueKind == JsonValueKind.Null => c.Req.TargetState,
                    JsonElement je => je.ToString(),
                    _ => raw,
                };
                if (value is string placeholder) value = placeholder switch { "{now}" => c.Now.ToString("O"), "{user}" => _ctx.UserId?.ToString(), "{party}" => c.Party?.Name, "{location}" => c.To?.Name, _ => placeholder };
                item.Attributes = new Dictionary<string, object?>(item.Attributes) { [key] = value };
                data["attribute"] = key; break;
            }
            default: throw new DomainException($"Unknown effect '{e.Kind}' in operation {c.Def.Code}");
        }
    }

    private async Task<Item> CommissionAsync(OperationLineRequest lineReq, string? epc, Dictionary<string, Tag> tagMap, Location? to, DateTime now, Operation op, CancellationToken ct)
    {
        if (epc == null) throw new DomainException("Commission requires an EPC");
        Tag? tag = tagMap.GetValueOrDefault(epc);
        if (tag?.ItemId != null) throw new DomainException($"EPC {epc} already bound to {tag.Item?.Name ?? tag.ItemId.ToString()}");

        Item? item = null;
        if (lineReq.ItemId.HasValue) item = await _db.Items.Include(i => i.ItemType).FirstOrDefaultAsync(i => i.Id == lineReq.ItemId, ct);
        else if (!string.IsNullOrEmpty(lineReq.Identifier) && lineReq.NewItem == null) item = await _db.Items.Include(i => i.ItemType).FirstOrDefaultAsync(i => i.Identifier == lineReq.Identifier, ct);
        if (item == null && lineReq.NewItem == null) throw new NotFoundException("Item to bind");

        if (item == null)
        {
            var n = lineReq.NewItem!;
            var type = await _db.ItemTypes.FindAsync(new object[] { n.ItemTypeId }, ct) ?? throw new NotFoundException("Item type");
            if (await _db.Items.AnyAsync(i => i.Identifier == n.Identifier, ct)) throw new DomainException($"Identifier {n.Identifier} already exists");
            var state = n.State ?? type.Lifecycle?.Initial;
            if (type.Lifecycle != null && !type.Lifecycle.IsValidState(state)) throw new DomainException($"Invalid state {state}");
            item = new Item
            {
                TenantId = _ctx.TenantId, ItemTypeId = type.Id, ItemType = type, Identifier = n.Identifier, Name = string.IsNullOrEmpty(n.Name) ? n.Identifier : n.Name, State = state,
                Quantity = n.Quantity ?? 1, Unit = type.Unit, LotNumber = n.LotNumber, ExpiryDate = n.ExpiryDate, CurrentLocationId = to?.Id, Attributes = n.Attributes ?? new(),
                NextInspectionDue = type.RequiresInspection && type.InspectionIntervalDays is int d ? now.AddDays(d) : null, LastSeenAt = now, LastSeenLocationId = to?.Id,
            };
            _db.Items.Add(item);
            await AddEventAsync(new ItemEvent { ItemId = item.Id, Type = ItemEventType.Created, ToLocationId = to?.Id, ToState = state, OperationId = op.Id, OccurredAt = now }, item, null, to, null, ct);
        }
        if (tag == null || tag.Id == Guid.Empty || _db.Tags.Local.All(t => t.Id != tag.Id) && !await _db.Tags.AnyAsync(t => t.Id == tag.Id, ct))
        {
            tag = new Tag { TenantId = _ctx.TenantId, Epc = epc, Tid = lineReq.Tid, Technology = lineReq.NewItem?.Technology ?? (TagResolver.IsHexEpc(epc) ? TagTechnology.UhfGen2 : TagTechnology.Barcode) };
            _db.Tags.Add(tag);
        }
        tag.ItemId = item.Id; tag.Item = item; tag.Status = TagStatus.Active; tag.EncodedAt = now;
        await AddEventAsync(new ItemEvent { ItemId = item.Id, Type = ItemEventType.Commissioned, OperationId = op.Id, OccurredAt = now, Data = new() { ["epc"] = epc } }, item, null, null, null, ct);
        return item;
    }

    /// <summary>Moves an item and, recursively, everything packed inside it. Child moves are real events: rules and live updates fire for them too.</summary>
    private async Task MoveAsync(Item item, Location to, DateTime now, CancellationToken ct, int depth = 0, Operation? op = null)
    {
        if (item.CurrentLocationId == to.Id) return;
        if (depth > MaxContainerDepth) throw new DomainException($"Container nesting deeper than {MaxContainerDepth} levels – refusing to move");
        item.CurrentLocationId = to.Id; item.CurrentLocation = to; item.LastSeenLocationId = to.Id; item.LastSeenAt = now;
        if (item.ItemType?.IsContainer == true)
        {
            var children = await _db.Items.Include(i => i.ItemType).Include(i => i.CurrentLocation).Where(i => i.ParentItemId == item.Id && i.Status != ItemStatus.Disposed).ToListAsync(ct);
            foreach (var child in children)
            {
                var prev = child.CurrentLocationId; var prevLoc = child.CurrentLocation;
                await MoveAsync(child, to, now, ct, depth + 1, op);
                await AddEventAsync(new ItemEvent { ItemId = child.Id, Type = ItemEventType.Moved, FromLocationId = prev, ToLocationId = to.Id, OccurredAt = now, OperationId = op?.Id, DeviceId = op?.DeviceId, Data = new() { ["viaContainer"] = item.Identifier, ["viaContainerId"] = item.Id } }, child, prevLoc, to, null, ct);
            }
        }
    }

    private async Task AddEventAsync(ItemEvent ev, Item item, Location? from, Location? to, Party? party, CancellationToken ct)
    {
        ev.TenantId = _ctx.TenantId;
        ev.UserId ??= _ctx.UserId;
        _db.ItemEvents.Add(ev);
        await _live.PublishEventAsync(ev, ct);
        await _rules.EvaluateAsync(new RuleContext { Event = ev, Item = item, ItemType = item.ItemType, FromLocation = from, ToLocation = to, Party = party }, ct);
    }

    private static OperationResult ToResult(Operation op, List<OperationLineResult> lines)
    {
        if (lines.Count == 0) lines = op.Lines.Select(l => new OperationLineResult { Epc = l.Epc, ItemId = l.ItemId, Result = l.Result, Message = l.Message }).ToList();
        return new OperationResult { OperationId = op.Id, Status = op.Status, Lines = lines, Ok = lines.Count(l => l.Result == LineResult.Ok), Unknown = lines.Count(l => l.Result == LineResult.Unknown), Rejected = lines.Count(l => l.Result == LineResult.Rejected) };
    }
}
