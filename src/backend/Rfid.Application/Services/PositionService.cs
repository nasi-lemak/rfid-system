using Microsoft.EntityFrameworkCore;
using Rfid.Application.Contracts;
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
        var (x, y, acc) = _smoother?.Smooth(item.Id, zone.Key, est.X, est.Y, est.AccuracyM, at) ?? (est.X, est.Y, est.AccuracyM);
        item.PositionX = x; item.PositionY = y; item.PositionLocationId = zone.Key; item.PositionAt = at; item.PositionAccuracyM = acc;
        return est with { X = x, Y = y, AccuracyM = acc };
    }

    public static (double w, double h)? Bounds(Location? loc)
    {
        if (loc == null) return null;
        var w = loc.Attributes.TryGetValue("widthM", out var wv) && double.TryParse(wv?.ToString(), out var wd) ? wd : (double?)null;
        var h = loc.Attributes.TryGetValue("heightM", out var hv) && double.TryParse(hv?.ToString(), out var hd) ? hd : (double?)null;
        return w.HasValue && h.HasValue ? (w.Value, h.Value) : null;
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
