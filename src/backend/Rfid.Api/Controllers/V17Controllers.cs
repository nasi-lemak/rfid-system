using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rfid.Application.Contracts;
using Rfid.Application.Services;
using Rfid.Domain;
using Rfid.Domain.Entities;
using Rfid.Infrastructure.Persistence;

namespace Rfid.Api.Controllers;

// ───────────────────────────── Billing ─────────────────────────────

[ApiController, Route("api/billing"), Authorize]
public class BillingController : ControllerBase
{
    private readonly AppDbContext _db; private readonly BillingService _svc; private readonly ICurrentContext _ctx;
    public BillingController(AppDbContext db, BillingService svc, ICurrentContext ctx) { _db = db; _svc = svc; _ctx = ctx; }

    [HttpGet("rate-cards")]
    public async Task<IActionResult> RateCards()
    {
        var cards = await _db.RateCards.OrderByDescending(c => c.Priority).ThenBy(c => c.Name).ToListAsync();
        var types = await _db.ItemTypes.ToDictionaryAsync(t => t.Id, t => t.Name); var parties = await _db.Parties.ToDictionaryAsync(p => p.Id, p => p.Name);
        return Ok(cards.Select(c => new { c.Id, c.Name, c.ItemTypeId, ItemType = c.ItemTypeId.HasValue ? types.GetValueOrDefault(c.ItemTypeId.Value) : null, c.PartyKind, c.PartyId, Party = c.PartyId.HasValue ? parties.GetValueOrDefault(c.PartyId.Value) : null, c.Currency, c.DepositAmount, c.CycleFee, c.DailyFee, c.FreeDays, c.LateFeePerDay, c.LossFee, c.Enabled, c.Priority }));
    }

    public record RateCardWrite(string Name, Guid? ItemTypeId, PartyKind? PartyKind, Guid? PartyId, string Currency, decimal DepositAmount, decimal CycleFee, decimal DailyFee, int FreeDays, decimal LateFeePerDay, decimal LossFee, bool Enabled = true, int Priority = 0);

    [HttpPost("rate-cards"), Authorize(Policy = "Admin")]
    public async Task<IActionResult> CreateCard(RateCardWrite w) { var c = new RateCard { TenantId = _ctx.TenantId }; Apply(c, w); _db.RateCards.Add(c); await _db.SaveChangesAsync(); return Ok(c); }
    [HttpPut("rate-cards/{id:guid}"), Authorize(Policy = "Admin")]
    public async Task<IActionResult> UpdateCard(Guid id, RateCardWrite w) { var c = await _db.RateCards.FindAsync(id); if (c == null) return NotFound(); Apply(c, w); await _db.SaveChangesAsync(); return Ok(c); }
    [HttpDelete("rate-cards/{id:guid}"), Authorize(Policy = "Admin")]
    public async Task<IActionResult> DeleteCard(Guid id) { var c = await _db.RateCards.FindAsync(id); if (c == null) return NotFound(); _db.RateCards.Remove(c); await _db.SaveChangesAsync(); return NoContent(); }
    private static void Apply(RateCard c, RateCardWrite w) { c.Name = w.Name; c.ItemTypeId = w.ItemTypeId; c.PartyKind = w.PartyKind; c.PartyId = w.PartyId; c.Currency = w.Currency; c.DepositAmount = w.DepositAmount; c.CycleFee = w.CycleFee; c.DailyFee = w.DailyFee; c.FreeDays = w.FreeDays; c.LateFeePerDay = w.LateFeePerDay; c.LossFee = w.LossFee; c.Enabled = w.Enabled; c.Priority = w.Priority; }

    [HttpPost("accrue"), Authorize(Policy = "Admin")]
    public async Task<IActionResult> Accrue(CancellationToken ct) => Ok(new { created = await _svc.AccrueAsync(DateTime.UtcNow, ct) });

    [HttpGet("balances")]
    public async Task<IActionResult> Balances(CancellationToken ct) => Ok(await _svc.BalancesAsync(ct));

    [HttpGet("ledger")]
    public async Task<IActionResult> Ledger(Guid? partyId, Guid? invoiceId, bool? unbilled, int take = 300)
    {
        var q = _db.LedgerEntries.AsQueryable();
        if (partyId.HasValue) q = q.Where(l => l.PartyId == partyId); if (invoiceId.HasValue) q = q.Where(l => l.InvoiceId == invoiceId); if (unbilled == true) q = q.Where(l => l.InvoiceId == null);
        var rows = await q.OrderByDescending(l => l.OccurredAt).Take(Math.Clamp(take, 1, 2000)).ToListAsync();
        var items = await _db.Items.Where(i => rows.Select(r => r.ItemId).Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Identifier); var parties = await _db.Parties.ToDictionaryAsync(p => p.Id, p => p.Name);
        return Ok(rows.Select(l => new { l.Id, l.PartyId, Party = parties.GetValueOrDefault(l.PartyId), l.ItemId, Item = l.ItemId.HasValue ? items.GetValueOrDefault(l.ItemId.Value) : null, l.Kind, l.Amount, l.Currency, l.Quantity, l.Description, l.OccurredAt, l.InvoiceId }));
    }

    public record AdjustmentWrite(Guid PartyId, decimal Amount, string Description, string Currency = "USD");
    [HttpPost("ledger/adjustment"), Authorize(Policy = "Admin")]
    public async Task<IActionResult> Adjust(AdjustmentWrite w) { var l = new LedgerEntry { TenantId = _ctx.TenantId, PartyId = w.PartyId, Kind = LedgerKind.Adjustment, Amount = w.Amount, Currency = w.Currency, Description = w.Description }; _db.LedgerEntries.Add(l); await _db.SaveChangesAsync(); return Ok(l); }

    [HttpGet("invoices")]
    public async Task<IActionResult> Invoices(Guid? partyId, InvoiceStatus? status)
    {
        var q = _db.Invoices.AsQueryable(); if (partyId.HasValue) q = q.Where(i => i.PartyId == partyId); if (status.HasValue) q = q.Where(i => i.Status == status);
        var rows = await q.OrderByDescending(i => i.CreatedAt).Take(500).ToListAsync(); var parties = await _db.Parties.ToDictionaryAsync(p => p.Id, p => p.Name);
        return Ok(rows.Select(i => new { i.Id, i.Number, i.PartyId, Party = parties.GetValueOrDefault(i.PartyId), i.PeriodFrom, i.PeriodTo, i.Status, i.Currency, i.Charges, i.Credits, i.Total, i.IssuedAt, i.DueAt, i.PaidAt, i.Lines, i.CreatedAt }));
    }

    public record GenerateWrite(DateTime? From, DateTime? To, Guid? PartyId, int DueDays = 30);
    [HttpPost("invoices/generate"), Authorize(Policy = "Admin")]
    public async Task<IActionResult> Generate(GenerateWrite w, CancellationToken ct)
    {
        var to = w.To ?? new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc); var from = w.From ?? to.AddMonths(-1);
        return Ok(await _svc.GenerateInvoicesAsync(from, to, w.PartyId, w.DueDays, ct));
    }

    [HttpPost("invoices/{id:guid}/issue"), Authorize(Policy = "Admin")] public async Task<IActionResult> Issue(Guid id, CancellationToken ct) => Ok(await _svc.SetStatusAsync(id, InvoiceStatus.Issued, DateTime.UtcNow, ct));
    [HttpPost("invoices/{id:guid}/pay"), Authorize(Policy = "Admin")] public async Task<IActionResult> Pay(Guid id, CancellationToken ct) => Ok(await _svc.SetStatusAsync(id, InvoiceStatus.Paid, DateTime.UtcNow, ct));
    [HttpPost("invoices/{id:guid}/void"), Authorize(Policy = "Admin")] public async Task<IActionResult> Void(Guid id, CancellationToken ct) => Ok(await _svc.SetStatusAsync(id, InvoiceStatus.Void, DateTime.UtcNow, ct));

    /// <summary>Printable invoice (HTML).</summary>
    [HttpGet("invoices/{id:guid}/print")]
    public async Task<IActionResult> Print(Guid id)
    {
        var inv = await _db.Invoices.FindAsync(id); if (inv == null) return NotFound();
        var party = await _db.Parties.FindAsync(inv.PartyId); var tenant = await _db.Tenants.FindAsync(inv.TenantId);
        var entries = await _db.LedgerEntries.Where(l => l.InvoiceId == id && l.Kind != LedgerKind.Payment).OrderBy(l => l.OccurredAt).ToListAsync();
        string M(decimal v) => $"{v:N2} {inv.Currency}";
        var rows = string.Join("", entries.Select(e => $"<tr><td>{e.OccurredAt:yyyy-MM-dd}</td><td>{e.Kind}</td><td>{System.Net.WebUtility.HtmlEncode(e.Description)}</td><td class=r>{M(e.Amount)}</td></tr>"));
        var html = $@"<!doctype html><html><head><meta charset=utf-8><title>{inv.Number}</title><style>body{{font-family:system-ui,sans-serif;margin:40px;color:#1d2530}}h1{{margin:0}}table{{width:100%;border-collapse:collapse;margin-top:20px}}td,th{{padding:6px 8px;border-bottom:1px solid #e2e6ea;text-align:left;font-size:13px}}.r{{text-align:right}}.tot{{font-weight:700;font-size:16px}}.muted{{color:#6b7684}}@media print{{button{{display:none}}}}</style></head><body>
<div style='display:flex;justify-content:space-between'><div><h1>Invoice {inv.Number}</h1><div class=muted>{System.Net.WebUtility.HtmlEncode(tenant?.Name ?? "")}</div></div><div style='text-align:right'><div><b>Status:</b> {inv.Status}</div><div><b>Period:</b> {inv.PeriodFrom:yyyy-MM-dd} – {inv.PeriodTo:yyyy-MM-dd}</div><div><b>Due:</b> {inv.DueAt:yyyy-MM-dd}</div></div></div>
<p><b>Bill to</b><br>{System.Net.WebUtility.HtmlEncode(party?.Name ?? "")}<br><span class=muted>{System.Net.WebUtility.HtmlEncode(party?.Email ?? "")}</span></p>
<table><thead><tr><th>Date</th><th>Type</th><th>Description</th><th class=r>Amount</th></tr></thead><tbody>{rows}</tbody>
<tfoot><tr><td colspan=3 class=r>Charges</td><td class=r>{M(inv.Charges)}</td></tr><tr><td colspan=3 class=r>Credits</td><td class=r>{M(inv.Credits)}</td></tr><tr class=tot><td colspan=3 class=r>Total due</td><td class=r>{M(inv.Total)}</td></tr></tfoot></table>
<p class=muted>{System.Net.WebUtility.HtmlEncode(inv.Notes ?? "")}</p><button onclick='print()'>Print</button></body></html>";
        return Content(html, "text/html");
    }
}

// ───────────────────────────── Predictive maintenance ─────────────────────────────

[ApiController, Route("api/maintenance"), Authorize]
public class MaintenanceController : ControllerBase
{
    private readonly MaintenanceService _svc; private readonly AppDbContext _db;
    public MaintenanceController(MaintenanceService svc, AppDbContext db) { _svc = svc; _db = db; }

    [HttpGet("predictions")]
    public async Task<IActionResult> Predictions(Guid? itemTypeId, double minRisk = 0, int take = 200, CancellationToken ct = default)
    {
        var preds = await _svc.PredictAsync(DateTime.UtcNow, itemTypeId, null, ct);
        return Ok(new { total = preds.Count, high = preds.Count(p => p.Level == "High"), medium = preds.Count(p => p.Level == "Medium"), low = preds.Count(p => p.Level == "Low"), rows = preds.Where(p => p.Risk >= minRisk).Take(Math.Clamp(take, 1, 2000)) });
    }

    [HttpGet("predictions/{itemId:guid}")]
    public async Task<IActionResult> Prediction(Guid itemId, CancellationToken ct)
    {
        var p = (await _svc.PredictAsync(DateTime.UtcNow, null, itemId, ct)).FirstOrDefault(); if (p == null) return NotFound();
        return Ok(new { prediction = p, trend = await _svc.TrendAsync(itemId, 90, ct) });
    }

    [HttpPost("run"), Authorize(Policy = "Operator")]
    public async Task<IActionResult> Run(CancellationToken ct) { var (f, a) = await _svc.RunAsync(DateTime.UtcNow, ct); return Ok(new { forecasts = f, alerts = a }); }
}

// ───────────────────────────── Supplier / customer portal ─────────────────────────────

[ApiController, Route("api/portal"), Authorize]
public class PortalController : ControllerBase
{
    private readonly AppDbContext _db; private readonly ICurrentContext _ctx;
    public PortalController(AppDbContext db, ICurrentContext ctx) { _db = db; _ctx = ctx; }

    private Guid? PartyId => Guid.TryParse(User.FindFirst("portal")?.Value, out var g) ? g : null;
    private IQueryable<Item> MyItems(Guid party) => _db.Items.Include(i => i.ItemType).Include(i => i.CurrentLocation).Include(i => i.Tags).Where(i => i.CustodianPartyId == party || _db.ItemEvents.Any(e => e.ItemId == i.Id && (e.ToPartyId == party || e.FromPartyId == party)));

    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        if (PartyId is not { } party) return Forbid();
        var p = await _db.Parties.FindAsync(party); if (p == null) return NotFound();
        var now = DateTime.UtcNow;
        var inCustody = _db.Items.Where(i => i.CustodianPartyId == party);
        var outstanding = await _db.Invoices.Where(i => i.PartyId == party && i.Status == InvoiceStatus.Issued).SumAsync(i => i.Total);
        return Ok(new { party = new { p.Id, p.Name, p.Kind, p.Code, p.Email }, inCustody = await inCustody.CountAsync(), overdue = await inCustody.CountAsync(i => i.DueBackAt != null && i.DueBackAt < now), dueSoon = await inCustody.CountAsync(i => i.DueBackAt != null && i.DueBackAt >= now && i.DueBackAt < now.AddDays(7)), everHandled = await MyItems(party).CountAsync(), openInvoices = await _db.Invoices.CountAsync(i => i.PartyId == party && i.Status == InvoiceStatus.Issued), outstanding, depositsHeld = await _db.LedgerEntries.Where(l => l.PartyId == party && (l.Kind == LedgerKind.Deposit || l.Kind == LedgerKind.DepositRefund)).SumAsync(l => l.Amount) });
    }

    [HttpGet("items")]
    public async Task<IActionResult> Items(string? q, bool? inCustody, int? page, int? pageSize)
    {
        if (PartyId is not { } party) return Forbid();
        var (p, s) = Query.Page(page, pageSize);
        var query = MyItems(party);
        if (inCustody == true) query = query.Where(i => i.CustodianPartyId == party);
        if (!string.IsNullOrWhiteSpace(q)) { var like = $"%{q}%"; query = query.Where(i => EF.Functions.ILike(i.Name, like) || EF.Functions.ILike(i.Identifier, like)); }
        var total = await query.CountAsync();
        var rows = await query.OrderByDescending(i => i.CustodianPartyId == party).ThenBy(i => i.Name).Skip((p - 1) * s).Take(s).ToListAsync();
        return Ok(new Paged<object>(rows.Select(i => (object)new { i.Id, i.Name, i.Identifier, ItemType = i.ItemType?.Name, i.State, InCustody = i.CustodianPartyId == party, i.DueBackAt, i.LastSeenAt, Location = i.CustodianPartyId == party ? null : i.CurrentLocation?.Name, Epc = i.Tags.FirstOrDefault()?.Epc }).ToList(), total, p, s));
    }

    [HttpGet("activity")]
    public async Task<IActionResult> Activity(int take = 100)
    {
        if (PartyId is not { } party) return Forbid();
        var evs = await _db.ItemEvents.Where(e => e.ToPartyId == party || e.FromPartyId == party).OrderByDescending(e => e.OccurredAt).Take(Math.Clamp(take, 1, 500)).ToListAsync();
        var items = await _db.Items.Where(i => evs.Select(e => e.ItemId).Contains(i.Id)).ToDictionaryAsync(i => i.Id);
        var ops = await _db.Operations.Where(o => evs.Select(e => e.OperationId).Contains(o.Id)).ToDictionaryAsync(o => o.Id);
        return Ok(evs.Select(e => new { e.Id, e.ItemId, Item = items.GetValueOrDefault(e.ItemId)?.Name, Identifier = items.GetValueOrDefault(e.ItemId)?.Identifier, Direction = e.ToPartyId == party ? "Issued to you" : "Returned by you", e.OccurredAt, Reference = e.OperationId.HasValue ? ops.GetValueOrDefault(e.OperationId.Value)?.Reference : null, Operation = e.OperationId.HasValue ? ops.GetValueOrDefault(e.OperationId.Value)?.Type.ToString() : null }));
    }

    [HttpGet("invoices")]
    public async Task<IActionResult> Invoices()
    {
        if (PartyId is not { } party) return Forbid();
        return Ok(await _db.Invoices.Where(i => i.PartyId == party && i.Status != InvoiceStatus.Draft).OrderByDescending(i => i.CreatedAt).Select(i => new { i.Id, i.Number, i.PeriodFrom, i.PeriodTo, i.Status, i.Currency, i.Charges, i.Credits, i.Total, i.IssuedAt, i.DueAt, i.PaidAt, i.Lines }).ToListAsync());
    }

    [HttpGet("invoices/{id:guid}/print")]
    public async Task<IActionResult> Print(Guid id, [FromServices] BillingController billing)
    {
        if (PartyId is not { } party) return Forbid();
        var inv = await _db.Invoices.FindAsync(id); if (inv == null || inv.PartyId != party || inv.Status == InvoiceStatus.Draft) return NotFound();
        return await billing.Print(id);
    }

    [HttpGet("ledger")]
    public async Task<IActionResult> Ledger(int take = 200)
    {
        if (PartyId is not { } party) return Forbid();
        var rows = await _db.LedgerEntries.Where(l => l.PartyId == party).OrderByDescending(l => l.OccurredAt).Take(Math.Clamp(take, 1, 1000)).ToListAsync();
        var items = await _db.Items.Where(i => rows.Select(r => r.ItemId).Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Identifier);
        return Ok(rows.Select(l => new { l.Id, l.Kind, l.Amount, l.Currency, l.Description, l.OccurredAt, l.InvoiceId, Item = l.ItemId.HasValue ? items.GetValueOrDefault(l.ItemId.Value) : null }));
    }
}

// ───────────────────────────── EPCIS 2.0 ─────────────────────────────

[ApiController, Route("api/epcis/v2"), Authorize]
public class EpcisController : ControllerBase
{
    private readonly EpcisService _svc; private readonly AppDbContext _db;
    public EpcisController(EpcisService svc, AppDbContext db) { _svc = svc; _db = db; }

    private static readonly System.Text.Json.JsonSerializerOptions JsonOut = new() { WriteIndented = true, TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(), Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private void Headers() { Response.Headers["GS1-EPCIS-Version"] = "2.0"; Response.Headers["GS1-CBV-Version"] = "2.0"; Response.Headers["GS1-Extensions"] = "rfid=https://rfid-platform.local/epcis"; }

    /// <summary>EPCIS 2.0 simple event query. Filters: EQ_bizStep, EQ_disposition, EQ_action, MATCH_epc / MATCH_anyEPC, GE_eventTime, LT_eventTime, eventType, EQ_readPoint (location id), perPage, page.</summary>
    [HttpGet("events")]
    public async Task<IActionResult> Events([FromQuery] string? EQ_bizStep, [FromQuery] string? EQ_disposition, [FromQuery] string? EQ_action, [FromQuery] string? MATCH_epc, [FromQuery] string? MATCH_anyEPC, [FromQuery] DateTime? GE_eventTime, [FromQuery] DateTime? LT_eventTime, [FromQuery] string? eventType, [FromQuery] Guid? EQ_readPoint, int perPage = 100, int page = 1, CancellationToken ct = default)
    {
        Headers();
        var (doc, total) = await _svc.QueryAsync(new EpcisService.Query(EQ_bizStep, EQ_disposition, EQ_action, MATCH_epc ?? MATCH_anyEPC, null, GE_eventTime, LT_eventTime, eventType, EQ_readPoint, Math.Clamp(perPage, 1, 1000), page), ct);
        Response.Headers["X-Total-Count"] = total.ToString();
        if (page * perPage < total) Response.Headers["Link"] = $"<{Request.Path}?{Request.QueryString.ToString().TrimStart('?').Replace($"page={page}", "")}&page={page + 1}>; rel=\"next\"";
        return Content(doc.ToJsonString(JsonOut), "application/ld+json");
    }

    [HttpGet("events/{eventId}")]
    public async Task<IActionResult> Event(string eventId, CancellationToken ct)
    {
        Headers();
        if (!Guid.TryParse(eventId.Replace("urn:uuid:", ""), out var id)) return NotFound();
        var e = await _db.ItemEvents.FirstOrDefaultAsync(x => x.Id == id, ct); if (e == null) return NotFound();
        var doc = await _svc.DocumentAsync(new List<ItemEvent> { e }, ct);
        return Content(doc["epcisBody"]!["eventList"]![0]!.ToJsonString(JsonOut), "application/ld+json");
    }

    [HttpGet("epcs/{epc}/events")]
    public async Task<IActionResult> ByEpc(string epc, int perPage = 100, int page = 1, CancellationToken ct = default)
    {
        Headers();
        var (doc, total) = await _svc.QueryAsync(new EpcisService.Query(Epc: Uri.UnescapeDataString(epc), PerPage: Math.Clamp(perPage, 1, 1000), Page: page), ct);
        Response.Headers["X-Total-Count"] = total.ToString();
        return Content(doc.ToJsonString(JsonOut), "application/ld+json");
    }

    /// <summary>EPCIS 2.0 capture interface: POST an EPCISDocument (JSON). Synchronous; returns the capture job.</summary>
    [HttpPost("capture"), Authorize(Policy = "Operator")]
    public async Task<IActionResult> Capture([FromBody] JsonNode document, CancellationToken ct)
    {
        Headers();
        var r = await _svc.CaptureAsync(document, ct);
        var job = await _db.EpcisCaptures.OrderByDescending(c => c.CreatedAt).FirstAsync(ct);
        Response.Headers["Location"] = $"/api/epcis/v2/capture/{job.Id}";
        return StatusCode(r.Rejected == 0 ? 200 : r.Applied > 0 ? 207 : 400, new { captureID = job.Id, r.Events, r.Applied, r.Rejected, r.Errors });
    }

    [HttpGet("capture")]
    public async Task<IActionResult> Captures(int take = 50) => Ok(await _db.EpcisCaptures.OrderByDescending(c => c.CreatedAt).Take(Math.Clamp(take, 1, 500)).ToListAsync());

    [HttpGet("capture/{id:guid}")]
    public async Task<IActionResult> CaptureJob(Guid id) => await _db.EpcisCaptures.FindAsync(id) is { } c ? Ok(c) : NotFound();

    /// <summary>Discovery: supported vocabularies and query names.</summary>
    [HttpGet(""), AllowAnonymous]
    public IActionResult Root() { Headers(); return Ok(new { epcisVersion = "2.0", cbvVersion = "2.0", resources = new[] { "events", "events/{eventId}", "epcs/{epc}/events", "capture", "capture/{captureID}" }, eventTypes = new[] { "ObjectEvent", "AggregationEvent" }, queryParameters = new[] { "EQ_bizStep", "EQ_disposition", "EQ_action", "MATCH_epc", "MATCH_anyEPC", "GE_eventTime", "LT_eventTime", "eventType", "EQ_readPoint", "perPage", "page" } }); }
}

[ApiController, Route("api/tags"), Authorize]
public class CodeDescribeController : ControllerBase
{
    /// <summary>What a scanned code is: hex EPC (scheme), GS1 element string / Digital Link (parsed AIs, candidate EPCs) or a plain code.</summary>
    [HttpGet("describe/{code}")]
    public IActionResult Describe(string code) => Ok(TagResolver.Describe(Uri.UnescapeDataString(code)));
}
