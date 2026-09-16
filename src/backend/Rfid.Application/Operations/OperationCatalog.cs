using Microsoft.EntityFrameworkCore;
using Rfid.Application.Contracts;
using Rfid.Domain;
using Rfid.Domain.Entities;
using static Rfid.Domain.Entities.OperationEffectKinds;

namespace Rfid.Application.Operations;

/// <summary>The built-in operations, expressed as definitions in the same effect vocabulary templates use.</summary>
public static class OperationCatalog
{
    private static OperationEffect E(string kind, params (string k, object? v)[] p) => new(kind, p.ToDictionary(x => x.k, x => x.v));
    private static OperationDefinition D(OperationType type, string name, string description, ItemEventType ev, OperationRequirements req, params OperationEffect[] effects)
        => new() { Code = type.ToString(), Name = name, Description = description, BaseType = type, EventType = ev, Requires = req, Effects = effects.ToList(), IsBuiltIn = true, Enabled = true, EventData = Cbv(type) };

    /// <summary>GS1 CBV business step / disposition each built-in writes into its events (EPCIS projection reads them; templates may override per definition).</summary>
    private static Dictionary<string, object?> Cbv(OperationType t) => t switch
    {
        OperationType.Receive => new() { ["bizStep"] = "receiving", ["disposition"] = "in_progress" },
        OperationType.Transfer => new() { ["bizStep"] = "storing", ["disposition"] = "in_progress" },
        OperationType.Dispatch => new() { ["bizStep"] = "shipping", ["disposition"] = "in_transit" },
        OperationType.Issue => new() { ["bizStep"] = "shipping", ["disposition"] = "in_transit" },
        OperationType.Return => new() { ["bizStep"] = "receiving", ["disposition"] = "returned" },
        OperationType.Count => new() { ["bizStep"] = "cycle_counting", ["disposition"] = "in_progress" },
        OperationType.Inspect => new() { ["bizStep"] = "inspecting" },
        OperationType.Maintain => new() { ["bizStep"] = "repairing", ["disposition"] = "active" },
        OperationType.Dispose => new() { ["bizStep"] = "destroying", ["disposition"] = "destroyed" },
        OperationType.Pack => new() { ["bizStep"] = "packing", ["disposition"] = "in_progress" },
        OperationType.Unpack => new() { ["bizStep"] = "unpacking", ["disposition"] = "in_progress" },
        OperationType.ProcessStage => new() { ["bizStep"] = "transforming", ["disposition"] = "active" },
        OperationType.Commission => new() { ["bizStep"] = "commissioning", ["disposition"] = "active" },
        OperationType.Adjust => new() { ["bizStep"] = "stock_taking", ["disposition"] = "in_progress" },
        OperationType.MoveQuantity => new() { ["bizStep"] = "storing", ["disposition"] = "in_progress" },
        _ => new(),
    };

    public static readonly IReadOnlyList<OperationDefinition> BuiltIn = new List<OperationDefinition>
    {
        D(OperationType.Receive, "Receive", "Goods/assets arrive: item becomes Active at the destination", ItemEventType.Moved, new() { ToLocation = true }, E(Activate), E(Move)),
        D(OperationType.Transfer, "Transfer", "Move to a destination; optionally hand custody to a party", ItemEventType.Moved, new() { ToLocation = true }, E(Move), E(SetCustodian, ("optional", true))),
        new() { Code = "Dispatch", Name = "Dispatch", Description = "Ship out to a destination/party with an optional due-back date", BaseType = OperationType.Dispatch, EventType = ItemEventType.Moved, Requires = new() { ToLocation = true }, Effects = { E(Move), E(SetCustodian, ("optional", true)), E(SetDueBack) }, EventData = { ["dispatch"] = true, ["bizStep"] = "shipping", ["disposition"] = "in_transit" }, IsBuiltIn = true },
        D(OperationType.Issue, "Issue", "Hand custody to a party (tool crib, linen issue, loans)", ItemEventType.CustodyChanged, new() { Party = true }, E(SetCustodian), E(SetDueBack), E(Move, ("optional", true))),
        D(OperationType.Return, "Return", "Custody comes back; optionally place the item", ItemEventType.CustodyChanged, new(), E(ClearCustodian), E(ClearDueBack), E(Move, ("optional", true))),
        D(OperationType.Count, "Count", "Confirm presence (clears Missing); records counted quantity for quantity items", ItemEventType.Counted, new(), E(RecordSeen)),
        D(OperationType.Inspect, "Inspect", "Record an inspection result (target state = result)", ItemEventType.Inspected, new(), E(RecordInspection), E(SetState, ("free", false))),
        D(OperationType.Maintain, "Maintain", "Service / repair; optionally move to the workshop", ItemEventType.Maintained, new(), E(Move, ("optional", true)), E(SetState, ("free", false))),
        D(OperationType.Dispose, "Dispose", "Retire the item and its tags", ItemEventType.Disposed, new(), E(Dispose)),
        D(OperationType.Pack, "Pack", "Put items into a container (nested containers allowed, cycles refused)", ItemEventType.Packed, new() { Container = true }, E(Pack)),
        D(OperationType.Unpack, "Unpack", "Take items out of their container; optionally place them", ItemEventType.Unpacked, new(), E(Unpack), E(Move, ("optional", true))),
        D(OperationType.ProcessStage, "Process stage", "Advance the lifecycle to the target state; optionally move", ItemEventType.StateChanged, new(), E(SetState, ("free", true)), E(Move, ("optional", true))),
        D(OperationType.Commission, "Commission", "Create/bind items to tags", ItemEventType.Commissioned, new()),
        D(OperationType.Adjust, "Adjust", "Change a quantity item's stock by a delta", ItemEventType.QuantityChanged, new() { Quantity = true }, E(AdjustQuantity)),
        D(OperationType.MoveQuantity, "Move quantity", "Move part of a quantity item (lot/SKU) to another location: the destination lot row is created or topped up", ItemEventType.QuantityChanged, new() { ToLocation = true, Quantity = true }, E(MoveQuantity)),
    };

    public static OperationDefinition? Get(string code) => BuiltIn.FirstOrDefault(d => string.Equals(d.Code, code, StringComparison.OrdinalIgnoreCase));

    public static ItemEventType DefaultEvent(OperationType t) => Get(t.ToString())?.EventType ?? ItemEventType.Seen;

    /// <summary>Validates a custom definition: known effects, a real base type, and requirements consistent with effects.</summary>
    public static List<string> Validate(OperationDefinition d)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(d.Code) || !System.Text.RegularExpressions.Regex.IsMatch(d.Code, "^[A-Za-z][A-Za-z0-9_-]{1,40}$")) errors.Add("Code must be 2–40 letters/digits/_/- and start with a letter");
        if (Get(d.Code) != null && !d.IsBuiltIn) errors.Add($"'{d.Code}' is a built-in operation");
        if (d.BaseType == OperationType.Commission) errors.Add("Commission cannot be a base type for custom operations");
        foreach (var e in d.Effects) if (!All.Contains(e.Kind)) errors.Add($"Unknown effect '{e.Kind}'");
        if (d.Effects.Count == 0) errors.Add("At least one effect is required");
        if (d.Effects.Any(e => e.Kind == Pack) && !d.Requires.Container) errors.Add("Pack requires a container");
        if (d.Effects.Any(e => e.Kind == AdjustQuantity) && !d.Requires.Quantity) errors.Add("AdjustQuantity requires a quantity");
        if (d.Effects.Any(e => e.Kind == MoveQuantity) && !(d.Requires.Quantity && d.Requires.ToLocation)) errors.Add("MoveQuantity requires a quantity and a destination");
        if (d.Effects.Any(e => e.Kind == SetCustodian && !IsOptional(e)) && !d.Requires.Party) errors.Add("SetCustodian requires a party unless optional");
        return errors;
    }

    /// <summary>An effect flagged <c>optional</c> is skipped when its input (party, location) is absent instead of failing.</summary>
    public static bool IsOptional(OperationEffect e)
        => e.Params.TryGetValue("optional", out var o) && o != null && string.Equals(o.ToString(), "true", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Resolves operation codes to definitions: tenant-defined first, then built-ins. Cached per unit of work.</summary>
public class OperationDefinitions
{
    private readonly IAppDb _db; private List<OperationDefinition>? _custom;
    public OperationDefinitions(IAppDb db) => _db = db;

    public async Task<OperationDefinition> ResolveAsync(string? code, OperationType fallback, CancellationToken ct = default)
    {
        var c = string.IsNullOrWhiteSpace(code) ? fallback.ToString() : code.Trim();
        _custom ??= await _db.OperationDefinitions.Where(d => d.Enabled).ToListAsync(ct);
        return _custom.FirstOrDefault(d => string.Equals(d.Code, c, StringComparison.OrdinalIgnoreCase))
            ?? OperationCatalog.Get(c)
            ?? throw new DomainException($"Unknown operation '{c}'");
    }

    public async Task<List<OperationDefinition>> ListAsync(CancellationToken ct = default)
    {
        var custom = await _db.OperationDefinitions.OrderBy(d => d.Name).ToListAsync(ct);
        return OperationCatalog.BuiltIn.Concat(custom).ToList();
    }
    public void InvalidateCache() => _custom = null;
}
