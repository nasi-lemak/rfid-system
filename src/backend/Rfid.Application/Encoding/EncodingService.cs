using Microsoft.EntityFrameworkCore;
using Rfid.Application.Contracts;
using Rfid.Domain;
using Rfid.Domain.Entities;
using Rfid.Domain.Epc;

namespace Rfid.Application.Services;

public class EncodingRequest
{
    public string? Name { get; set; }
    public EpcScheme Scheme { get; set; } = EpcScheme.Sgtin96;
    public string CompanyPrefix { get; set; } = "";
    /// <summary>SGTIN item reference / GRAI asset type / SSCC extension digit (one digit); ignored for GIAI.</summary>
    public string Reference { get; set; } = "";
    public int Filter { get; set; } = 1;
    /// <summary>Take serials from this pool (recommended) …</summary>
    public Guid? PoolId { get; set; }
    /// <summary>… or start here explicitly.</summary>
    public ulong? FirstSerial { get; set; }
    public int Count { get; set; } = 1;
    /// <summary>tags: create unassigned tags · bind: bind to the given items (ItemIds) or to items of ItemTypeId that have no tag · items: create one item per EPC (ItemTypeId, IdentifierPrefix).</summary>
    public string Mode { get; set; } = "tags";
    public Guid? ItemTypeId { get; set; }
    public List<Guid>? ItemIds { get; set; }
    public string? IdentifierPrefix { get; set; }
    public Guid? PrinterDeviceId { get; set; }
}

public record EncodingPreview(int Count, ulong FirstSerial, ulong LastSerial, List<string> Sample, string? FirstEpc, string? LastEpc, int Duplicates, List<string> Warnings, int TargetItems);

/// <summary>GS1 encoding at scale: serial pools with atomic allocation, previews with duplicate checks, and batch commits that create tags, bind items or create items (and queue labels).</summary>
public class EncodingService
{
    private readonly IAppDb _db; private readonly ICurrentContext _ctx; private readonly PrintQueueService? _print;
    public EncodingService(IAppDb db, ICurrentContext ctx, PrintQueueService? print = null) { _db = db; _ctx = ctx; _print = print; }

    public static string Encode(EpcScheme scheme, string companyPrefix, string reference, int filter, ulong serial) => scheme switch
    {
        EpcScheme.Sgtin96 => Sgtin96.Encode(companyPrefix, reference, serial, filter),
        EpcScheme.Grai96 => Gs1.EncodeGrai96(companyPrefix, reference, serial, filter),
        EpcScheme.Giai96 => Gs1.EncodeGiai96(companyPrefix, serial, filter),
        EpcScheme.Sscc96 => Sscc96.Encode(companyPrefix, int.TryParse(reference, out var ext) ? ext : 0, serial, filter),
        _ => throw new DomainException("Unsupported scheme"),
    };

    public static object Decode(string epc)
    {
        epc = TagResolver.Normalize(epc);
        var scheme = Gs1.Scheme(epc);
        return scheme switch
        {
            "SGTIN-96" when Sgtin96.Decode(epc) is { } s => new { scheme, s.companyPrefix, itemRef = s.itemRef, s.serial },
            "GRAI-96" when Gs1.DecodeGrai96(epc) is { } g => new { scheme, g.companyPrefix, assetType = g.assetType, g.serial },
            "GIAI-96" when Gs1.DecodeGiai96(epc) is { } g => new { scheme, g.companyPrefix, assetReference = g.assetReference },
            "SSCC-96" when Sscc96.Decode(epc) is { } c => new { scheme, c.companyPrefix, extensionDigit = c.extensionDigit, c.serial },
            _ => new { scheme },
        };
    }

    /// <summary>Reserves <paramref name="count"/> consecutive serials from a pool (optimistic concurrency; retried on conflict).</summary>
    public async Task<(SerialPool pool, ulong first, ulong last)> AllocateAsync(Guid poolId, int count, CancellationToken ct = default)
    {
        if (count < 1 || count > 100_000) throw new DomainException("Count must be 1–100000");
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var pool = await _db.SerialPools.FirstOrDefaultAsync(p => p.Id == poolId, ct) ?? throw new NotFoundException("Serial pool");
            var first = pool.NextSerial; var last = first + (ulong)count - 1;
            if (pool.MaxSerial.HasValue && last > pool.MaxSerial.Value) throw new DomainException($"Pool '{pool.Name}' has only {pool.MaxSerial.Value - pool.NextSerial + 1} serials left");
            pool.NextSerial = last + 1; pool.Version++;
            try { await _db.SaveChangesAsync(ct); return (pool, first, last); }
            catch (DbUpdateConcurrencyException) { foreach (var e in ((DbContext)_db).ChangeTracker.Entries<SerialPool>()) await e.ReloadAsync(ct); }
        }
        throw new DomainException("Could not allocate serials – pool is busy, retry");
    }

    private async Task<(string prefix, string reference, int filter, EpcScheme scheme)> ResolveAsync(EncodingRequest r, CancellationToken ct)
    {
        if (r.PoolId is { } pid)
        {
            var pool = await _db.SerialPools.FirstOrDefaultAsync(p => p.Id == pid, ct) ?? throw new NotFoundException("Serial pool");
            return (pool.CompanyPrefix, pool.Reference, pool.Filter, pool.Scheme);
        }
        return (r.CompanyPrefix.Trim(), r.Reference.Trim(), r.Filter, r.Scheme);
    }

    public async Task<EncodingPreview> PreviewAsync(EncodingRequest r, CancellationToken ct = default)
    {
        var (prefix, reference, filter, scheme) = await ResolveAsync(r, ct);
        var warnings = new List<string>();
        var count = Math.Clamp(r.Count, 1, 100_000);
        ulong first = r.PoolId is { } pid ? (await _db.SerialPools.Where(p => p.Id == pid).Select(p => p.NextSerial).FirstAsync(ct)) : r.FirstSerial ?? 1;
        var target = 0;
        if (r.Mode == "bind")
        {
            var items = await TargetItemsAsync(r, ct); target = items.Count;
            if (target == 0) warnings.Add("No target items without tags match the selection");
            if (target < count) { warnings.Add($"Only {target} item(s) to bind – count reduced"); count = Math.Max(1, target); }
        }
        if (r.Mode == "items" && r.ItemTypeId == null) warnings.Add("Item type required to create items");
        List<string> epcs;
        try { epcs = Enumerable.Range(0, count).Select(i => Encode(scheme, prefix, reference, filter, first + (ulong)i)).ToList(); }
        catch (ArgumentException ex) { throw new DomainException(ex.Message); }
        var existing = await _db.Tags.Where(t => epcs.Contains(t.Epc)).Select(t => t.Epc).ToListAsync(ct);
        if (existing.Count > 0) warnings.Add($"{existing.Count} EPC(s) already exist (first: {existing[0]}) – choose another serial range or pool");
        return new EncodingPreview(count, first, first + (ulong)count - 1, epcs.Take(10).ToList(), epcs.FirstOrDefault(), epcs.LastOrDefault(), existing.Count, warnings, target);
    }

    private async Task<List<Item>> TargetItemsAsync(EncodingRequest r, CancellationToken ct)
    {
        var q = _db.Items.Include(i => i.Tags).Where(i => i.Status != ItemStatus.Disposed);
        if (r.ItemIds is { Count: > 0 } ids) q = q.Where(i => ids.Contains(i.Id));
        else if (r.ItemTypeId is { } tid) q = q.Where(i => i.ItemTypeId == tid && !i.Tags.Any(t => t.Status == TagStatus.Active));
        else return new();
        return await q.OrderBy(i => i.Identifier).Take(Math.Clamp(r.Count, 1, 100_000)).ToListAsync(ct);
    }

    public async Task<EncodingBatch> CommitAsync(EncodingRequest r, CancellationToken ct = default)
    {
        var preview = await PreviewAsync(r, ct);
        if (preview.Duplicates > 0) throw new DomainException(preview.Warnings.First(w => w.Contains("already exist")));
        var (prefix, reference, filter, scheme) = await ResolveAsync(r, ct);
        var count = preview.Count; ulong first = preview.FirstSerial;
        if (r.PoolId is { } pid) (_, first, _) = await AllocateAsync(pid, count, ct);
        var batch = new EncodingBatch { TenantId = _ctx.TenantId, Name = r.Name ?? $"{scheme} {prefix} {DateTime.UtcNow:yyyy-MM-dd HH:mm}", Scheme = scheme, CompanyPrefix = prefix, Reference = reference, Filter = filter, FirstSerial = first, LastSerial = first + (ulong)count - 1, Count = count, PoolId = r.PoolId, ItemTypeId = r.ItemTypeId, Mode = r.Mode, UserId = _ctx.UserId };
        _db.EncodingBatches.Add(batch);
        var epcs = Enumerable.Range(0, count).Select(i => Encode(scheme, prefix, reference, filter, first + (ulong)i)).ToList();
        batch.FirstEpc = epcs[0]; batch.LastEpc = epcs[^1];
        var now = DateTime.UtcNow; var itemIdsForPrint = new List<Guid>();
        switch (r.Mode)
        {
            case "tags":
                foreach (var epc in epcs) _db.Tags.Add(new Tag { TenantId = _ctx.TenantId, Epc = epc, EncodedAt = now, EncodingBatchId = batch.Id });
                batch.TagsCreated = count; break;
            case "bind":
            {
                var items = await TargetItemsAsync(r, ct);
                foreach (var (item, epc) in items.Zip(epcs))
                {
                    _db.Tags.Add(new Tag { TenantId = _ctx.TenantId, Epc = epc, ItemId = item.Id, Status = TagStatus.Active, EncodedAt = now, EncodingBatchId = batch.Id });
                    _db.ItemEvents.Add(new ItemEvent { TenantId = _ctx.TenantId, ItemId = item.Id, Type = ItemEventType.Commissioned, UserId = _ctx.UserId, OccurredAt = now, Data = new() { ["epc"] = epc, ["batch"] = batch.Name } });
                    itemIdsForPrint.Add(item.Id);
                }
                batch.TagsCreated = items.Count; batch.ItemsBound = items.Count; break;
            }
            case "items":
            {
                var type = await _db.ItemTypes.FirstOrDefaultAsync(t => t.Id == r.ItemTypeId, ct) ?? throw new DomainException("Item type required");
                var i = 0;
                foreach (var epc in epcs)
                {
                    var serial = first + (ulong)i++;
                    var identifier = $"{r.IdentifierPrefix ?? type.Code + "-"}{serial}";
                    var item = new Item { TenantId = _ctx.TenantId, ItemTypeId = type.Id, Identifier = identifier, Name = $"{type.Name} {serial}", State = type.Lifecycle?.Initial, CreatedAt = now };
                    _db.Items.Add(item);
                    _db.Tags.Add(new Tag { TenantId = _ctx.TenantId, Epc = epc, ItemId = item.Id, Status = TagStatus.Active, EncodedAt = now, EncodingBatchId = batch.Id });
                    _db.ItemEvents.Add(new ItemEvent { TenantId = _ctx.TenantId, ItemId = item.Id, Type = ItemEventType.Commissioned, UserId = _ctx.UserId, OccurredAt = now, Data = new() { ["epc"] = epc, ["batch"] = batch.Name } });
                    itemIdsForPrint.Add(item.Id);
                }
                batch.TagsCreated = count; batch.ItemsBound = count; break;
            }
            default: throw new DomainException("Mode must be tags, bind or items");
        }
        await _db.SaveChangesAsync(ct);
        if (r.PrinterDeviceId is { } printer && _print != null && itemIdsForPrint.Count > 0)
        {
            var jobs = await _print.EnqueueAsync(itemIdsForPrint, printer, PrintReason.Batch, ct: ct);
            batch.PrintJobs = jobs.Count; await _db.SaveChangesAsync(ct);
        }
        return batch;
    }
}
