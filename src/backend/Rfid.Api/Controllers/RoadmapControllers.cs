using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rfid.Api.Background;
using Rfid.Application.Contracts;
using Rfid.Application.Services;
using Rfid.Application.Templates;
using Rfid.Domain;
using Rfid.Domain.Entities;
using Rfid.Domain.Epc;
using Rfid.Infrastructure.Persistence;

namespace Rfid.Api.Controllers;

[ApiController, Route("api/presence"), Authorize]
public class PresenceController : ControllerBase
{
    private readonly PresenceService _svc; private readonly AppDbContext _db;
    public PresenceController(PresenceService svc, AppDbContext db) { _svc = svc; _db = db; }

    /// <summary>Live zone occupancy (open presence sessions), optionally restricted to a location subtree.</summary>
    [HttpGet("zones")] public async Task<IActionResult> Zones(Guid? under, CancellationToken ct) => Ok(await _svc.OccupancyAsync(under, ct));

    /// <summary>Emergency muster roll-call for a site: accounted (at a muster point) vs unaccounted.</summary>
    [HttpGet("muster")] public async Task<IActionResult> Muster(Guid siteId, CancellationToken ct) => Ok(await _svc.MusterAsync(siteId, ct));

    /// <summary>Checkpoint timing (race / process stage) under an event location.</summary>
    [HttpGet("timing")]
    public async Task<IActionResult> Timing(Guid eventLocationId, CancellationToken ct) { var (cps, rows) = await _svc.TimingAsync(eventLocationId, ct); return Ok(new { checkpoints = cps, rows }); }

    [HttpGet("items/{itemId:guid}")]
    public async Task<IActionResult> History(Guid itemId, int take = 50) => Ok(await _db.PresenceSessions.Where(p => p.ItemId == itemId).OrderByDescending(p => p.EnteredAt).Take(take).ToListAsync());

    /// <summary>Force a sweep (normally run by the background service every 30 s).</summary>
    [HttpPost("sweep"), Authorize(Policy = "Operator")] public async Task<IActionResult> Sweep(CancellationToken ct) => Ok(new { closed = await _svc.SweepAsync(DateTime.UtcNow, ct) });
}

[ApiController, Route("api/reports"), Authorize]
public class ReportsController : ControllerBase
{
    private readonly ReportService _svc;
    public ReportsController(ReportService svc) => _svc = svc;

    [HttpGet] public IActionResult Catalog() => Ok(ReportService.Catalog);

    /// <summary>Runs a report. format=json (default) or csv. Other query parameters are passed to the report (days, from, to, type).</summary>
    [HttpGet("{code}")]
    public async Task<IActionResult> Run(string code, string format = "json", CancellationToken ct = default)
    {
        var p = Request.Query.ToDictionary(k => k.Key, k => (string?)k.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        var (columns, rows) = await _svc.RunAsync(code, p, ct);
        if (format.Equals("csv", StringComparison.OrdinalIgnoreCase))
            return File(System.Text.Encoding.UTF8.GetBytes(ReportService.ToCsv(columns, rows)), "text/csv", $"{code}-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv");
        return Ok(new { columns, rows, count = rows.Count });
    }
}

[ApiController, Route("api/integrations"), Authorize(Policy = "Admin")]
public class IntegrationsController : ControllerBase
{
    private readonly AppDbContext _db; private readonly ICurrentContext _ctx; private readonly IntegrationService _svc;
    public IntegrationsController(AppDbContext db, ICurrentContext ctx, IntegrationService svc) { _db = db; _ctx = ctx; _svc = svc; }

    private static object Map(IntegrationEndpoint e) => new { e.Id, e.Name, e.Url, HasSecret = !string.IsNullOrEmpty(e.Secret), e.Enabled, e.EventTypes, e.IncludeAlerts, e.Headers, e.BatchSize, e.EventCursor, e.AlertCursor, e.LastDeliveryAt, e.LastError, e.FailureCount, e.NextAttemptAt, e.DeliveredCount, e.CreatedAt, e.Format, e.AuthType, e.Username, e.TokenUrl, e.ClientId, e.Scope, e.Mapping, HasCredentials = !string.IsNullOrEmpty(e.ApiToken) || !string.IsNullOrEmpty(e.Password) || !string.IsNullOrEmpty(e.ClientSecret) };

    [HttpGet] public async Task<IActionResult> List() => Ok((await _db.IntegrationEndpoints.OrderBy(e => e.Name).ToListAsync()).Select(Map));

    public record Write(string Name, string Url, string? Secret, bool Enabled, List<ItemEventType>? EventTypes, bool IncludeAlerts, Dictionary<string, object?>? Headers, int? BatchSize, DateTime? EventCursor,
        IntegrationFormat? Format = null, IntegrationAuth? AuthType = null, string? ApiToken = null, string? Username = null, string? Password = null, string? TokenUrl = null, string? ClientId = null, string? ClientSecret = null, string? Scope = null, Dictionary<string, object?>? Mapping = null);

    private static void ApplyVendor(IntegrationEndpoint e, Write w)
    {
        if (w.Format.HasValue) e.Format = w.Format.Value; if (w.AuthType.HasValue) e.AuthType = w.AuthType.Value;
        if (w.ApiToken != null) e.ApiToken = w.ApiToken == "" ? null : w.ApiToken; if (w.Username != null) e.Username = w.Username; if (w.Password != null) e.Password = w.Password == "" ? null : w.Password;
        if (w.TokenUrl != null) e.TokenUrl = w.TokenUrl; if (w.ClientId != null) e.ClientId = w.ClientId; if (w.ClientSecret != null) e.ClientSecret = w.ClientSecret == "" ? null : w.ClientSecret; if (w.Scope != null) e.Scope = w.Scope;
        if (w.Mapping != null) e.Mapping = w.Mapping;
    }

    [HttpPost]
    public async Task<IActionResult> Create(Write w)
    {
        var e = new IntegrationEndpoint { TenantId = _ctx.TenantId, Name = w.Name, Url = w.Url, Secret = w.Secret, Enabled = w.Enabled, EventTypes = w.EventTypes ?? new(), IncludeAlerts = w.IncludeAlerts, Headers = w.Headers ?? new(), BatchSize = w.BatchSize ?? 100, EventCursor = w.EventCursor ?? DateTime.UtcNow, AlertCursor = w.EventCursor ?? DateTime.UtcNow };
        ApplyVendor(e, w);
        _db.IntegrationEndpoints.Add(e); await _db.SaveChangesAsync(); return Ok(Map(e));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, Write w)
    {
        var e = await _db.IntegrationEndpoints.FindAsync(id); if (e == null) return NotFound();
        e.Name = w.Name; e.Url = w.Url; if (w.Secret != null) e.Secret = w.Secret == "" ? null : w.Secret; e.Enabled = w.Enabled; e.EventTypes = w.EventTypes ?? new(); e.IncludeAlerts = w.IncludeAlerts; if (w.Headers != null) e.Headers = w.Headers; if (w.BatchSize.HasValue) e.BatchSize = w.BatchSize.Value;
        if (w.EventCursor.HasValue) { e.EventCursor = w.EventCursor.Value; e.AlertCursor = w.EventCursor.Value; }
        ApplyVendor(e, w);
        e.NextAttemptAt = null; e.FailureCount = 0;
        await _db.SaveChangesAsync(); return Ok(Map(e));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id) { var e = await _db.IntegrationEndpoints.FindAsync(id); if (e == null) return NotFound(); _db.IntegrationEndpoints.Remove(e); await _db.SaveChangesAsync(); return NoContent(); }

    /// <summary>Deliver the next batch now (bypasses the retry back-off).</summary>
    [HttpPost("{id:guid}/deliver")]
    public async Task<IActionResult> Deliver(Guid id, CancellationToken ct)
    {
        var e = await _db.IntegrationEndpoints.FindAsync(id); if (e == null) return NotFound();
        var n = await _svc.DispatchAsync(e, DateTime.UtcNow, ct);
        return Ok(new { delivered = n, e.LastError, e.FailureCount, e.NextAttemptAt, e.EventCursor });
    }

    /// <summary>Preview of what the next delivery would contain (counts only).</summary>
    [HttpGet("{id:guid}/pending")]
    public async Task<IActionResult> Pending(Guid id)
    {
        var e = await _db.IntegrationEndpoints.FindAsync(id); if (e == null) return NotFound();
        var q = _db.ItemEvents.Where(x => x.OccurredAt > e.EventCursor); if (e.EventTypes.Count > 0) q = q.Where(x => e.EventTypes.Contains(x.Type));
        return Ok(new { events = await q.CountAsync(), alerts = e.IncludeAlerts ? await _db.Alerts.CountAsync(a => a.RaisedAt > e.AlertCursor) : 0 });
    }
}

[ApiController, Route("api/labels"), Authorize(Policy = "Operator")]
public class LabelsController : ControllerBase
{
    private readonly AppDbContext _db; private readonly IPrinterClient _printer;
    public LabelsController(AppDbContext db, IPrinterClient printer) { _db = db; _printer = printer; }

    private Task<Item?> Load(Guid id) => _db.Items.Include(i => i.ItemType).Include(i => i.CurrentLocation).Include(i => i.Tags).FirstOrDefaultAsync(i => i.Id == id);

    /// <summary>Renders the ZPL for an item's label (uses the item type's LabelTemplate or the default 4x2" RFID label).</summary>
    [HttpGet("items/{id:guid}")]
    public async Task<IActionResult> Render(Guid id, string? epc, string? template)
    {
        var item = await Load(id); if (item == null) return NotFound();
        var e = epc ?? item.Tags.FirstOrDefault(t => t.Status == TagStatus.Active)?.Epc ?? item.Tags.FirstOrDefault()?.Epc;
        if (string.IsNullOrEmpty(e)) return BadRequest(new { error = "Item has no tag; pass ?epc= to encode a new one" });
        return Content(LabelService.Render(item, e, template), "text/plain");
    }

    public record PrintRequest(List<Guid> ItemIds, Guid PrinterDeviceId, string? Template);

    /// <summary>Prints (and RFID-encodes) labels for the given items on a printer device (Config: host, port=9100).</summary>
    [HttpPost("print")]
    public async Task<IActionResult> Print(PrintRequest req, CancellationToken ct)
    {
        var printer = await _db.Devices.FindAsync(req.PrinterDeviceId);
        if (printer == null || printer.Kind != DeviceKind.Printer) return BadRequest(new { error = "Printer device not found" });
        var host = printer.Config.GetValueOrDefault("host")?.ToString(); var port = int.TryParse(printer.Config.GetValueOrDefault("port")?.ToString(), out var p) ? p : 9100;
        if (string.IsNullOrEmpty(host)) return BadRequest(new { error = "Printer device has no 'host' in its config" });
        var results = new List<object>();
        foreach (var id in req.ItemIds)
        {
            var item = await Load(id);
            var epc = item?.Tags.FirstOrDefault(t => t.Status == TagStatus.Active)?.Epc;
            if (item == null || epc == null) { results.Add(new { itemId = id, ok = false, error = "Item or tag not found" }); continue; }
            try { await _printer.SendAsync(host, port, LabelService.Render(item, epc, req.Template), ct); results.Add(new { itemId = id, ok = true, epc }); printer.LastSeenAt = DateTime.UtcNow; }
            catch (Exception ex) { results.Add(new { itemId = id, ok = false, error = ex.Message }); }
        }
        await _db.SaveChangesAsync(ct);
        return Ok(results);
    }
}

[ApiController, Route("api/stocktake-schedules"), Authorize]
public class StocktakeSchedulesController : ControllerBase
{
    private readonly AppDbContext _db; private readonly ICurrentContext _ctx; private readonly StocktakeScheduleService _svc;
    public StocktakeSchedulesController(AppDbContext db, ICurrentContext ctx, StocktakeScheduleService svc) { _db = db; _ctx = ctx; _svc = svc; }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var rows = await _db.StocktakeSchedules.OrderBy(s => s.NextRunAt).ToListAsync();
        var locs = await _db.Locations.ToDictionaryAsync(l => l.Id, l => l.Name);
        return Ok(rows.Select(s => new { s.Id, s.Name, s.LocationId, Location = locs.GetValueOrDefault(s.LocationId), s.ItemTypeId, s.IntervalDays, TimeOfDay = s.TimeOfDay.ToString(@"hh\:mm"), s.NextRunAt, s.LastRunAt, s.LastStocktakeId, s.Enabled, s.AutoReconcileHours }));
    }

    public record Write(string Name, Guid LocationId, Guid? ItemTypeId, int IntervalDays, string TimeOfDay, bool Enabled, int? AutoReconcileHours);

    [HttpPost, Authorize(Policy = "Operator")]
    public async Task<IActionResult> Create(Write w)
    {
        var tod = TimeSpan.TryParse(w.TimeOfDay, out var t) ? t : TimeSpan.FromHours(6);
        var s = new StocktakeSchedule { TenantId = _ctx.TenantId, Name = w.Name, LocationId = w.LocationId, ItemTypeId = w.ItemTypeId, IntervalDays = Math.Max(1, w.IntervalDays), TimeOfDay = tod, Enabled = w.Enabled, AutoReconcileHours = w.AutoReconcileHours, NextRunAt = StocktakeScheduleService.ComputeNextRun(DateTime.UtcNow, Math.Max(1, w.IntervalDays), tod) };
        _db.StocktakeSchedules.Add(s); await _db.SaveChangesAsync(); return Ok(s);
    }

    [HttpPut("{id:guid}"), Authorize(Policy = "Operator")]
    public async Task<IActionResult> Update(Guid id, Write w)
    {
        var s = await _db.StocktakeSchedules.FindAsync(id); if (s == null) return NotFound();
        var tod = TimeSpan.TryParse(w.TimeOfDay, out var t) ? t : s.TimeOfDay;
        s.Name = w.Name; s.LocationId = w.LocationId; s.ItemTypeId = w.ItemTypeId; s.IntervalDays = Math.Max(1, w.IntervalDays); s.TimeOfDay = tod; s.Enabled = w.Enabled; s.AutoReconcileHours = w.AutoReconcileHours;
        s.NextRunAt = StocktakeScheduleService.ComputeNextRun(DateTime.UtcNow, s.IntervalDays, tod);
        await _db.SaveChangesAsync(); return Ok(s);
    }

    [HttpDelete("{id:guid}"), Authorize(Policy = "Operator")]
    public async Task<IActionResult> Delete(Guid id) { var s = await _db.StocktakeSchedules.FindAsync(id); if (s == null) return NotFound(); _db.StocktakeSchedules.Remove(s); await _db.SaveChangesAsync(); return NoContent(); }

    /// <summary>Run a schedule immediately (opens the stocktake now and advances NextRunAt).</summary>
    [HttpPost("{id:guid}/run"), Authorize(Policy = "Operator")]
    public async Task<IActionResult> RunNow(Guid id, CancellationToken ct)
    {
        var s = await _db.StocktakeSchedules.FindAsync(id); if (s == null) return NotFound();
        s.NextRunAt = DateTime.UtcNow.AddSeconds(-1); await _db.SaveChangesAsync(ct);
        return Ok(new { created = await _svc.RunDueAsync(DateTime.UtcNow, ct), s.LastStocktakeId });
    }
}

/// <summary>Vendor-format ingestion endpoints (device JWT) – no edge software needed for readers that can POST JSON.</summary>
[ApiController, Route("api/ingest"), Authorize(Policy = "Operator")]
public class VendorIngestController : ControllerBase
{
    private readonly ReadIngestionService _svc; private readonly ICurrentContext _ctx;
    public VendorIngestController(ReadIngestionService svc, ICurrentContext ctx) { _svc = svc; _ctx = ctx; }

    /// <summary>Impinj IoT Interface (R700) webhook / stream payloads ("tagInventoryEvent").</summary>
    [HttpPost("impinj")] public Task<IActionResult> Impinj([FromBody] JsonElement body, Guid? deviceId, CancellationToken ct) => Run(body, "impinj", deviceId, ct);
    /// <summary>Zebra IoT Connector (FX7500/FX9600) event payloads ("data.idHex").</summary>
    [HttpPost("zebra")] public Task<IActionResult> Zebra([FromBody] JsonElement body, Guid? deviceId, CancellationToken ct) => Run(body, "zebra", deviceId, ct);
    /// <summary>Any JSON with an epc-like field, an array of reads, or {"reads":[...]}.</summary>
    [HttpPost("generic")] public Task<IActionResult> Generic([FromBody] JsonElement body, Guid? deviceId, CancellationToken ct) => Run(body, null, deviceId, ct);

    private async Task<IActionResult> Run(JsonElement body, string? vendor, Guid? deviceId, CancellationToken ct)
    {
        var batch = IngestAdapters.Parse(body, vendor);
        batch.DeviceId = deviceId ?? batch.DeviceId ?? _ctx.DeviceId;
        return Ok(await _svc.IngestAsync(batch, ReadSource.Fixed, ct));
    }
}

[ApiController, Route("api/tags"), Authorize]
public class EncodingController : ControllerBase
{
    public record EncodeRequest(string Scheme, string CompanyPrefix, string? Reference, ulong Serial, int Filter = 0);

    /// <summary>Encodes a GS1 EPC: scheme = SGTIN-96 (reference = item ref), GRAI-96 (reference = asset type), GIAI-96 (serial = asset reference).</summary>
    [HttpPost("encode"), Authorize(Policy = "Operator")]
    public IActionResult Encode(EncodeRequest r)
    {
        try
        {
            var epc = r.Scheme.ToUpperInvariant() switch
            {
                "SGTIN-96" or "SGTIN" => Sgtin96.Encode(r.CompanyPrefix, r.Reference ?? "", r.Serial, r.Filter),
                "GRAI-96" or "GRAI" => Gs1.EncodeGrai96(r.CompanyPrefix, r.Reference ?? "", r.Serial, r.Filter),
                "GIAI-96" or "GIAI" => Gs1.EncodeGiai96(r.CompanyPrefix, r.Serial, r.Filter),
                _ => throw new ArgumentException("Unknown scheme; use SGTIN-96, GRAI-96 or GIAI-96"),
            };
            return Ok(new { epc, scheme = Gs1.Scheme(epc) });
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpGet("decode-any/{epc}")]
    public IActionResult DecodeAny(string epc)
    {
        var e = TagResolver.Normalize(epc);
        return Gs1.Scheme(e) switch
        {
            "SGTIN-96" => Ok(new { scheme = "SGTIN-96", value = Sgtin96.Decode(e) }),
            "GRAI-96" => Ok(new { scheme = "GRAI-96", value = Gs1.DecodeGrai96(e) }),
            "GIAI-96" => Ok(new { scheme = "GIAI-96", value = Gs1.DecodeGiai96(e) }),
            var s => Ok(new { scheme = s }),
        };
    }
}

[ApiController, Route("api/templates"), Authorize(Policy = "Admin")]
public class TemplateExchangeController : ControllerBase
{
    private readonly TemplateProvisioner _prov;
    public TemplateExchangeController(TemplateProvisioner prov) => _prov = prov;

    /// <summary>Exports the tenant's item types and rules as a template definition JSON.</summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export(CancellationToken ct) => Ok(new { code = "custom", name = "Exported configuration", vertical = "Custom", exportedAt = DateTime.UtcNow, definition = await _prov.ExportAsync(ct) });

    public record ImportRequest(string? Code, string? Vertical, TemplateDefinition Definition);

    /// <summary>Imports a template definition (as produced by export) into the tenant; existing codes/names are skipped.</summary>
    [HttpPost("import")]
    public async Task<IActionResult> Import(ImportRequest r, CancellationToken ct) => Ok(await _prov.ApplyDefinitionAsync(r.Code ?? "custom", r.Vertical ?? "Custom", r.Definition, ct));
}
