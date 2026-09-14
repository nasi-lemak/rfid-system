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
