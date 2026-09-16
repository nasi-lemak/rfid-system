namespace Rfid.Domain.Entities;

/// <summary>
/// A guided, multi-step task for the handheld: an ordered list of operation definitions with prompts and
/// per-step fixed inputs. Workflows are configuration (templates ship them, tenants edit them); every step
/// executes as an ordinary operation carrying the run id, so progress and audit are the operations themselves
/// and offline execution works through the same queue.
/// </summary>
public class WorkflowDefinition : TenantEntity
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public string? Vertical { get; set; }
    public string? Icon { get; set; }
    /// <summary>Empty = any item type; otherwise the handheld offers the workflow for these types.</summary>
    public List<string> ItemTypeCodes { get; set; } = new();
    public List<WorkflowStep> Steps { get; set; } = new();
}

public class WorkflowStep
{
    /// <summary>Stable key recorded on each operation (<c>operations.WorkflowStep</c>).</summary>
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    /// <summary>What the operator is told to do at this step.</summary>
    public string? Prompt { get; set; }
    /// <summary>Operation definition code executed by the step (built-in or template/tenant-defined).</summary>
    public string Operation { get; set; } = "";
    /// <summary>Inputs the operator is asked for at this step beyond what the operation requires (toLocation, party, targetState, quantity, dueBack).</summary>
    public List<string> Ask { get; set; } = new();
    /// <summary>Inputs fixed by the workflow (tenant-specific; templates leave them empty).</summary>
    public WorkflowStepFixed Fixed { get; set; } = new();
    /// <summary>Scan a fresh set of tags for this step instead of reusing the previous step's set.</summary>
    public bool Rescan { get; set; }
    /// <summary>The operator may skip this step.</summary>
    public bool Optional { get; set; }
    /// <summary>When a line is rejected: <c>stop</c> (default) the run for those items, or <c>continue</c> with the rest.</summary>
    public string OnRejected { get; set; } = "stop";
}

public class WorkflowStepFixed
{
    public Guid? ToLocationId { get; set; }
    public Guid? PartyId { get; set; }
    public string? TargetState { get; set; }
    public Guid? ContainerItemId { get; set; }
    public string? Reference { get; set; }
}
