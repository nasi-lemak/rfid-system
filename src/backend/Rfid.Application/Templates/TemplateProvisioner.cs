using Microsoft.EntityFrameworkCore;
using Rfid.Application.Contracts;
using Rfid.Domain.Entities;

namespace Rfid.Application.Templates;

public class ProvisionResult
{
    public string Template { get; set; } = "";
    public int ItemTypesCreated { get; set; }
    public int RulesCreated { get; set; }
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
                Vertical = tpl.Vertical,
            });
            result.ItemTypesCreated++;
        }
        var existingRules = await _db.Rules.Select(r => r.Name).ToListAsync(ct);
        foreach (var r in tpl.Definition.Rules)
        {
            if (existingRules.Contains(r.Name, StringComparer.OrdinalIgnoreCase)) { result.Skipped++; continue; }
            _db.Rules.Add(new Rule
            {
                TenantId = _ctx.TenantId, Name = r.Name, Enabled = r.Enabled, Trigger = r.Trigger, Conditions = r.Conditions,
                Action = r.Action, Params = r.Params, Severity = r.Severity, Vertical = tpl.Vertical,
            });
            result.RulesCreated++;
        }
        await _db.SaveChangesAsync(ct);
        return result;
    }
}
