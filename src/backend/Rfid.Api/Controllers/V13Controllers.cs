using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rfid.Api.Background;
using Rfid.Application.Services;
using Rfid.Infrastructure.Persistence;

namespace Rfid.Api.Controllers;

[ApiController, Route("api/import"), Authorize(Policy = "Admin")]
public class ImportController : ControllerBase
{
    private readonly ImportService _svc;
    public ImportController(ImportService svc) => _svc = svc;

    public record ImportRequest(string? Csv, string? Json, List<ImportRow>? Rows, bool DryRun = false, bool CreateMissingLocations = true, bool CreateMissingParties = true, bool UpdateExisting = true, string? DefaultType = null, string Source = "erp");

    private static List<ImportRow> Rows(ImportRequest r) => r.Rows is { Count: > 0 } ? r.Rows : r.Csv != null ? ImportService.ParseCsv(r.Csv) : r.Json != null ? ImportService.ParseJson(r.Json) : new();

    /// <summary>Upserts items from ERP/EAM master data (CSV text, JSON array or typed rows). dryRun previews the changes.</summary>
    [HttpPost("items")]
    public async Task<IActionResult> Items(ImportRequest r, CancellationToken ct)
        => Ok(await _svc.ImportAsync(Rows(r), new ImportOptions { DryRun = r.DryRun, CreateMissingLocations = r.CreateMissingLocations, CreateMissingParties = r.CreateMissingParties, UpdateExisting = r.UpdateExisting, DefaultType = r.DefaultType, Source = r.Source }, ct));

    /// <summary>Raw CSV body variant for scripted/nightly ERP extracts.</summary>
    [HttpPost("items/csv"), Consumes("text/csv", "text/plain")]
    public async Task<IActionResult> ItemsCsv(bool dryRun = false, string? defaultType = null, string source = "erp", CancellationToken ct = default)
    {
        using var reader = new StreamReader(Request.Body);
        var rows = ImportService.ParseCsv(await reader.ReadToEndAsync(ct));
        return Ok(await _svc.ImportAsync(rows, new ImportOptions { DryRun = dryRun, DefaultType = defaultType, Source = source }, ct));
    }

    /// <summary>Compares an external asset list with the platform: missing on either side and field differences. Writes nothing.</summary>
    [HttpPost("reconcile")]
    public async Task<IActionResult> Reconcile(ImportRequest r, CancellationToken ct)
    {
        var res = await _svc.ReconcileAsync(Rows(r), ct);
        return Ok(new { res.Matched, res.MissingInPlatform, res.MissingInErp, differences = res.Differences.Select(d => new { d.identifier, d.field, erp = d.erp, platform = d.platform }) });
    }
}

[ApiController, Route("api/devices/{id:guid}/llrp"), Authorize(Policy = "Operator")]
public class LlrpController : ControllerBase
{
    private readonly LlrpReaderService _llrp; private readonly AppDbContext _db;
    public LlrpController(LlrpReaderService llrp, AppDbContext db) { _llrp = llrp; _db = db; }

    /// <summary>Connection state, capabilities (manufacturer, firmware, antennas, GPIO, power table) and active options for an LLRP reader.</summary>
    [HttpGet("status")]
    public async Task<IActionResult> Status(Guid id)
    {
        var d = await _db.Devices.FindAsync(id); if (d == null) return NotFound();
        var st = _llrp.Status(id);
        var ep = LlrpReaderService.Endpoint(d);
        return Ok(new { device = d.Name, endpoint = ep == null ? null : new { ep.Value.host, ep.Value.port }, options = LlrpReaderService.OptionsFor(d), connected = st?.Connected ?? false, st?.ConnectedAt, st?.TagsReceived,
            capabilities = st?.Capabilities == null ? null : new { st.Capabilities.Manufacturer, st.Capabilities.ModelId, st.Capabilities.Firmware, st.Capabilities.MaxAntennas, st.Capabilities.Gpis, st.Capabilities.Gpos, st.Capabilities.HasUtcClock, powerTable = st.Capabilities.PowerTable.Select(p => new { p.index, p.dbm }) } });
    }

    public record GpoRequest(int Port, bool State);
    /// <summary>Drives a GPO on the reader (stack light, buzzer, gate).</summary>
    [HttpPost("gpo")]
    public async Task<IActionResult> Gpo(Guid id, GpoRequest r, CancellationToken ct)
    {
        try { await _llrp.SetGpoAsync(id, r.Port, r.State, ct); return Ok(new { r.Port, r.State }); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    /// <summary>Reconnects with the device's current configuration (after changing power/session/antennas/GPI settings).</summary>
    [HttpPost("reconnect")]
    public async Task<IActionResult> Reconnect(Guid id) { await _llrp.ReconnectAsync(id); return Ok(new { reconnecting = true }); }
}
