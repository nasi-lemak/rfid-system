using Microsoft.EntityFrameworkCore;
using Rfid.Application.Contracts;
using Rfid.Protocols;
using Rfid.Application.Positioning;
using Rfid.Domain.Entities;

namespace Rfid.Application.Services;

/// <summary>Turns multi-antenna sightings of one tag into an x/y estimate inside a zone (antennas act as anchors).</summary>
public class PositionService
{
    private readonly IAppDb _db;
    private readonly IPositionSmoother? _smoother;
    public PositionService(IAppDb db, IPositionSmoother? smoother = null) { _db = db; _smoother = smoother; }

    public record Sighting(Antenna Antenna, double? Rssi, double? RangeM);
    public double MinMoveM { get; set; } = 0.5;
    public TimeSpan StationarySampleEvery { get; set; } = TimeSpan.FromSeconds(30);

    public Estimate? Update(Item item, IEnumerable<(Antenna antenna, double rssi)> sightings, DateTime at)
        => Update(item, sightings.Select(s => new Sighting(s.antenna, s.rssi, null)), at);

    /// <summary>
    /// Estimates and stores the item's position from the antennas (with coordinates, in one location) that saw it.
    /// A direct range (UWB / ranging beacon) is used when present; otherwise RSSI is converted with the path-loss model.
    /// Successive fixes are smoothed by a constant-velocity Kalman filter when a smoother is configured.
    /// </summary>
    public Estimate? Update(Item item, IEnumerable<Sighting> sightings, DateTime at)
    {
        var usable = sightings.Where(s => s.Antenna.X.HasValue && s.Antenna.Y.HasValue && s.Antenna.LocationId.HasValue && (s.RangeM.HasValue || s.Rssi.HasValue)).ToList();
        if (usable.Count == 0) return null;
        var zone = usable.GroupBy(s => s.Antenna.LocationId!.Value).OrderByDescending(g => g.Count()).First();
        var anchors = zone.GroupBy(s => s.Antenna.Id).Select(g =>
        {
            var ranged = g.FirstOrDefault(s => s.RangeM.HasValue);
            if (ranged != null) return new Anchor(ranged.Antenna.X!.Value, ranged.Antenna.Y!.Value, Math.Max(0.05, ranged.RangeM!.Value), Weight: 4); // ranging is far more precise than RSSI
            var best = g.OrderByDescending(s => s.Rssi).First();
            return new Anchor(best.Antenna.X!.Value, best.Antenna.Y!.Value, Trilateration.RssiToDistance(best.Rssi!.Value, best.Antenna.RssiAt1m ?? -45, best.Antenna.PathLossExponent ?? 2.2));
        }).ToList();
        var est = Trilateration.Estimate(anchors, Bounds(zone.First().Antenna.Location));
        if (est == null) return null;
        var (x, y, acc) = _smoother?.Smooth(item, zone.Key, est.X, est.Y, est.AccuracyM, at) ?? (est.X, est.Y, est.AccuracyM);
        // History sample when the item moved noticeably, changed zone, or the last sample is stale (keeps the table small while stationary).
        var moved = item.PositionX == null || item.PositionLocationId != zone.Key || Math.Abs(item.PositionX.Value - x) + Math.Abs((item.PositionY ?? 0) - y) > MinMoveM || item.PositionAt == null || at - item.PositionAt.Value > StationarySampleEvery;
        if (moved) _db.PositionFixes.Add(new PositionFix { TenantId = item.TenantId, ItemId = item.Id, LocationId = zone.Key, X = x, Y = y, AccuracyM = acc, At = at });
        item.PositionX = x; item.PositionY = y; item.PositionLocationId = zone.Key; item.PositionAt = at; item.PositionAccuracyM = acc;
        return est with { X = x, Y = y, AccuracyM = acc };
    }

    public record ExternalApply(PositionIngestResult Result);

    /// <summary>
    /// Applies positions computed elsewhere (vendor RTLS engine, edge agent). Same effect as <see cref="Update(Item, IEnumerable{Sighting}, DateTime)"/>:
    /// a PositionFix when the item moved and the item's current position. Idempotent per (device, batchId); one SaveChanges.
    /// </summary>
    public async Task<PositionIngestResult> ApplyExternalAsync(PositionBatchRequest batch, Services.TagResolver tags, Guid tenantId, CancellationToken ct = default)
    {
        var result = new PositionIngestResult { Received = batch.Fixes.Count };
        var key = string.IsNullOrWhiteSpace(batch.BatchId) ? null : $"{batch.DeviceId?.ToString("N") ?? "-"}:{batch.BatchId.Trim()}";
        if (key != null && await _db.IdempotencyKeys.AnyAsync(k => k.Scope == "position-batch" && k.Key == key, ct)) { result.Duplicate = true; return result; }
        var epcs = batch.Fixes.Where(f => !string.IsNullOrWhiteSpace(f.Epc)).Select(f => f.Epc!).Distinct().ToList();
        var tagMap = epcs.Count > 0 ? await tags.ResolveAsync(epcs, ct) : new Dictionary<string, Tag>();
        var itemIds = batch.Fixes.Where(f => f.ItemId.HasValue).Select(f => f.ItemId!.Value).Distinct().ToList();
        var items = itemIds.Count > 0 ? await _db.Items.Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct) : new Dictionary<Guid, Item>();
        var locIds = batch.Fixes.Select(f => f.LocationId).Distinct().ToList();
        var locs = await _db.Locations.Where(l => locIds.Contains(l.Id)).Select(l => l.Id).ToListAsync(ct);
        foreach (var f in batch.Fixes.OrderBy(f => f.At ?? DateTime.UtcNow))
        {
            Item? item = f.ItemId.HasValue ? items.GetValueOrDefault(f.ItemId.Value) : f.Epc != null && tagMap.TryGetValue(Services.TagResolver.Normalize(f.Epc), out var tag) ? tag.Item : null;
            if (item == null) { result.Unknown++; continue; }
            if (!locs.Contains(f.LocationId) || double.IsNaN(f.X) || double.IsNaN(f.Y)) { result.Rejected++; continue; }
            var at = f.At?.ToUniversalTime() ?? DateTime.UtcNow;
            if (item.PositionAt.HasValue && at < item.PositionAt.Value) { result.Rejected++; continue; }   // late fix never moves the position backwards
            var moved = item.PositionX == null || item.PositionLocationId != f.LocationId || Math.Abs(item.PositionX.Value - f.X) + Math.Abs((item.PositionY ?? 0) - f.Y) > MinMoveM || item.PositionAt == null || at - item.PositionAt.Value > StationarySampleEvery;
            if (moved) _db.PositionFixes.Add(new PositionFix { TenantId = item.TenantId, ItemId = item.Id, LocationId = f.LocationId, X = f.X, Y = f.Y, AccuracyM = f.AccuracyM, At = at });
            item.PositionX = f.X; item.PositionY = f.Y; item.PositionLocationId = f.LocationId; item.PositionAt = at; item.PositionAccuracyM = f.AccuracyM;
            item.LastSeenAt = item.LastSeenAt.HasValue && item.LastSeenAt > at ? item.LastSeenAt : at;
            result.Applied++;
        }
        if (key != null) _db.IdempotencyKeys.Add(new IdempotencyKey { TenantId = tenantId, Scope = "position-batch", Key = key, Response = System.Text.Json.JsonSerializer.Serialize(result) });
        await _db.SaveChangesAsync(ct);
        return result;
    }

    public static (double w, double h)? Bounds(Location? loc)
    {
        if (loc == null) return null;
        var w = loc.Attributes.TryGetValue("widthM", out var wv) && double.TryParse(wv?.ToString(), out var wd) ? wd : (double?)null;
        var h = loc.Attributes.TryGetValue("heightM", out var hv) && double.TryParse(hv?.ToString(), out var hd) ? hd : (double?)null;
        return w.HasValue && h.HasValue ? (w.Value, h.Value) : null;
    }

    public record HeatCell(int Ix, int Iy, double X, double Y, int Samples, double Seconds, int Items);
    public record HeatMap(Guid LocationId, double CellM, double? WidthM, double? HeightM, DateTime From, DateTime To, List<HeatCell> Cells);

    /// <summary>Dwell heat map: position samples bucketed into cells; seconds ≈ samples × sampling interval (capped), items = distinct items per cell.</summary>
    public async Task<HeatMap> HeatMapAsync(Guid locationId, DateTime from, DateTime to, double cellM = 1.0, CancellationToken ct = default)
    {
        var loc = await _db.Locations.FindAsync(new object[] { locationId }, ct) ?? throw new NotFoundException("Location");
        cellM = Math.Clamp(cellM, 0.25, 20);
        var fixes = await _db.PositionFixes.Where(f => f.LocationId == locationId && f.At >= from && f.At <= to).OrderBy(f => f.ItemId).ThenBy(f => f.At).ToListAsync(ct);
        var cells = new Dictionary<(int, int), (int samples, double seconds, HashSet<Guid> items)>();
        var cap = StationarySampleEvery.TotalSeconds * 2;
        for (var i = 0; i < fixes.Count; i++)
        {
            var f = fixes[i];
            var next = i + 1 < fixes.Count && fixes[i + 1].ItemId == f.ItemId ? fixes[i + 1].At : (DateTime?)null;
            // Dwell = time until the item's next sample (it stayed here meanwhile), capped so gaps without coverage don't inflate a cell.
            var dt = next.HasValue ? Math.Min((next.Value - f.At).TotalSeconds, cap) : Math.Min(Math.Max(0, (to - f.At).TotalSeconds), StationarySampleEvery.TotalSeconds);
            var key = ((int)Math.Floor(f.X / cellM), (int)Math.Floor(f.Y / cellM));
            if (!cells.TryGetValue(key, out var c)) c = (0, 0, new HashSet<Guid>());
            c.samples++; c.seconds += dt; c.items.Add(f.ItemId); cells[key] = c;
        }
        var b = Bounds(loc);
        return new HeatMap(locationId, cellM, b?.w, b?.h, from, to, cells.Select(kv => new HeatCell(kv.Key.Item1, kv.Key.Item2, Math.Round(kv.Key.Item1 * cellM, 2), Math.Round(kv.Key.Item2 * cellM, 2), kv.Value.samples, Math.Round(kv.Value.seconds), kv.Value.items.Count)).OrderByDescending(c => c.Seconds).ToList());
    }

    public record PathPoint(double X, double Y, double? AccuracyM, DateTime At, Guid LocationId);

    /// <summary>An item's position history (path replay), oldest first.</summary>
    public async Task<List<PathPoint>> HistoryAsync(Guid itemId, DateTime from, DateTime to, int take = 5000, CancellationToken ct = default)
        => await _db.PositionFixes.Where(f => f.ItemId == itemId && f.At >= from && f.At <= to).OrderBy(f => f.At).Take(Math.Clamp(take, 1, 50000)).Select(f => new PathPoint(f.X, f.Y, f.AccuracyM, f.At, f.LocationId)).ToListAsync(ct);

    /// <summary>Deletes samples older than the retention window; returns rows removed.</summary>
    public async Task<int> PruneAsync(TimeSpan retention, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - retention;
        var old = await _db.PositionFixes.Where(f => f.At < cutoff).Take(5000).ToListAsync(ct);
        if (old.Count == 0) return 0;
        _db.PositionFixes.RemoveRange(old); await _db.SaveChangesAsync(ct); return old.Count;
    }

    public record FloorPlan(Guid LocationId, string Location, double? WidthM, double? HeightM, List<AnchorDto> Anchors, List<PositionDto> Items);
    public record AnchorDto(Guid AntennaId, string Device, int Port, double X, double Y);
    public record PositionDto(Guid ItemId, string Name, string Identifier, string? ItemType, double X, double Y, double? AccuracyM, DateTime? At, string? Person);

    public async Task<FloorPlan> FloorPlanAsync(Guid locationId, TimeSpan? maxAge = null, CancellationToken ct = default)
    {
        var loc = await _db.Locations.FindAsync(new object[] { locationId }, ct) ?? throw new NotFoundException("Location");
        var anchors = await _db.Antennas.Include(a => a.Device).Where(a => a.LocationId == locationId && a.X != null && a.Y != null).ToListAsync(ct);
        var since = DateTime.UtcNow - (maxAge ?? TimeSpan.FromHours(12));
        var items = await _db.Items.Include(i => i.ItemType).Where(i => i.PositionLocationId == locationId && i.PositionAt >= since).ToListAsync(ct);
        var b = Bounds(loc);
        return new FloorPlan(loc.Id, loc.Name, b?.w, b?.h,
            anchors.Select(a => new AnchorDto(a.Id, a.Device?.Name ?? "", a.Port, a.X!.Value, a.Y!.Value)).ToList(),
            items.Select(i => new PositionDto(i.Id, i.Name, i.Identifier, i.ItemType?.Name, i.PositionX!.Value, i.PositionY!.Value, i.PositionAccuracyM, i.PositionAt, i.Attributes.GetValueOrDefault("person")?.ToString())).ToList());
    }
}
