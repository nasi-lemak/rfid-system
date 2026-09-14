using Microsoft.EntityFrameworkCore;
using Rfid.Application.Contracts;
using Rfid.Application.Positioning;
using Rfid.Domain.Entities;

namespace Rfid.Application.Services;

/// <summary>Turns multi-antenna sightings of one tag into an x/y estimate inside a zone (antennas act as anchors).</summary>
public class PositionService
{
    private readonly IAppDb _db;
    public PositionService(IAppDb db) => _db = db;

    /// <summary>Estimates and stores the item's position from the antennas (with coordinates, in one location) that saw it.</summary>
    public Estimate? Update(Item item, IEnumerable<(Antenna antenna, double rssi)> sightings, DateTime at)
    {
        var usable = sightings.Where(s => s.antenna.X.HasValue && s.antenna.Y.HasValue && s.antenna.LocationId.HasValue).ToList();
        if (usable.Count == 0) return null;
        var zone = usable.GroupBy(s => s.antenna.LocationId!.Value).OrderByDescending(g => g.Count()).First();
        var anchors = zone.GroupBy(s => s.antenna.Id).Select(g => { var best = g.OrderByDescending(s => s.rssi).First(); return new Anchor(best.antenna.X!.Value, best.antenna.Y!.Value, Trilateration.RssiToDistance(best.rssi, best.antenna.RssiAt1m ?? -45, best.antenna.PathLossExponent ?? 2.2)); }).ToList();
        var est = Trilateration.Estimate(anchors, Bounds(zone.First().antenna.Location));
        if (est == null) return null;
        item.PositionX = est.X; item.PositionY = est.Y; item.PositionLocationId = zone.Key; item.PositionAt = at; item.PositionAccuracyM = est.AccuracyM;
        return est;
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
