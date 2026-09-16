using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rfid.Application.Contracts;
using Rfid.Application.Operations;
using Rfid.Domain.Entities;
using Rfid.Infrastructure.Persistence;

namespace Rfid.Api.Controllers;

/// <summary>Guided workflows: definitions (template/tenant configuration) and run history derived from operations.</summary>
[ApiController, Route("api/workflows"), Authorize]
public class WorkflowsController : ControllerBase
{
    private readonly AppDbContext _db; private readonly ICurrentContext _ctx; private readonly WorkflowService _svc; private readonly OperationDefinitions _ops;
    public WorkflowsController(AppDbContext db, ICurrentContext ctx, WorkflowService svc, OperationDefinitions ops) { _db = db; _ctx = ctx; _svc = svc; _ops = ops; }

    [HttpGet]
    public async Task<IActionResult> List(bool includeDisabled = false, CancellationToken ct = default) => Ok(await _svc.ListAsync(includeDisabled, ct));

    [HttpGet("inputs")]
    public IActionResult Inputs() => Ok(WorkflowCatalog.AskInputs);

    [HttpPost, Authorize(Policy = "Admin")]
    public async Task<IActionResult> Create(WorkflowDefinition w, CancellationToken ct)
    {
        var errors = WorkflowCatalog.Validate(w, await _ops.ListAsync(ct));
        if (errors.Count > 0) return BadRequest(new { errors });
        if (await _db.Workflows.AnyAsync(x => x.Code == w.Code, ct)) return Conflict(new { error = $"Workflow '{w.Code}' already exists" });
        var e = new WorkflowDefinition { TenantId = _ctx.TenantId, Code = w.Code, Name = w.Name, Description = w.Description, Enabled = w.Enabled, Vertical = w.Vertical, Icon = w.Icon, ItemTypeCodes = w.ItemTypeCodes, Steps = w.Steps };
        _db.Workflows.Add(e); await _db.SaveChangesAsync(ct);
        return Created($"/api/workflows/{e.Id}", e);
    }

    [HttpPut("{id:guid}"), Authorize(Policy = "Admin")]
    public async Task<IActionResult> Update(Guid id, WorkflowDefinition w, CancellationToken ct)
    {
        var e = await _db.Workflows.FirstOrDefaultAsync(x => x.Id == id, ct); if (e == null) return NotFound();
        w.Code = e.Code;
        var errors = WorkflowCatalog.Validate(w, await _ops.ListAsync(ct));
        if (errors.Count > 0) return BadRequest(new { errors });
        e.Name = w.Name; e.Description = w.Description; e.Enabled = w.Enabled; e.Icon = w.Icon; e.ItemTypeCodes = w.ItemTypeCodes; e.Steps = w.Steps;
        await _db.SaveChangesAsync(ct); return Ok(e);
    }

    [HttpDelete("{id:guid}"), Authorize(Policy = "Admin")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var e = await _db.Workflows.FirstOrDefaultAsync(x => x.Id == id, ct); if (e == null) return NotFound();
        if (await _db.Operations.AnyAsync(o => o.WorkflowCode == e.Code, ct)) { e.Enabled = false; await _db.SaveChangesAsync(ct); return Ok(new { disabled = true, reason = "Runs reference this workflow; it was disabled instead of deleted" }); }
        _db.Workflows.Remove(e); await _db.SaveChangesAsync(ct); return NoContent();
    }

    /// <summary>Recent runs (operations grouped by run id), newest first.</summary>
    [HttpGet("runs")]
    public async Task<IActionResult> Runs(int take = 100, string? workflow = null, CancellationToken ct = default) => Ok(await _svc.RunsAsync(take, workflow, ct));

    [HttpGet("runs/{runId}")]
    public async Task<IActionResult> Run(string runId, CancellationToken ct)
    {
        var r = await _svc.RunAsync(runId, ct); if (r == null) return NotFound();
        var (summary, ops) = r.Value;
        var itemIds = ops.SelectMany(o => o.Lines).Where(l => l.ItemId.HasValue).Select(l => l.ItemId!.Value).Distinct().ToList();
        var items = await _db.Items.Where(i => itemIds.Contains(i.Id)).Select(i => new { i.Id, i.Name, i.Identifier }).ToDictionaryAsync(i => i.Id, ct);
        return Ok(new
        {
            summary,
            steps = ops.Select(o => new { o.Id, o.WorkflowStep, o.DefinitionCode, o.StartedAt, o.ToLocationId, o.PartyId, o.TargetState, o.UserId, o.DeviceId,
                lines = o.Lines.Select(l => new { l.Epc, l.ItemId, Item = l.ItemId.HasValue ? items.GetValueOrDefault(l.ItemId.Value)?.Name : null, l.Result, l.Message }) }),
        });
    }
}

/// <summary>Edge agents pull their desired reader configuration here with their gateway device token.</summary>
[ApiController, Route("api/edge"), Authorize]
public class EdgeController : ControllerBase
{
    private readonly Rfid.Application.Devices.EdgeConfigService _svc; private readonly ICurrentContext _ctx; private readonly AppDbContext _db;
    public EdgeController(Rfid.Application.Devices.EdgeConfigService svc, ICurrentContext ctx, AppDbContext db) { _svc = svc; _ctx = ctx; _db = db; }

    /// <summary>The readers assigned to the calling gateway (device token), with LLRP options and a content revision.</summary>
    [HttpGet("config")]
    public async Task<IActionResult> Config(Guid? gatewayId, CancellationToken ct)
    {
        var id = _ctx.DeviceId ?? gatewayId;
        if (id == null) return BadRequest(new { error = "Use a gateway device token, or pass gatewayId as an administrator" });
        if (_ctx.DeviceId == null && !User.IsInRole("Admin") && !User.HasClaim("role", "Admin")) return Forbid();
        if (!await _db.Devices.AnyAsync(d => d.Id == id && d.Kind == Rfid.Domain.DeviceKind.Gateway, ct)) return NotFound(new { error = "Gateway device not found" });
        return Ok(await _svc.ForGatewayAsync(id.Value, ct));
    }
}
