using Rfid.Domain;

namespace Rfid.Application.Contracts;

public class OperationRequest
{
    /// <summary>Built-in base type; ignored when Operation names a definition code.</summary>
    public OperationType Type { get; set; }
    /// <summary>Operation definition code (built-in type name or a tenant/template-defined operation such as "Sterilise").</summary>
    public string? Operation { get; set; }
    public Guid? FromLocationId { get; set; }
    public Guid? ToLocationId { get; set; }
    public Guid? PartyId { get; set; }
    public Guid? ContainerItemId { get; set; }
    public string? TargetState { get; set; }
    public string? Reference { get; set; }
    public string? Notes { get; set; }
    public Guid? DeviceId { get; set; }
    public DateTime? DueBackAt { get; set; }
    public DateTime? OccurredAt { get; set; }
    /// <summary>Client-generated idempotency key (offline handhelds retry).</summary>
    public string? ClientId { get; set; }
    /// <summary>Guided workflow context: the run this operation is a step of (client-generated run id), the workflow code and the step key.</summary>
    public string? WorkflowRunId { get; set; }
    public string? WorkflowCode { get; set; }
    public string? WorkflowStep { get; set; }
    public List<OperationLineRequest> Lines { get; set; } = new();
}

public class OperationLineRequest
{
    public string? Epc { get; set; }
    public string? Tid { get; set; }
    public Guid? ItemId { get; set; }
    public string? Identifier { get; set; }
    public decimal? Quantity { get; set; }
    /// <summary>Only for Commission: the item to create and bind to Epc.</summary>
    public NewItemRequest? NewItem { get; set; }
}

public class NewItemRequest
{
    public Guid ItemTypeId { get; set; }
    public string Identifier { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal? Quantity { get; set; }
    public string? LotNumber { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public string? State { get; set; }
    public Dictionary<string, object?>? Attributes { get; set; }
    public TagTechnology Technology { get; set; } = TagTechnology.UhfGen2;
}

public class OperationResult
{
    public Guid OperationId { get; set; }
    public OperationStatus Status { get; set; }
    public int Ok { get; set; }
    public int Unknown { get; set; }
    public int Rejected { get; set; }
    public List<OperationLineResult> Lines { get; set; } = new();
}

public class OperationLineResult
{
    public string? Epc { get; set; }
    public Guid? ItemId { get; set; }
    public string? ItemName { get; set; }
    public LineResult Result { get; set; }
    public string? Message { get; set; }
    public string? NewState { get; set; }
}


public class StocktakeScanRequest
{
    public List<string> Epcs { get; set; } = new();
    public Guid? DeviceId { get; set; }
    public Guid? LocationId { get; set; }
}

public class StocktakeSummary
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public StocktakeStatus Status { get; set; }
    public int Expected { get; set; }
    public int Found { get; set; }
    public int Missing { get; set; }
    public int Unexpected { get; set; }
    public int Unknown { get; set; }
}
