namespace Rfid.Domain.Entities;

public class Operation : TenantEntity
{
    public OperationType Type { get; set; }
    public OperationStatus Status { get; set; } = OperationStatus.Draft;
    public Guid? FromLocationId { get; set; }
    public Guid? ToLocationId { get; set; }
    public Guid? PartyId { get; set; }
    public Guid? ContainerItemId { get; set; }
    public string? TargetState { get; set; }
    public string? Reference { get; set; }
    public string? Notes { get; set; }
    public Guid? DeviceId { get; set; }
    public Guid? UserId { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public DateTime? DueBackAt { get; set; }
    /// <summary>Operation definition that ran (a built-in type name or a tenant-defined code); Type keeps the semantic base category.</summary>
    public string? DefinitionCode { get; set; }
    /// <summary>Client-generated idempotency key (offline handheld queues, edge agents).</summary>
    public string? ClientId { get; set; }
    public List<OperationLine> Lines { get; set; } = new();
}

public class OperationLine : EntityBase
{
    public Guid OperationId { get; set; }
    public string? Epc { get; set; }
    public Guid? ItemId { get; set; }
    public decimal? Quantity { get; set; }
    public LineResult Result { get; set; } = LineResult.Ok;
    public string? Message { get; set; }
}

public class ItemEvent : TenantEntity
{
    public Guid ItemId { get; set; }
    public ItemEventType Type { get; set; }
    public Guid? FromLocationId { get; set; }
    public Guid? ToLocationId { get; set; }
    public Guid? FromPartyId { get; set; }
    public Guid? ToPartyId { get; set; }
    public string? FromState { get; set; }
    public string? ToState { get; set; }
    public Guid? OperationId { get; set; }
    public Guid? DeviceId { get; set; }
    public Guid? UserId { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public Dictionary<string, object?> Data { get; set; } = new();
}

public class Stocktake : TenantEntity
{
    public string Name { get; set; } = "";
    public Guid LocationId { get; set; }
    public Guid? ItemTypeId { get; set; }
    public StocktakeStatus Status { get; set; } = StocktakeStatus.Open;
    public int ExpectedCount { get; set; }
    public int FoundCount { get; set; }
    public int MissingCount { get; set; }
    public int UnexpectedCount { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public Guid? UserId { get; set; }
    public List<StocktakeLine> Lines { get; set; } = new();
}

public class StocktakeLine : EntityBase
{
    public Guid StocktakeId { get; set; }
    public Guid? ItemId { get; set; }
    public string? Epc { get; set; }
    public bool Expected { get; set; }
    public StocktakeResult Result { get; set; } = StocktakeResult.Pending;
    public DateTime? FoundAt { get; set; }
    public Guid? FoundLocationId { get; set; }
}

/// <summary>
/// A business operation expressed as an ordered list of built-in effects plus what the request must supply.
/// The 14 built-in operation types are definitions too (see OperationCatalog); templates and tenants add their own
/// (e.g. "Sterilise" = SetState + IncrementCycle + Move) without touching application code.
/// </summary>
public class OperationDefinition : TenantEntity
{
    /// <summary>Unique per tenant; built-ins use the OperationType name.</summary>
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    /// <summary>Semantic base category used by analytics, EPCIS mapping, billing and lifecycle fallbacks.</summary>
    public OperationType BaseType { get; set; } = OperationType.ProcessStage;
    /// <summary>Event written per item; null → the base type's default.</summary>
    public ItemEventType? EventType { get; set; }
    public List<OperationEffect> Effects { get; set; } = new();
    public OperationRequirements Requires { get; set; } = new();
    /// <summary>Constant data merged into every item event (e.g. {"dispatch": true}).</summary>
    public Dictionary<string, object?> EventData { get; set; } = new();
    /// <summary>Only items of these item type codes may take part (empty = any).</summary>
    public List<string> ItemTypeCodes { get; set; } = new();
    public bool Enabled { get; set; } = true;
    public bool IsBuiltIn { get; set; }
    public string? Vertical { get; set; }
    public string? Icon { get; set; }
}

/// <summary>One step of an operation. Kinds are a closed, strongly typed set – see OperationEffectKinds.</summary>
public class OperationEffect
{
    public string Kind { get; set; } = "";
    public Dictionary<string, object?> Params { get; set; } = new();
    public OperationEffect() { }
    public OperationEffect(string kind, Dictionary<string, object?>? p = null) { Kind = kind; Params = p ?? new(); }
}

public class OperationRequirements
{
    public bool ToLocation { get; set; }
    public bool Party { get; set; }
    public bool Container { get; set; }
    public bool TargetState { get; set; }
    public bool Quantity { get; set; }
    /// <summary>Items must currently be in one of these states (empty = any); "*" allowed.</summary>
    public List<string> FromStates { get; set; } = new();
}

/// <summary>The closed vocabulary of effects an operation definition may compose.</summary>
public static class OperationEffectKinds
{
    public const string Move = "Move";                     // to the request destination (params: optional=true → skip when no destination)
    public const string SetCustodian = "SetCustodian";     // custodian = request party (params: optional=true)
    public const string ClearCustodian = "ClearCustodian";
    public const string SetDueBack = "SetDueBack";         // dueBack = request dueBackAt
    public const string ClearDueBack = "ClearDueBack";
    public const string SetState = "SetState";             // state = params.state ?? request targetState (validated against the lifecycle)
    public const string IncrementCycle = "IncrementCycle";
    public const string RecordSeen = "RecordSeen";         // lastSeen/count semantics; clears Missing (Count)
    public const string RecordInspection = "RecordInspection"; // lastInspected, nextInspectionDue, data.result
    public const string Activate = "Activate";             // status = Active (Receive)
    public const string Dispose = "Dispose";               // status = Disposed, retire tags, clear custodian
    public const string Pack = "Pack";                     // parent = request container (cycle-checked), move into it
    public const string Unpack = "Unpack";                 // parent = null, optional move
    public const string AdjustQuantity = "AdjustQuantity"; // quantity += line quantity (never below zero)
    public const string SetAttribute = "SetAttribute";     // attributes[params.key] = params.value ?? request targetState
    public static readonly string[] All = { Move, SetCustodian, ClearCustodian, SetDueBack, ClearDueBack, SetState, IncrementCycle, RecordSeen, RecordInspection, Activate, Dispose, Pack, Unpack, AdjustQuantity, SetAttribute };
}
