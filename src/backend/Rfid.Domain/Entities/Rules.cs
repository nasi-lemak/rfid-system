namespace Rfid.Domain.Entities;

public class Rule : TenantEntity
{
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public ItemEventType Trigger { get; set; }
    public List<RuleCondition> Conditions { get; set; } = new();
    public RuleAction Action { get; set; } = RuleAction.CreateAlert;
    public Dictionary<string, object?> Params { get; set; } = new();
    public Severity Severity { get; set; } = Severity.Warning;
    public string? Vertical { get; set; }
}

/// <summary>field: e.g. "item.state", "item.cycleCount", "toLocation.kind", "data.direction". op: eq, ne, gt, gte, lt, lte, in, contains, exists.</summary>
public class RuleCondition
{
    public string Field { get; set; } = "";
    public string Op { get; set; } = "eq";
    public object? Value { get; set; }
}

public class Alert : TenantEntity
{
    public Guid? RuleId { get; set; }
    public Guid? ItemId { get; set; }
    public Guid? LocationId { get; set; }
    public Severity Severity { get; set; }
    public string Message { get; set; } = "";
    public AlertStatus Status { get; set; } = AlertStatus.Open;
    public DateTime RaisedAt { get; set; } = DateTime.UtcNow;
    public Guid? AcknowledgedBy { get; set; }
    public DateTime? ClosedAt { get; set; }
}

public class SolutionTemplate : EntityBase
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Vertical { get; set; } = "";
    public string Description { get; set; } = "";
    public TemplateDefinition Definition { get; set; } = new();
}

public class TemplateDefinition
{
    public List<ItemType> ItemTypes { get; set; } = new();
    public List<Rule> Rules { get; set; } = new();
    public List<LocationKind> LocationKinds { get; set; } = new();
    public List<OperationType> Operations { get; set; } = new();
    public List<PartyKind> PartyKinds { get; set; } = new();
}
