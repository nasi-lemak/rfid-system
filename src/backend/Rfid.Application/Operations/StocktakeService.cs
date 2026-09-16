using Microsoft.EntityFrameworkCore;
using Rfid.Application.Contracts;
using Rfid.Domain;
using Rfid.Domain.Entities;

namespace Rfid.Application.Services;

public class StocktakeService
{
    private readonly IAppDb _db;
    private readonly ICurrentContext _ctx;
    private readonly TagResolver _tags;
    private readonly RuleEngine _rules;

    public StocktakeService(IAppDb db, ICurrentContext ctx, TagResolver tags, RuleEngine rules)
    {
        _db = db; _ctx = ctx; _tags = tags; _rules = rules;
    }

    public async Task<Stocktake> CreateAsync(string name, Guid locationId, Guid? itemTypeId, CancellationToken ct = default)
    {
        var loc = await _db.Locations.FindAsync(new object[] { locationId }, ct) ?? throw new NotFoundException("Location");
        var expected = await _db.Items.Include(i => i.CurrentLocation)
            .Where(i => i.Status != ItemStatus.Disposed && i.CurrentLocation != null && i.CurrentLocation.Path.StartsWith(loc.Path))
            .Where(i => itemTypeId == null || i.ItemTypeId == itemTypeId)
            .Select(i => new { i.Id, Epc = i.Tags.Select(t => t.Epc).FirstOrDefault() })
            .ToListAsync(ct);
        var st = new Stocktake
        {
            TenantId = _ctx.TenantId, Name = name, LocationId = locationId, ItemTypeId = itemTypeId,
            UserId = _ctx.UserId, ExpectedCount = expected.Count,
        };
        foreach (var e in expected)
            st.Lines.Add(new StocktakeLine { StocktakeId = st.Id, ItemId = e.Id, Epc = e.Epc, Expected = true });
        _db.Stocktakes.Add(st);
        await _db.SaveChangesAsync(ct);
        return st;
    }

    public async Task<StocktakeSummary> AddScansAsync(Guid id, StocktakeScanRequest req, CancellationToken ct = default)
    {
        var st = await _db.Stocktakes.Include(s => s.Lines).FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException("Stocktake");
        if (st.Status != StocktakeStatus.Open) throw new DomainException("Stocktake is not open");
        var now = DateTime.UtcNow;
        var tagMap = await _tags.ResolveAsync(req.Epcs, ct);
        var byItem = st.Lines.Where(l => l.ItemId.HasValue).ToDictionary(l => l.ItemId!.Value);
        var byEpc = st.Lines.Where(l => l.Epc != null).GroupBy(l => l.Epc!).ToDictionary(g => g.Key, g => g.First());

        foreach (var raw in req.Epcs.Select(TagResolver.Normalize).Distinct())
        {
            _db.TagReads.Add(new TagRead { TenantId = _ctx.TenantId, Epc = raw, DeviceId = req.DeviceId ?? _ctx.DeviceId, LocationId = req.LocationId ?? st.LocationId, ReadAt = now, Source = ReadSource.Handheld, SessionId = st.Id.ToString() });
            var item = tagMap.GetValueOrDefault(raw)?.Item;
            if (item == null)
            {
                if (!byEpc.ContainsKey(raw))
                {
                    var l = new StocktakeLine { StocktakeId = st.Id, Epc = raw, Expected = false, Result = StocktakeResult.Unknown, FoundAt = now, FoundLocationId = req.LocationId };
                    st.Lines.Add(l); _db.StocktakeLines.Add(l); byEpc[raw] = l;
                }
                continue;
            }
            item.LastSeenAt = now; item.LastSeenLocationId = req.LocationId ?? st.LocationId; item.LastSeenDeviceId = req.DeviceId;
            if (byItem.TryGetValue(item.Id, out var line))
            {
                if (line.Result == StocktakeResult.Pending || line.Result == StocktakeResult.Missing)
                {
                    line.Result = StocktakeResult.Found; line.FoundAt = now; line.FoundLocationId = req.LocationId ?? st.LocationId;
                }
            }
            else
            {
                var l = new StocktakeLine { StocktakeId = st.Id, ItemId = item.Id, Epc = raw, Expected = false, Result = StocktakeResult.Unexpected, FoundAt = now, FoundLocationId = req.LocationId ?? st.LocationId };
                st.Lines.Add(l); _db.StocktakeLines.Add(l); byItem[item.Id] = l;
            }
        }
        Recount(st);
        await _db.SaveChangesAsync(ct);
        return Summarize(st);
    }

    public async Task<StocktakeSummary> ReconcileAsync(Guid id, CancellationToken ct = default)
    {
        var st = await _db.Stocktakes.Include(s => s.Lines).FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException("Stocktake");
        if (st.Status != StocktakeStatus.Open) throw new DomainException("Stocktake is not open");
        foreach (var l in st.Lines.Where(l => l.Expected && l.Result == StocktakeResult.Pending)) l.Result = StocktakeResult.Missing;
        st.Status = StocktakeStatus.Reconciled; st.CompletedAt = DateTime.UtcNow;
        Recount(st);
        await _db.SaveChangesAsync(ct);
        return Summarize(st);
    }

    /// <summary>Applies the reconciled result: missing items are flagged, unexpected items are moved into the counted location.</summary>
    public async Task<StocktakeSummary> ApplyAsync(Guid id, CancellationToken ct = default)
    {
        var st = await _db.Stocktakes.Include(s => s.Lines).FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException("Stocktake");
        if (st.Status != StocktakeStatus.Reconciled) throw new DomainException("Reconcile the stocktake first");
        var loc = await _db.Locations.FindAsync(new object[] { st.LocationId }, ct);
        var ids = st.Lines.Where(l => l.ItemId.HasValue).Select(l => l.ItemId!.Value).ToList();
        var items = await _db.Items.Include(i => i.ItemType).Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        var now = DateTime.UtcNow;
        foreach (var line in st.Lines.Where(l => l.ItemId.HasValue))
        {
            var item = items[line.ItemId!.Value];
            switch (line.Result)
            {
                case StocktakeResult.Missing when item.Status == ItemStatus.Active:
                    item.Status = ItemStatus.Missing;
                    await Emit(new ItemEvent { ItemId = item.Id, Type = ItemEventType.Counted, FromLocationId = item.CurrentLocationId, ToLocationId = item.CurrentLocationId, OccurredAt = now, Data = new() { ["stocktake"] = st.Name, ["result"] = "Missing" } }, item, loc, ct);
                    break;
                case StocktakeResult.Found:
                    if (item.Status == ItemStatus.Missing) item.Status = ItemStatus.Active;
                    await Emit(new ItemEvent { ItemId = item.Id, Type = ItemEventType.Counted, FromLocationId = item.CurrentLocationId, ToLocationId = item.CurrentLocationId, OccurredAt = now, Data = new() { ["stocktake"] = st.Name, ["result"] = "Found" } }, item, loc, ct);
                    break;
                case StocktakeResult.Unexpected:
                {
                    var prev = item.CurrentLocationId;
                    item.CurrentLocationId = line.FoundLocationId ?? st.LocationId;
                    item.Status = ItemStatus.Active;
                    await Emit(new ItemEvent { ItemId = item.Id, Type = ItemEventType.Moved, FromLocationId = prev, ToLocationId = item.CurrentLocationId, OccurredAt = now, Data = new() { ["stocktake"] = st.Name, ["result"] = "Unexpected" } }, item, loc, ct);
                    break;
                }
            }
        }
        st.Status = StocktakeStatus.Applied;
        await _db.SaveChangesAsync(ct);
        return Summarize(st);
    }

    private async Task Emit(ItemEvent ev, Item item, Location? loc, CancellationToken ct)
    {
        ev.TenantId = _ctx.TenantId; ev.UserId = _ctx.UserId;
        _db.ItemEvents.Add(ev);
        await _rules.EvaluateAsync(new RuleContext { Event = ev, Item = item, ItemType = item.ItemType, ToLocation = loc }, ct);
    }

    private static void Recount(Stocktake st)
    {
        st.FoundCount = st.Lines.Count(l => l.Result == StocktakeResult.Found);
        st.MissingCount = st.Lines.Count(l => l.Result == StocktakeResult.Missing);
        st.UnexpectedCount = st.Lines.Count(l => l.Result == StocktakeResult.Unexpected);
    }

    public static StocktakeSummary Summarize(Stocktake st) => new()
    {
        Id = st.Id, Name = st.Name, Status = st.Status, Expected = st.ExpectedCount, Found = st.FoundCount,
        Missing = st.Status == StocktakeStatus.Open ? st.Lines.Count(l => l.Expected && l.Result == StocktakeResult.Pending) : st.MissingCount,
        Unexpected = st.UnexpectedCount, Unknown = st.Lines.Count(l => l.Result == StocktakeResult.Unknown),
    };
}
