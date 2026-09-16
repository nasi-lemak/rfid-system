using Microsoft.EntityFrameworkCore;
using Rfid.Application.Contracts;
using Rfid.Application.Security;
using Rfid.Domain;
using Rfid.Domain.Entities;

namespace Rfid.Application.Inventory;

/// <summary>
/// Warehouse view over quantity items. There is no separate stock ledger: a balance is the sum of the
/// quantity-item rows of a type at a location (per lot), movements are the QuantityChanged events, and a pick
/// list is an allocation over those rows (FEFO). Execution is ordinary operations (MoveQuantity, Adjust, Dispatch).
/// </summary>
public class StockService
{
    private readonly IAppDb _db;
    public StockService(IAppDb db) => _db = db;

    public record BalanceRow(Guid ItemTypeId, string ItemType, string ItemTypeCode, Guid? LocationId, string? Location, string? LotNumber, DateOnly? Expiry, decimal Quantity, string? Unit, int Rows, DateTime? LastSeenAt);
    public record SummaryRow(Guid ItemTypeId, string ItemType, string ItemTypeCode, decimal OnHand, string? Unit, int Locations, int Lots, decimal? ReorderPoint, decimal Shortfall, decimal Consumed30d, double? DaysOfCover, DateOnly? EarliestExpiry, int ExpiringSoon);
    public record MovementRow(DateTime At, Guid ItemId, string Identifier, string ItemType, string? LotNumber, decimal Delta, decimal? Before, Guid? LocationId, string? Location, string Kind, Guid? OperationId);
    public record AllocateLine(string? ItemTypeCode, Guid? ItemTypeId, decimal Quantity, string? LotNumber);
    public record AllocateRequest(List<AllocateLine> Lines, Guid? FromLocationId, bool Fefo = true);
    public record Pick(Guid ItemId, string Identifier, string? Epc, Guid? LocationId, string? Location, string? LotNumber, DateOnly? Expiry, decimal Available, decimal Take);
    public record AllocationLine(string ItemType, string ItemTypeCode, decimal Requested, decimal Allocated, decimal Shortfall, List<Pick> Picks);

    private IQueryable<Item> Live(SiteScope scope) => scope.Items(_db.Items).Where(i => i.Status != ItemStatus.Disposed && i.ItemType!.Category == ItemCategory.Quantity);

    public async Task<List<BalanceRow>> BalancesAsync(SiteScope scope, Guid? itemTypeId = null, Guid? locationId = null, bool includeZero = false, CancellationToken ct = default)
    {
        var q = Live(scope);
        if (itemTypeId.HasValue) q = q.Where(i => i.ItemTypeId == itemTypeId);
        if (locationId.HasValue)
        {
            var path = await _db.Locations.Where(l => l.Id == locationId).Select(l => l.Path).FirstOrDefaultAsync(ct);
            if (path != null) q = q.Where(i => i.CurrentLocation != null && i.CurrentLocation.Path.StartsWith(path));
        }
        var rows = await q.Select(i => new { i.ItemTypeId, TypeName = i.ItemType!.Name, TypeCode = i.ItemType.Code, i.CurrentLocationId, Location = i.CurrentLocation != null ? i.CurrentLocation.Name : null, i.LotNumber, i.ExpiryDate, i.Quantity, i.Unit, i.LastSeenAt }).ToListAsync(ct);
        return rows.GroupBy(r => new { r.ItemTypeId, r.CurrentLocationId, r.LotNumber, r.ExpiryDate })
            .Select(g => new BalanceRow(g.Key.ItemTypeId, g.First().TypeName, g.First().TypeCode, g.Key.CurrentLocationId, g.First().Location, g.Key.LotNumber, g.Key.ExpiryDate, g.Sum(r => r.Quantity), g.First().Unit, g.Count(), g.Max(r => r.LastSeenAt)))
            .Where(b => includeZero || b.Quantity != 0)
            .OrderBy(b => b.ItemType).ThenBy(b => b.Location).ThenBy(b => b.Expiry).ThenBy(b => b.LotNumber).ToList();
    }

    public async Task<List<SummaryRow>> SummaryAsync(SiteScope scope, DateTime? now = null, CancellationToken ct = default)
    {
        var t = now ?? DateTime.UtcNow; var since = t.AddDays(-30); var soon = DateOnly.FromDateTime(t.AddDays(30));
        var rows = await Live(scope).Select(i => new { i.Id, i.ItemTypeId, TypeName = i.ItemType!.Name, TypeCode = i.ItemType.Code, Reorder = i.ItemType.ReorderPoint, i.CurrentLocationId, i.LotNumber, i.ExpiryDate, i.Quantity, i.Unit }).ToListAsync(ct);
        var ids = rows.Select(r => r.Id).ToList();
        // Consumption = negative, non-transfer QuantityChanged deltas in the window (issues, dispatches, adjustments down).
        var events = await _db.ItemEvents.Where(e => e.Type == ItemEventType.QuantityChanged && e.OccurredAt >= since && ids.Contains(e.ItemId)).Select(e => new { e.ItemId, e.Data }).ToListAsync(ct);
        var consumed = events.Where(e => !IsTransfer(e.Data)).GroupBy(e => e.ItemId).ToDictionary(g => g.Key, g => g.Sum(e => Math.Max(0, -Delta(e.Data))));
        return rows.GroupBy(r => r.ItemTypeId).Select(g =>
        {
            var onHand = g.Sum(r => r.Quantity);
            var used = g.Sum(r => consumed.GetValueOrDefault(r.Id));
            var perDay = used / 30m;
            var reorder = g.First().Reorder;
            return new SummaryRow(g.Key, g.First().TypeName, g.First().TypeCode, onHand, g.First().Unit, g.Select(r => r.CurrentLocationId).Distinct().Count(), g.Select(r => r.LotNumber).Distinct().Count(),
                reorder, reorder is decimal rp && onHand < rp ? rp - onHand : 0, used, perDay > 0 ? Math.Round((double)(onHand / perDay), 1) : null,
                g.Where(r => r.ExpiryDate != null).Min(r => r.ExpiryDate), g.Count(r => r.ExpiryDate != null && r.ExpiryDate <= soon && r.Quantity > 0));
        }).OrderByDescending(s => s.Shortfall).ThenBy(s => s.ItemType).ToList();
    }

    public async Task<List<MovementRow>> MovementsAsync(SiteScope scope, int days = 30, Guid? itemTypeId = null, int take = 500, CancellationToken ct = default)
    {
        var since = DateTime.UtcNow.AddDays(-days);
        var items = Live(scope); if (itemTypeId.HasValue) items = items.Where(i => i.ItemTypeId == itemTypeId);
        var ids = items.Select(i => i.Id);
        var events = await _db.ItemEvents.Where(e => e.Type == ItemEventType.QuantityChanged && e.OccurredAt >= since && ids.Contains(e.ItemId)).OrderByDescending(e => e.OccurredAt).Take(Math.Clamp(take, 1, 5000)).ToListAsync(ct);
        var itemIds = events.Select(e => e.ItemId).Distinct().ToList();
        var info = await _db.Items.Include(i => i.ItemType).Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        var locIds = events.Select(e => e.ToLocationId ?? e.FromLocationId).Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList();
        var locs = await _db.Locations.Where(l => locIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.Name, ct);
        return events.Select(e =>
        {
            var it = info.GetValueOrDefault(e.ItemId); var loc = e.ToLocationId ?? e.FromLocationId;
            var delta = Delta(e.Data);
            var kind = IsTransfer(e.Data) ? (delta >= 0 ? "transfer-in" : "transfer-out") : e.Data.ContainsKey("counted") ? "count" : delta >= 0 ? "receipt" : "consumption";
            return new MovementRow(e.OccurredAt, e.ItemId, it?.Identifier ?? "?", it?.ItemType?.Name ?? "?", it?.LotNumber, delta, Before(e.Data), loc, loc.HasValue ? locs.GetValueOrDefault(loc.Value) : null, kind, e.OperationId);
        }).ToList();
    }

    /// <summary>Allocates requested quantities over the available lot rows: earliest expiry first (FEFO), then the largest lot, optionally within a location subtree.</summary>
    public async Task<List<AllocationLine>> AllocateAsync(AllocateRequest req, SiteScope scope, CancellationToken ct = default)
    {
        var q = Live(scope).Include(i => i.ItemType).Include(i => i.CurrentLocation).Include(i => i.Tags).Where(i => i.Quantity > 0 && i.ParentItemId == null);
        if (req.FromLocationId.HasValue)
        {
            var path = await _db.Locations.Where(l => l.Id == req.FromLocationId).Select(l => l.Path).FirstOrDefaultAsync(ct);
            if (path != null) q = q.Where(i => i.CurrentLocation != null && i.CurrentLocation.Path.StartsWith(path));
        }
        var candidates = await q.ToListAsync(ct);
        var result = new List<AllocationLine>();
        var taken = new Dictionary<Guid, decimal>();
        foreach (var line in req.Lines)
        {
            var pool = candidates.Where(i => (line.ItemTypeId.HasValue && i.ItemTypeId == line.ItemTypeId) || (line.ItemTypeCode != null && string.Equals(i.ItemType!.Code, line.ItemTypeCode, StringComparison.OrdinalIgnoreCase)))
                .Where(i => line.LotNumber == null || i.LotNumber == line.LotNumber)
                .Select(i => (item: i, available: i.Quantity - taken.GetValueOrDefault(i.Id))).Where(x => x.available > 0);
            pool = req.Fefo ? pool.OrderBy(x => x.item.ExpiryDate ?? DateOnly.MaxValue).ThenByDescending(x => x.available) : pool.OrderByDescending(x => x.available);
            var remaining = line.Quantity; var picks = new List<Pick>(); string typeName = line.ItemTypeCode ?? "", typeCode = line.ItemTypeCode ?? "";
            foreach (var (item, available) in pool)
            {
                if (remaining <= 0) break;
                var take = Math.Min(available, remaining);
                taken[item.Id] = taken.GetValueOrDefault(item.Id) + take; remaining -= take;
                typeName = item.ItemType!.Name; typeCode = item.ItemType.Code;
                picks.Add(new Pick(item.Id, item.Identifier, item.Tags.FirstOrDefault()?.Epc, item.CurrentLocationId, item.CurrentLocation?.Name, item.LotNumber, item.ExpiryDate, available, take));
            }
            result.Add(new AllocationLine(typeName, typeCode, line.Quantity, line.Quantity - remaining, remaining, picks));
        }
        return result;
    }

    private static decimal Delta(Dictionary<string, object?> data) => data.TryGetValue("delta", out var v) && decimal.TryParse(v?.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0;
    private static decimal? Before(Dictionary<string, object?> data) => data.TryGetValue("before", out var v) && decimal.TryParse(v?.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null;
    private static bool IsTransfer(Dictionary<string, object?> data) => data.TryGetValue("transfer", out var v) && string.Equals(v?.ToString(), "true", StringComparison.OrdinalIgnoreCase);
}
