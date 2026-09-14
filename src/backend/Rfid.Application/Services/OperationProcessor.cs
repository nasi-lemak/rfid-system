using Microsoft.EntityFrameworkCore;
using Rfid.Application.Contracts;
using Rfid.Domain;
using Rfid.Domain.Entities;

namespace Rfid.Application.Services;

/// <summary>
/// Applies a business operation (receive, transfer, issue, return, count, …) to the items behind
/// the scanned tags, writes the audit trail and runs rules. One unit of work per operation.
/// </summary>
public class OperationProcessor
{
    private readonly IAppDb _db;
    private readonly ICurrentContext _ctx;
    private readonly TagResolver _tags;
    private readonly RuleEngine _rules;
    private readonly ILivePublisher _live;

    public OperationProcessor(IAppDb db, ICurrentContext ctx, TagResolver tags, RuleEngine rules, ILivePublisher live)
    {
        _db = db; _ctx = ctx; _tags = tags; _rules = rules; _live = live;
    }

    public async Task<OperationResult> ProcessAsync(OperationRequest req, CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(req.ClientId))
        {
            var existing = await _db.Operations.Include(o => o.Lines)
                .FirstOrDefaultAsync(o => o.Reference == req.Reference && o.Notes == "client:" + req.ClientId, ct);
            if (existing != null) return ToResult(existing, new());
        }

        var now = req.OccurredAt ?? DateTime.UtcNow;
        Validate(req);

        var from = req.FromLocationId.HasValue ? await _db.Locations.FindAsync(new object[] { req.FromLocationId.Value }, ct) : null;
        var to = req.ToLocationId.HasValue ? await _db.Locations.FindAsync(new object[] { req.ToLocationId.Value }, ct) : null;
        var party = req.PartyId.HasValue ? await _db.Parties.FindAsync(new object[] { req.PartyId.Value }, ct) : null;
        if (req.ToLocationId.HasValue && to == null) throw new NotFoundException("To location");
        if (req.FromLocationId.HasValue && from == null) throw new NotFoundException("From location");
        if (req.PartyId.HasValue && party == null) throw new NotFoundException("Party");

        Item? container = null;
        if (req.ContainerItemId.HasValue)
        {
            container = await _db.Items.Include(i => i.ItemType).FirstOrDefaultAsync(i => i.Id == req.ContainerItemId, ct)
                        ?? throw new NotFoundException("Container item");
            if (container.ItemType?.IsContainer != true) throw new DomainException($"{container.Name} is not a container");
        }

        var op = new Operation
        {
            TenantId = _ctx.TenantId, Type = req.Type, FromLocationId = from?.Id, ToLocationId = to?.Id,
            PartyId = party?.Id, ContainerItemId = container?.Id, TargetState = req.TargetState,
            Reference = req.Reference, Notes = string.IsNullOrEmpty(req.ClientId) ? req.Notes : "client:" + req.ClientId,
            DeviceId = req.DeviceId ?? _ctx.DeviceId, UserId = _ctx.UserId, StartedAt = now, DueBackAt = req.DueBackAt,
        };
        _db.Operations.Add(op);

        var epcs = req.Lines.Where(l => !string.IsNullOrWhiteSpace(l.Epc)).Select(l => l.Epc!).ToList();
        var tagMap = await _tags.ResolveAsync(epcs, ct);
        var itemIds = req.Lines.Where(l => l.ItemId.HasValue).Select(l => l.ItemId!.Value).ToList();
        var identifiers = req.Lines.Where(l => !string.IsNullOrWhiteSpace(l.Identifier)).Select(l => l.Identifier!).ToList();
        var itemsById = itemIds.Count == 0 ? new() : await _db.Items.Include(i => i.ItemType).Include(i => i.CurrentLocation)
            .Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        var itemsByIdent = identifiers.Count == 0 ? new() : await _db.Items.Include(i => i.ItemType).Include(i => i.CurrentLocation)
            .Where(i => identifiers.Contains(i.Identifier)).ToDictionaryAsync(i => i.Identifier, ct);

        var results = new List<OperationLineResult>();
        var touched = new HashSet<Guid>();
        foreach (var lineReq in req.Lines)
        {
            var epc = string.IsNullOrWhiteSpace(lineReq.Epc) ? null : TagResolver.Normalize(lineReq.Epc);
            var line = new OperationLine { OperationId = op.Id, Epc = epc, Quantity = lineReq.Quantity };
            op.Lines.Add(line);
            var res = new OperationLineResult { Epc = epc };
            results.Add(res);

            try
            {
                Item? item = null;
                if (req.Type == OperationType.Commission)
                {
                    item = await CommissionAsync(lineReq, epc, tagMap, to, now, op, ct);
                }
                else
                {
                    if (lineReq.ItemId.HasValue) itemsById.TryGetValue(lineReq.ItemId.Value, out item);
                    if (item == null && epc != null && tagMap.TryGetValue(epc, out var tag)) item = tag.Item;
                    if (item == null && lineReq.Identifier != null) itemsByIdent.TryGetValue(lineReq.Identifier, out item);
                    if (item == null)
                    {
                        line.Result = LineResult.Unknown; line.Message = "Tag not commissioned";
                        res.Result = LineResult.Unknown; res.Message = line.Message;
                        continue;
                    }
                    if (!touched.Add(item.Id))
                    {
                        line.Result = LineResult.Rejected; line.Message = "Duplicate in operation";
                        res.Result = LineResult.Rejected; res.Message = line.Message; res.ItemId = item.Id;
                        continue;
                    }
                    await ApplyAsync(req.Type, item, lineReq, op, from, to, party, container, req.TargetState, now, ct);
                }
                line.ItemId = item.Id; line.Result = LineResult.Ok;
                res.ItemId = item.Id; res.ItemName = item.Name; res.Result = LineResult.Ok; res.NewState = item.State;
            }
            catch (DomainException ex)
            {
                line.Result = LineResult.Rejected; line.Message = ex.Message;
                res.Result = LineResult.Rejected; res.Message = ex.Message;
            }
        }

        op.Status = OperationStatus.Completed;
        op.CompletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return ToResult(op, results);
    }

    private static void Validate(OperationRequest req)
    {
        switch (req.Type)
        {
            case OperationType.Receive:
            case OperationType.Transfer:
            case OperationType.Dispatch:
                if (!req.ToLocationId.HasValue) throw new DomainException($"{req.Type} requires a destination location");
                break;
            case OperationType.Issue:
                if (!req.PartyId.HasValue) throw new DomainException("Issue requires a party (who receives custody)");
                break;
            case OperationType.Pack:
                if (!req.ContainerItemId.HasValue) throw new DomainException("Pack requires a container item");
                break;
            case OperationType.Commission:
                if (req.Lines.Any(l => l.NewItem == null && !l.ItemId.HasValue && string.IsNullOrEmpty(l.Identifier)))
                    throw new DomainException("Commission lines need a NewItem, ItemId or Identifier to bind the tag to");
                break;
        }
    }

    private async Task<Item> CommissionAsync(OperationLineRequest lineReq, string? epc, Dictionary<string, Tag> tagMap,
        Location? to, DateTime now, Operation op, CancellationToken ct)
    {
        if (epc == null) throw new DomainException("Commission requires an EPC");
        Tag? tag = tagMap.GetValueOrDefault(epc);
        if (tag?.ItemId != null)
            throw new DomainException($"EPC {epc} already bound to {tag.Item?.Name ?? tag.ItemId.ToString()}");

        Item? item = null;
        if (lineReq.ItemId.HasValue)
            item = await _db.Items.Include(i => i.ItemType).FirstOrDefaultAsync(i => i.Id == lineReq.ItemId, ct);
        else if (!string.IsNullOrEmpty(lineReq.Identifier) && lineReq.NewItem == null)
            item = await _db.Items.Include(i => i.ItemType).FirstOrDefaultAsync(i => i.Identifier == lineReq.Identifier, ct);
        if (item == null && lineReq.NewItem == null) throw new NotFoundException("Item to bind");

        if (item == null)
        {
            var n = lineReq.NewItem!;
            var type = await _db.ItemTypes.FindAsync(new object[] { n.ItemTypeId }, ct) ?? throw new NotFoundException("Item type");
            if (await _db.Items.AnyAsync(i => i.Identifier == n.Identifier, ct))
                throw new DomainException($"Identifier {n.Identifier} already exists");
            var state = n.State ?? type.Lifecycle?.Initial;
            if (type.Lifecycle != null && !type.Lifecycle.IsValidState(state)) throw new DomainException($"Invalid state {state}");
            item = new Item
            {
                TenantId = _ctx.TenantId, ItemTypeId = type.Id, ItemType = type, Identifier = n.Identifier,
                Name = string.IsNullOrEmpty(n.Name) ? n.Identifier : n.Name, State = state,
                Quantity = n.Quantity ?? 1, Unit = type.Unit, LotNumber = n.LotNumber, ExpiryDate = n.ExpiryDate,
                CurrentLocationId = to?.Id, Attributes = n.Attributes ?? new(),
                NextInspectionDue = type.RequiresInspection && type.InspectionIntervalDays is int d ? now.AddDays(d) : null,
                LastSeenAt = now, LastSeenLocationId = to?.Id,
            };
            _db.Items.Add(item);
            await AddEventAsync(new ItemEvent { ItemId = item.Id, Type = ItemEventType.Created, ToLocationId = to?.Id, ToState = state, OperationId = op.Id, OccurredAt = now }, item, null, to, null, ct);
        }

        if (tag == null)
        {
            tag = new Tag { TenantId = _ctx.TenantId, Epc = epc, Tid = lineReq.Tid, Technology = lineReq.NewItem?.Technology ?? TagTechnology.UhfGen2 };
            _db.Tags.Add(tag);
        }
        tag.ItemId = item.Id; tag.Item = item; tag.Status = TagStatus.Active; tag.EncodedAt = now;
        await AddEventAsync(new ItemEvent { ItemId = item.Id, Type = ItemEventType.Commissioned, OperationId = op.Id, OccurredAt = now, Data = new() { ["epc"] = epc } }, item, null, null, null, ct);
        return item;
    }

    private async Task ApplyAsync(OperationType type, Item item, OperationLineRequest lineReq, Operation op,
        Location? from, Location? to, Party? party, Item? container, string? targetState, DateTime now, CancellationToken ct)
    {
        if (item.Status == ItemStatus.Disposed) throw new DomainException($"{item.Name} is disposed");
        var lifecycle = item.ItemType?.Lifecycle;

        // 1. Lifecycle transition (if the type defines one for this operation).
        string? prevState = item.State;
        bool stateChanged = false;
        if (lifecycle != null)
        {
            var hasAny = lifecycle.Transitions.Any(t => string.Equals(t.On, type.ToString(), StringComparison.OrdinalIgnoreCase));
            var tr = lifecycle.FindTransition(item.State, type, targetState);
            if (tr != null)
            {
                item.State = tr.To;
                if (tr.IncrementCycle) item.CycleCount++;
                stateChanged = !string.Equals(prevState, tr.To, StringComparison.OrdinalIgnoreCase);
            }
            else if (hasAny && type != OperationType.ProcessStage)
            {
                throw new DomainException($"{type} not allowed while {item.Name} is '{item.State ?? "(none)"}'");
            }
            else if (type == OperationType.ProcessStage && targetState != null)
            {
                if (!lifecycle.IsValidState(targetState)) throw new DomainException($"Invalid state '{targetState}'");
                if (hasAny) throw new DomainException($"No transition from '{item.State}' to '{targetState}'");
                item.State = targetState; stateChanged = prevState != targetState;
            }
        }
        else if (type is OperationType.ProcessStage or OperationType.Inspect or OperationType.Maintain && targetState != null)
        {
            item.State = targetState; stateChanged = prevState != targetState;
        }

        // 2. Effect of the operation type.
        Guid? prevLoc = item.CurrentLocationId;
        Guid? prevParty = item.CustodianPartyId;
        ItemEventType evType = ItemEventType.Seen;
        var data = new Dictionary<string, object?>();
        switch (type)
        {
            case OperationType.Receive:
                item.Status = ItemStatus.Active;
                await MoveAsync(item, to!, now, ct);
                evType = ItemEventType.Moved;
                break;
            case OperationType.Transfer:
                await MoveAsync(item, to!, now, ct);
                if (party != null) item.CustodianPartyId = party.Id;
                evType = ItemEventType.Moved;
                break;
            case OperationType.Dispatch:
                await MoveAsync(item, to!, now, ct);
                if (party != null) item.CustodianPartyId = party.Id;
                if (op.DueBackAt.HasValue) item.DueBackAt = op.DueBackAt;
                evType = ItemEventType.Moved;
                data["dispatch"] = true;
                break;
            case OperationType.Issue:
                item.CustodianPartyId = party!.Id;
                item.DueBackAt = op.DueBackAt;
                if (to != null) await MoveAsync(item, to, now, ct);
                evType = ItemEventType.CustodyChanged;
                break;
            case OperationType.Return:
                item.CustodianPartyId = null;
                item.DueBackAt = null;
                if (to != null) await MoveAsync(item, to, now, ct);
                evType = ItemEventType.CustodyChanged;
                break;
            case OperationType.Count:
                item.LastSeenAt = now; item.LastSeenLocationId = to?.Id ?? item.CurrentLocationId; item.LastSeenDeviceId = op.DeviceId;
                if (item.Status == ItemStatus.Missing) item.Status = ItemStatus.Active;
                evType = ItemEventType.Counted;
                if (to != null && item.CurrentLocationId != to.Id) data["foundElsewhere"] = true;
                break;
            case OperationType.Inspect:
                item.LastInspectedAt = now;
                item.NextInspectionDue = item.ItemType?.InspectionIntervalDays is int d ? now.AddDays(d) : null;
                evType = ItemEventType.Inspected;
                data["result"] = targetState ?? "Passed";
                break;
            case OperationType.Maintain:
                if (to != null) await MoveAsync(item, to, now, ct);
                evType = ItemEventType.Maintained;
                break;
            case OperationType.Dispose:
                item.Status = ItemStatus.Disposed;
                item.CustodianPartyId = null;
                foreach (var t in await _db.Tags.Where(t => t.ItemId == item.Id).ToListAsync(ct)) t.Status = TagStatus.Retired;
                evType = ItemEventType.Disposed;
                break;
            case OperationType.Pack:
                if (container!.Id == item.Id) throw new DomainException("Cannot pack a container into itself");
                item.ParentItemId = container.Id;
                if (container.CurrentLocationId.HasValue && item.CurrentLocationId != container.CurrentLocationId)
                    await MoveAsync(item, (await _db.Locations.FindAsync(new object[] { container.CurrentLocationId.Value }, ct))!, now, ct);
                evType = ItemEventType.Packed; data["container"] = container.Identifier;
                break;
            case OperationType.Unpack:
                data["container"] = item.ParentItemId;
                item.ParentItemId = null;
                if (to != null) await MoveAsync(item, to, now, ct);
                evType = ItemEventType.Unpacked;
                break;
            case OperationType.ProcessStage:
                if (to != null) await MoveAsync(item, to, now, ct);
                evType = ItemEventType.StateChanged;
                break;
            case OperationType.Adjust:
                if (lineReq.Quantity == null) throw new DomainException("Adjust requires a quantity delta");
                data["delta"] = lineReq.Quantity; data["before"] = item.Quantity;
                item.Quantity += lineReq.Quantity.Value;
                if (item.Quantity < 0) throw new DomainException("Quantity cannot go negative");
                evType = ItemEventType.QuantityChanged;
                break;
        }

        item.LastSeenAt = now; item.LastSeenDeviceId = op.DeviceId;
        if (item.CurrentLocationId.HasValue) item.LastSeenLocationId = item.CurrentLocationId;

        var ev = new ItemEvent
        {
            ItemId = item.Id, Type = evType, FromLocationId = prevLoc, ToLocationId = item.CurrentLocationId,
            FromPartyId = prevParty, ToPartyId = item.CustodianPartyId, FromState = prevState, ToState = item.State,
            OperationId = op.Id, DeviceId = op.DeviceId, UserId = _ctx.UserId, OccurredAt = now, Data = data,
        };
        await AddEventAsync(ev, item, from, to ?? item.CurrentLocation, party, ct);
        if (stateChanged && evType != ItemEventType.StateChanged)
            await AddEventAsync(new ItemEvent { ItemId = item.Id, Type = ItemEventType.StateChanged, FromState = prevState, ToState = item.State, OperationId = op.Id, OccurredAt = now }, item, from, to, party, ct);
    }

    /// <summary>Moves an item and, when it is a container, everything packed inside it.</summary>
    private async Task MoveAsync(Item item, Location to, DateTime now, CancellationToken ct)
    {
        if (item.CurrentLocationId == to.Id) return;
        item.CurrentLocationId = to.Id; item.CurrentLocation = to; item.LastSeenLocationId = to.Id; item.LastSeenAt = now;
        if (item.ItemType?.IsContainer == true)
        {
            var children = await _db.Items.Include(i => i.ItemType).Where(i => i.ParentItemId == item.Id).ToListAsync(ct);
            foreach (var child in children)
            {
                var prev = child.CurrentLocationId;
                await MoveAsync(child, to, now, ct);
                _db.ItemEvents.Add(new ItemEvent { TenantId = _ctx.TenantId, ItemId = child.Id, Type = ItemEventType.Moved, FromLocationId = prev, ToLocationId = to.Id, OccurredAt = now, Data = new() { ["viaContainer"] = item.Identifier } });
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
        if (lines.Count == 0)
            lines = op.Lines.Select(l => new OperationLineResult { Epc = l.Epc, ItemId = l.ItemId, Result = l.Result, Message = l.Message }).ToList();
        return new OperationResult
        {
            OperationId = op.Id, Status = op.Status, Lines = lines,
            Ok = lines.Count(l => l.Result == LineResult.Ok),
            Unknown = lines.Count(l => l.Result == LineResult.Unknown),
            Rejected = lines.Count(l => l.Result == LineResult.Rejected),
        };
    }
}
