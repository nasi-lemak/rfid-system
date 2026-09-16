using Microsoft.EntityFrameworkCore;
using Rfid.Application.Contracts;
using Rfid.Domain;
using Rfid.Domain.Entities;

namespace Rfid.Application.Templates;

public class ProvisionResult
{
    public string Template { get; set; } = "";
    public int ItemTypesCreated { get; set; }
    public int RulesCreated { get; set; }
    public int OperationsCreated { get; set; }
    public int WorkflowsCreated { get; set; }
    public int Skipped { get; set; }
}

/// <summary>Copies a solution template's item types, lifecycles and rules into the current tenant (idempotent by code/name).</summary>
public class TemplateProvisioner
{
    private readonly IAppDb _db;
    private readonly ICurrentContext _ctx;
    public TemplateProvisioner(IAppDb db, ICurrentContext ctx) { _db = db; _ctx = ctx; }

    public async Task<ProvisionResult> ApplyAsync(string templateCode, CancellationToken ct = default)
    {
        var tpl = await _db.SolutionTemplates.FirstOrDefaultAsync(t => t.Code == templateCode, ct)
                  ?? SolutionTemplateCatalog.All.FirstOrDefault(t => t.Code == templateCode)
                  ?? throw new NotFoundException($"Template {templateCode}");
        return await ApplyDefinitionAsync(tpl.Code, tpl.Vertical, tpl.Definition, ct);
    }

    /// <summary>Exports the tenant's current item types and rules as a template definition (for backup, copying to another tenant, or contributing a new vertical).</summary>
    /// <summary>
    /// Transition path for tenants that installed a template before it carried operation definitions (or rules):
    /// re-applies every template whose item types are all present. Idempotent – existing types/rules/operations are skipped.
    /// </summary>
    public async Task<List<ProvisionResult>> SyncInstalledAsync(CancellationToken ct = default)
    {
        var results = new List<ProvisionResult>();
        var codes = (await _db.ItemTypes.Select(t => t.Code).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (codes.Count == 0) return results;
        foreach (var tpl in await _db.SolutionTemplates.ToListAsync(ct))
        {
            if (tpl.Definition.ItemTypes.Count == 0 || !tpl.Definition.ItemTypes.All(t => codes.Contains(t.Code))) continue;
            var r = await ApplyDefinitionAsync(tpl.Code, tpl.Vertical, tpl.Definition, ct);
            if (r.ItemTypesCreated + r.RulesCreated + r.OperationsCreated + r.WorkflowsCreated > 0) results.Add(r);
        }
        return results;
    }

    public async Task<TemplateDefinition> ExportAsync(CancellationToken ct = default)
    {
        var types = await _db.ItemTypes.OrderBy(t => t.Name).ToListAsync(ct);
        var rules = await _db.Rules.OrderBy(r => r.Name).ToListAsync(ct);
        return new TemplateDefinition
        {
            ItemTypes = types.Select(t => new ItemType { Name = t.Name, Code = t.Code, Category = t.Category, IsContainer = t.IsContainer, TracksExpiry = t.TracksExpiry, TracksCycles = t.TracksCycles, MaxCycles = t.MaxCycles, RequiresInspection = t.RequiresInspection, InspectionIntervalDays = t.InspectionIntervalDays, ReorderPoint = t.ReorderPoint, Unit = t.Unit, AttributeSchema = t.AttributeSchema, Lifecycle = t.Lifecycle, UsefulLifeMonths = t.UsefulLifeMonths, LabelTemplate = t.LabelTemplate, Vertical = t.Vertical }).ToList(),
            Rules = rules.Select(r => new Rule { Name = r.Name, Enabled = r.Enabled, Trigger = r.Trigger, Conditions = r.Conditions, Action = r.Action, Params = r.Params, Severity = r.Severity }).ToList(),
            Operations = Enum.GetValues<OperationType>().ToList(),
            OperationDefinitions = (await _db.OperationDefinitions.Where(d => !d.IsBuiltIn).OrderBy(d => d.Code).ToListAsync(ct))
                .Select(d => new OperationDefinition { Code = d.Code, Name = d.Name, Description = d.Description, BaseType = d.BaseType, EventType = d.EventType, Effects = d.Effects, Requires = d.Requires, EventData = d.EventData, ItemTypeCodes = d.ItemTypeCodes, Enabled = d.Enabled, Icon = d.Icon }).ToList(),
            Workflows = (await _db.Workflows.OrderBy(w => w.Code).ToListAsync(ct))
                .Select(w => new WorkflowDefinition { Code = w.Code, Name = w.Name, Description = w.Description, Enabled = w.Enabled, Icon = w.Icon, ItemTypeCodes = w.ItemTypeCodes, Steps = w.Steps.Select(s => new WorkflowStep { Key = s.Key, Title = s.Title, Prompt = s.Prompt, Operation = s.Operation, Ask = s.Ask, Rescan = s.Rescan, Optional = s.Optional, OnRejected = s.OnRejected }).ToList() }).ToList(),
        };
    }

    public async Task<ProvisionResult> ApplyDefinitionAsync(string code, string vertical, TemplateDefinition definition, CancellationToken ct = default)
    {
        var tpl = new SolutionTemplate { Code = code, Vertical = vertical, Definition = definition };
        var result = new ProvisionResult { Template = tpl.Code };
        var existingTypes = await _db.ItemTypes.Select(t => t.Code).ToListAsync(ct);
        foreach (var it in tpl.Definition.ItemTypes)
        {
            if (existingTypes.Contains(it.Code, StringComparer.OrdinalIgnoreCase)) { result.Skipped++; continue; }
            _db.ItemTypes.Add(new ItemType
            {
                TenantId = _ctx.TenantId, Name = it.Name, Code = it.Code, Category = it.Category, IsContainer = it.IsContainer,
                TracksExpiry = it.TracksExpiry, TracksCycles = it.TracksCycles, MaxCycles = it.MaxCycles,
                RequiresInspection = it.RequiresInspection, InspectionIntervalDays = it.InspectionIntervalDays,
                ReorderPoint = it.ReorderPoint, Unit = it.Unit, AttributeSchema = it.AttributeSchema, Lifecycle = it.Lifecycle,
                UsefulLifeMonths = it.UsefulLifeMonths, LabelTemplate = it.LabelTemplate, Vertical = tpl.Vertical,
            });
            result.ItemTypesCreated++;
        }
        var existingRules = await _db.Rules.Select(r => r.Name).ToListAsync(ct);
        // A template's rules apply to the template's own item types unless the rule already targets a type
        // explicitly – so several verticals can share one tenant without cross-firing (e.g. retail
        // loss-prevention must not alert on a hotel bathrobe passing the service exit).
        var typeCodes = string.Join(",", tpl.Definition.ItemTypes.Select(t => t.Code));
        foreach (var r in tpl.Definition.Rules)
        {
            if (existingRules.Contains(r.Name, StringComparer.OrdinalIgnoreCase)) { result.Skipped++; continue; }
            var conditions = r.Conditions.Select(c => new RuleCondition { Field = c.Field, Op = c.Op, Value = c.Value }).ToList();
            if (typeCodes.Length > 0 && !conditions.Any(c => c.Field.StartsWith("itemType.", StringComparison.OrdinalIgnoreCase)))
                conditions.Insert(0, new RuleCondition { Field = "itemType.code", Op = "in", Value = typeCodes });
            _db.Rules.Add(new Rule
            {
                TenantId = _ctx.TenantId, Name = r.Name, Enabled = r.Enabled, Trigger = r.Trigger, Conditions = conditions,
                Action = r.Action, Params = r.Params, Severity = r.Severity, Vertical = tpl.Vertical,
            });
            result.RulesCreated++;
        }
        // Vertical operations are configuration, not code: each one is a named composition of built-in effects.
        var existingOps = await _db.OperationDefinitions.Select(d => d.Code).ToListAsync(ct);
        foreach (var d in tpl.Definition.OperationDefinitions)
        {
            if (existingOps.Contains(d.Code, StringComparer.OrdinalIgnoreCase) || Operations.OperationCatalog.Get(d.Code) != null) { result.Skipped++; continue; }
            var errors = Operations.OperationCatalog.Validate(d);
            if (errors.Count > 0) throw new DomainException($"Template operation '{d.Code}' is invalid: {string.Join("; ", errors)}");
            _db.OperationDefinitions.Add(new OperationDefinition
            {
                TenantId = _ctx.TenantId, Code = d.Code, Name = d.Name, Description = d.Description, BaseType = d.BaseType, EventType = d.EventType,
                Effects = d.Effects, Requires = d.Requires, EventData = d.EventData, Enabled = d.Enabled, Icon = d.Icon, Vertical = tpl.Vertical,
                ItemTypeCodes = d.ItemTypeCodes.Count > 0 ? d.ItemTypeCodes : tpl.Definition.ItemTypes.Select(t => t.Code).ToList(),
            });
            result.OperationsCreated++;
        }
        // Guided workflows: ordered operation definitions with prompts. Validated against the template's and the tenant's operations.
        if (tpl.Definition.Workflows.Count > 0)
        {
            var existingWf = await _db.Workflows.Select(w => w.Code).ToListAsync(ct);
            var knownOps = Operations.OperationCatalog.BuiltIn.Concat(tpl.Definition.OperationDefinitions).Concat(await _db.OperationDefinitions.ToListAsync(ct)).Concat(_db.OperationDefinitions.Local).ToList();
            foreach (var w in tpl.Definition.Workflows)
            {
                if (existingWf.Contains(w.Code, StringComparer.OrdinalIgnoreCase)) { result.Skipped++; continue; }
                var errors = Operations.WorkflowCatalog.Validate(w, knownOps);
                if (errors.Count > 0) throw new DomainException($"Template workflow '{w.Code}' is invalid: {string.Join("; ", errors)}");
                _db.Workflows.Add(new WorkflowDefinition
                {
                    TenantId = _ctx.TenantId, Code = w.Code, Name = w.Name, Description = w.Description, Enabled = w.Enabled, Icon = w.Icon, Vertical = tpl.Vertical,
                    ItemTypeCodes = w.ItemTypeCodes.Count > 0 ? w.ItemTypeCodes : tpl.Definition.ItemTypes.Select(t => t.Code).ToList(),
                    Steps = w.Steps.Select(s => new WorkflowStep { Key = s.Key, Title = s.Title, Prompt = s.Prompt, Operation = s.Operation, Ask = s.Ask, Fixed = new(), Rescan = s.Rescan, Optional = s.Optional, OnRejected = s.OnRejected }).ToList(),
                });
                result.WorkflowsCreated++;
            }
        }
        await _db.SaveChangesAsync(ct);
        return result;
    }
}
