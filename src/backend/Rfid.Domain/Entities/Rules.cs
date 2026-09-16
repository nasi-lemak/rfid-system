namespace Rfid.Domain.Entities;

public class Rule : TenantEntity
{
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public RuleKind Kind { get; set; } = RuleKind.Event;
    /// <summary>Event rules: the event type that triggers evaluation.</summary>
    public ItemEventType Trigger { get; set; }
    /// <summary>Schedule rules: how often every item is checked against the conditions.</summary>
    public int IntervalMinutes { get; set; } = 60;
    public DateTime? LastRunAt { get; set; }
    public List<RuleCondition> Conditions { get; set; } = new();
    public RuleAction Action { get; set; } = RuleAction.CreateAlert;
    public Dictionary<string, object?> Params { get; set; } = new();
    public Severity Severity { get; set; } = Severity.Warning;
    public string? Vertical { get; set; }
    /// <summary>Channels notified immediately when the rule raises an alert.</summary>
    public List<Guid> NotifyChannelIds { get; set; } = new();
    /// <summary>Escalation policy for alerts raised by this rule while they stay unacknowledged.</summary>
    public Guid? EscalationPolicyId { get; set; }
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
    public DateTime? AcknowledgedAt { get; set; }
    /// <summary>Source for alerts not raised by a rule: "geofence", "device-health", "stocktake" …</summary>
    public string? Source { get; set; }
    public Guid? DeviceId { get; set; }
    public Guid? EscalationPolicyId { get; set; }
    public int EscalationLevel { get; set; }
    public DateTime? NextEscalationAt { get; set; }
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
    /// <summary>Vertical-specific operations composed from built-in effects (installed per tenant when the template is applied).</summary>
    public List<OperationDefinition> OperationDefinitions { get; set; } = new();
}
