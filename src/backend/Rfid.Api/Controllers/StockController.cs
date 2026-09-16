using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rfid.Application.Inventory;
using Rfid.Application.Security;
using Rfid.Infrastructure.Persistence;

namespace Rfid.Api.Controllers;

/// <summary>Warehouse stock over quantity items: balances per type/location/lot, replenishment summary, movements and FEFO pick allocation.</summary>
[ApiController, Route("api/stock"), Authorize]
public class StockController : ControllerBase
{
    private readonly StockService _svc; private readonly ISiteAccess _sites; private readonly AppDbContext _db;
    public StockController(StockService svc, ISiteAccess sites, AppDbContext db) { _svc = svc; _sites = sites; _db = db; }
    private Task<SiteScope> Scope(CancellationToken ct) => SiteScope.ForAsync(_sites, _db, ct);

    [HttpGet("balances")] public async Task<IActionResult> Balances(Guid? itemTypeId, Guid? locationId, bool includeZero = false, CancellationToken ct = default) => Ok(await _svc.BalancesAsync(await Scope(ct), itemTypeId, locationId, includeZero, ct));
    [HttpGet("summary")] public async Task<IActionResult> Summary(CancellationToken ct) => Ok(await _svc.SummaryAsync(await Scope(ct), null, ct));
    [HttpGet("movements")] public async Task<IActionResult> Movements(int days = 30, Guid? itemTypeId = null, int take = 500, CancellationToken ct = default) => Ok(await _svc.MovementsAsync(await Scope(ct), Math.Clamp(days, 1, 365), itemTypeId, take, ct));
    /// <summary>Suggests picks for the requested quantities (FEFO); execute them with MoveQuantity / Adjust / Dispatch operations.</summary>
    [HttpPost("allocate"), Authorize(Policy = "Operator")] public async Task<IActionResult> Allocate(StockService.AllocateRequest req, CancellationToken ct) => Ok(await _svc.AllocateAsync(req, await Scope(ct), ct));
}
