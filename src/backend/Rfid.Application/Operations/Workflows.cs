using Microsoft.EntityFrameworkCore;
using Rfid.Application.Contracts;
using Rfid.Domain;
using Rfid.Domain.Entities;

namespace Rfid.Application.Operations;

/// <summary>Validation and read models for guided workflows. Execution is not here: every step is an ordinary operation.</summary>
public static class WorkflowCatalog
{
    public static readonly string[] AskInputs = { "toLocation", "party", "targetState", "quantity", "dueBack", "container" };

    public static List<string> Validate(WorkflowDefinition w, IEnumerable<OperationDefinition> operations)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(w.Code) || !System.Text.RegularExpressions.Regex.IsMatch(w.Code, "^[A-Za-z][A-Za-z0-9_-]{1,40}$")) errors.Add("Code must be 2–40 letters/digits/_/- and start with a letter");
        if (string.IsNullOrWhiteSpace(w.Name)) errors.Add("Name is required");
        if (w.Steps.Count == 0) errors.Add("At least one step is required");
        var ops = operations.Where(o => o.Enabled).Select(o => o.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (s, i) in w.Steps.Select((s, i) => (s, i)))
        {
            if (string.IsNullOrWhiteSpace(s.Key)) errors.Add($"Step {i + 1}: key is required");
            else if (!keys.Add(s.Key)) errors.Add($"Step {i + 1}: duplicate key '{s.Key}'");
            if (string.IsNullOrWhiteSpace(s.Operation)) errors.Add($"Step {i + 1}: operation is required");
            else if (!ops.Contains(s.Operation)) errors.Add($"Step {i + 1}: unknown or disabled operation '{s.Operation}'");
            if (string.Equals(s.Operation, OperationType.Commission.ToString(), StringComparison.OrdinalIgnoreCase)) errors.Add($"Step {i + 1}: Commission cannot be a workflow step");
            foreach (var a in s.Ask) if (!AskInputs.Contains(a, StringComparer.OrdinalIgnoreCase)) errors.Add($"Step {i + 1}: unknown input '{a}' (use {string.Join(", ", AskInputs)})");
            if (s.OnRejected is not ("stop" or "continue")) errors.Add($"Step {i + 1}: onRejected must be 'stop' or 'continue'");
        }
        return errors;
    }
}

/// <summary>Read model of a workflow run: the operations that share a run id, in step order.</summary>
public record WorkflowRunSummary(string RunId, string? WorkflowCode, string? WorkflowName, DateTime StartedAt, DateTime LastActivityAt, int StepsDone, int StepsTotal, int Ok, int Rejected, int Unknown, Guid? UserId, Guid? DeviceId, string LastStep, bool Completed);

public class WorkflowService
{
    private readonly IAppDb _db;
    public WorkflowService(IAppDb db) => _db = db;

    public async Task<List<WorkflowDefinition>> ListAsync(bool includeDisabled = false, CancellationToken ct = default)
        => await _db.Workflows.Where(w => includeDisabled || w.Enabled).OrderBy(w => w.Name).ToListAsync(ct);

    public async Task<List<WorkflowRunSummary>> RunsAsync(int take = 100, string? workflowCode = null, CancellationToken ct = default)
    {
        var q = _db.Operations.Where(o => o.WorkflowRunId != null);
        if (!string.IsNullOrWhiteSpace(workflowCode)) q = q.Where(o => o.WorkflowCode == workflowCode);
        var runIds = await q.GroupBy(o => o.WorkflowRunId!).Select(g => new { RunId = g.Key, Last = g.Max(o => o.StartedAt) }).OrderByDescending(x => x.Last).Take(Math.Clamp(take, 1, 1000)).Select(x => x.RunId).ToListAsync(ct);
        var ops = await _db.Operations.Include(o => o.Lines).Where(o => runIds.Contains(o.WorkflowRunId!)).OrderBy(o => o.StartedAt).ToListAsync(ct);
        var defs = await _db.Workflows.ToDictionaryAsync(w => w.Code, ct);
        return ops.GroupBy(o => o.WorkflowRunId!).Select(g => Summarise(g.Key, g.ToList(), defs)).OrderByDescending(r => r.LastActivityAt).ToList();
    }

    public async Task<(WorkflowRunSummary summary, List<Operation> operations)?> RunAsync(string runId, CancellationToken ct = default)
    {
        var ops = await _db.Operations.Include(o => o.Lines).Where(o => o.WorkflowRunId == runId).OrderBy(o => o.StartedAt).ToListAsync(ct);
        if (ops.Count == 0) return null;
        var defs = await _db.Workflows.ToDictionaryAsync(w => w.Code, ct);
        return (Summarise(runId, ops, defs), ops);
    }

    private static WorkflowRunSummary Summarise(string runId, List<Operation> ops, Dictionary<string, WorkflowDefinition> defs)
    {
        var code = ops.Select(o => o.WorkflowCode).FirstOrDefault(c => c != null);
        var def = code != null ? defs.GetValueOrDefault(code) : null;
        var stepsDone = ops.Select(o => o.WorkflowStep).Where(s => s != null).Distinct().Count();
        var total = def?.Steps.Count ?? stepsDone;
        var last = ops[^1];
        var lastKey = last.WorkflowStep ?? "";
        var completed = def != null && def.Steps.Count > 0 && string.Equals(def.Steps[^1].Key, lastKey, StringComparison.OrdinalIgnoreCase) && last.Lines.Any(l => l.Result == LineResult.Ok);
        return new WorkflowRunSummary(runId, code, def?.Name, ops[0].StartedAt, last.StartedAt, stepsDone, total,
            ops.Sum(o => o.Lines.Count(l => l.Result == LineResult.Ok)), ops.Sum(o => o.Lines.Count(l => l.Result == LineResult.Rejected)), ops.Sum(o => o.Lines.Count(l => l.Result == LineResult.Unknown)),
            last.UserId, last.DeviceId, lastKey, completed);
    }
}
