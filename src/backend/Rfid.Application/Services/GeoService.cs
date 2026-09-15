using Microsoft.EntityFrameworkCore;
using Rfid.Application.Contracts;
using Rfid.Domain;
using Rfid.Domain.Entities;

namespace Rfid.Application.Services;

public class GpsBatchRequest
{
    public Guid? DeviceId { get; set; }
    public List<GpsReadRequest> Fixes { get; set; } = new();
}

public class GpsReadRequest
{
    public Guid? ItemId { get; set; }
    public string? Epc { get; set; }
    public string? Identifier { get; set; }
    public Guid? DeviceId { get; set; }
    public double Lat { get; set; }
    public double Lng { get; set; }
    public double? SpeedKph { get; set; }
    public double? HeadingDeg { get; set; }
    public double? AccuracyM { get; set; }
    public DateTime? At { get; set; }
}

public class GpsIngestResult { public int Received { get; set; } public int Resolved { get; set; } public int Unknown { get; set; } public int Fixes { get; set; } public int Transitions { get; set; } public int Alerts { get; set; } }

/// <summary>Outdoor asset tracking: GPS fixes from telematics units / handhelds, geofence enter/exit/dwell detection, map data.</summary>
public class GeoService
{
    private readonly IAppDb _db; private readonly ICurrentContext _ctx; private readonly RuleEngine? _rules; private readonly ILivePublisher? _live; private readonly NotificationService? _notifications;
    public double MinMoveM { get; set; } = 10;
    public TimeSpan StationarySampleEvery { get; set; } = TimeSpan.FromMinutes(5);
    public GeoService(IAppDb db, ICurrentContext ctx, RuleEngine? rules = null, ILivePublisher? live = null, NotificationService? notifications = null) { _db = db; _ctx = ctx; _rules = rules; _live = live; _notifications = notifications; }

    // ── Geometry ──
    public static double DistanceM(double lat1, double lng1, double lat2, double lng2)
    {
        const double R = 6371000; double Rad(double d) => d * Math.PI / 180;
        var dLat = Rad(lat2 - lat1); var dLng = Rad(lng2 - lng1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return 2 * R * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    public static bool Contains(GeoFence f, double lat, double lng)
    {
        if (f.Kind == GeoFenceKind.Circle) return f.CenterLat.HasValue && f.CenterLng.HasValue && f.RadiusM.HasValue && DistanceM(lat, lng, f.CenterLat.Value, f.CenterLng.Value) <= f.RadiusM.Value;
        var pts = f.Points; if (pts.Count < 3) return false;
        var inside = false;
        for (int i = 0, j = pts.Count - 1; i < pts.Count; j = i++)
        {
            var (yi, xi) = (pts[i].Lat, pts[i].Lng); var (yj, xj) = (pts[j].Lat, pts[j].Lng);
            if ((yi > lat) != (yj > lat) && lng < (xj - xi) * (lat - yi) / (yj - yi) + xi) inside = !inside;
        }
        return inside;
    }

    // ── Ingestion ──
    public async Task<GpsIngestResult> IngestAsync(GpsBatchRequest batch, CancellationToken ct = default)
    {
        var result = new GpsIngestResult { Received = batch.Fixes.Count };
        var fences = await _db.GeoFences.Where(f => f.Enabled).ToListAsync(ct);
        foreach (var r in batch.Fixes.OrderBy(f => f.At ?? DateTime.UtcNow))
        {
            var item = await ResolveAsync(r, batch.DeviceId, ct);
            if (item == null) { result.Unknown++; continue; }
            result.Resolved++;
            var at = r.At?.ToUniversalTime() ?? DateTime.UtcNow;
            var moved = item.Latitude == null || DistanceM(item.Latitude.Value, item.Longitude!.Value, r.Lat, r.Lng) >= MinMoveM || item.GpsAt == null || at - item.GpsAt.Value >= StationarySampleEvery;
            if (moved) { _db.GpsFixes.Add(new GpsFix { TenantId = item.TenantId, ItemId = item.Id, Lat = r.Lat, Lng = r.Lng, SpeedKph = r.SpeedKph, HeadingDeg = r.HeadingDeg, AccuracyM = r.AccuracyM, At = at, DeviceId = r.DeviceId ?? batch.DeviceId }); result.Fixes++; }
            item.Latitude = r.Lat; item.Longitude = r.Lng; item.GpsAt = at; item.GpsSpeedKph = r.SpeedKph; item.LastSeenAt = at;
            var (transitions, alerts) = await EvaluateFencesAsync(item, fences, r.Lat, r.Lng, at, r.DeviceId ?? batch.DeviceId, ct);
            result.Transitions += transitions; result.Alerts += alerts;
        }
        await _db.SaveChangesAsync(ct);
        return result;
    }

    private async Task<Item?> ResolveAsync(GpsReadRequest r, Guid? batchDevice, CancellationToken ct)
    {
        if (r.ItemId.HasValue) return await _db.Items.Include(i => i.ItemType).Include(i => i.CurrentLocation).FirstOrDefaultAsync(i => i.Id == r.ItemId, ct);
        if (!string.IsNullOrWhiteSpace(r.Epc)) { var epc = TagResolver.Normalize(r.Epc); var tag = await _db.Tags.Include(t => t.Item).ThenInclude(i => i!.ItemType).Include(t => t.Item).ThenInclude(i => i!.CurrentLocation).FirstOrDefaultAsync(t => t.Epc == epc, ct); if (tag?.Item != null) return tag.Item; }
        if (!string.IsNullOrWhiteSpace(r.Identifier)) { var it = await _db.Items.Include(i => i.ItemType).Include(i => i.CurrentLocation).FirstOrDefaultAsync(i => i.Identifier == r.Identifier, ct); if (it != null) return it; }
        var devId = r.DeviceId ?? batchDevice;
        if (devId.HasValue)
        {
            var tracked = await _db.Devices.Where(d => d.Id == devId).Select(d => d.TrackedItemId).FirstOrDefaultAsync(ct);
            if (tracked.HasValue) return await _db.Items.Include(i => i.ItemType).Include(i => i.CurrentLocation).FirstOrDefaultAsync(i => i.Id == tracked, ct);
        }
        return null;
    }

    private async Task<(int transitions, int alerts)> EvaluateFencesAsync(Item item, List<GeoFence> fences, double lat, double lng, DateTime at, Guid? deviceId, CancellationToken ct)
    {
        int transitions = 0, alerts = 0;
        foreach (var f in fences.Where(f => f.ItemTypeId == null || f.ItemTypeId == item.ItemTypeId))
        {
            var inside = Contains(f, lat, lng);
            var state = _db.GeoFenceStates.Local.FirstOrDefault(s => s.FenceId == f.Id && s.ItemId == item.Id) ?? await _db.GeoFenceStates.FirstOrDefaultAsync(s => s.FenceId == f.Id && s.ItemId == item.Id, ct);
            // First sighting establishes the baseline without firing enter/exit (avoids an alert storm when tracking starts).
            if (state == null) { _db.GeoFenceStates.Add(new GeoFenceState { TenantId = item.TenantId, FenceId = f.Id, ItemId = item.Id, Inside = inside, Since = at }); continue; }
            else if (state.Inside == inside) continue;
            else { state.Inside = inside; state.Since = at; state.DwellAlerted = false; }
            transitions++;
            var ev = new ItemEvent
            {
                TenantId = item.TenantId, ItemId = item.Id, Type = inside ? ItemEventType.GeofenceEntered : ItemEventType.GeofenceExited, DeviceId = deviceId, OccurredAt = at,
                FromLocationId = item.CurrentLocationId, ToLocationId = inside ? f.LocationId ?? item.CurrentLocationId : item.CurrentLocationId,
                Data = new() { ["fence"] = f.Name, ["fenceId"] = f.Id, ["lat"] = lat, ["lng"] = lng },
            };
            Location? to = null;
            if (inside && f.LocationId.HasValue && item.CurrentLocationId != f.LocationId)
            {
                to = await _db.Locations.FirstOrDefaultAsync(l => l.Id == f.LocationId, ct);
                if (to != null) { ev.Data["movedTo"] = to.Name; item.CurrentLocationId = to.Id; item.CurrentLocation = to; item.LastSeenLocationId = to.Id; }
            }
            _db.ItemEvents.Add(ev);
            if (_live != null) await _live.PublishEventAsync(ev, ct);
            var fire = f.Trigger == GeoFenceTrigger.Both || (inside && f.Trigger == GeoFenceTrigger.Enter) || (!inside && f.Trigger == GeoFenceTrigger.Exit);
            if (fire)
            {
                var alert = new Alert { TenantId = item.TenantId, ItemId = item.Id, LocationId = item.CurrentLocationId, Severity = f.Severity, Source = "geofence", DeviceId = deviceId, RaisedAt = at, Message = $"{item.Name} ({item.Identifier}) {(inside ? "entered" : "left")} {f.Name}" };
                _db.Alerts.Add(alert); alerts++;
                if (_live != null) await _live.PublishAlertAsync(alert, ct);
                if (_notifications != null) await _notifications.OnAlertRaisedAsync(alert, null, ct);
            }
            if (_rules != null) alerts += (await _rules.EvaluateAsync(new RuleContext { Event = ev, Item = item, ItemType = item.ItemType, FromLocation = item.CurrentLocation, ToLocation = to ?? item.CurrentLocation }, ct)).Count;
        }
        return (transitions, alerts);
    }

    /// <summary>Raises dwell alerts for items that have stayed inside a fence longer than its MaxDwellMinutes.</summary>
    public async Task<int> SweepDwellAsync(DateTime now, CancellationToken ct = default)
    {
        var fences = await _db.GeoFences.Where(f => f.Enabled && f.MaxDwellMinutes != null).ToListAsync(ct);
        if (fences.Count == 0) return 0;
        var ids = fences.Select(f => f.Id).ToList(); var raised = 0;
        var states = await _db.GeoFenceStates.Where(s => ids.Contains(s.FenceId) && s.Inside && !s.DwellAlerted).ToListAsync(ct);
        foreach (var s in states)
        {
            var f = fences.First(x => x.Id == s.FenceId);
            if (now - s.Since < TimeSpan.FromMinutes(f.MaxDwellMinutes!.Value)) continue;
            var item = await _db.Items.FirstOrDefaultAsync(i => i.Id == s.ItemId, ct); if (item == null) continue;
            s.DwellAlerted = true; raised++;
            var alert = new Alert { TenantId = item.TenantId, ItemId = item.Id, LocationId = item.CurrentLocationId, Severity = f.Severity, Source = "geofence", RaisedAt = now, Message = $"{item.Name} ({item.Identifier}) has been in {f.Name} for {(int)(now - s.Since).TotalMinutes} min (limit {f.MaxDwellMinutes})" };
            _db.Alerts.Add(alert);
            if (_notifications != null) await _notifications.OnAlertRaisedAsync(alert, null, ct);
        }
        if (raised > 0) await _db.SaveChangesAsync(ct);
        return raised;
    }

    // ── Map data ──
    public record MapItem(Guid ItemId, string Name, string Identifier, string? ItemType, double Lat, double Lng, DateTime? At, double? SpeedKph, string? Location, List<string> Fences);
    public record FenceDto(Guid Id, string Name, GeoFenceKind Kind, double? CenterLat, double? CenterLng, double? RadiusM, List<GeoPoint> Points, Guid? LocationId, string? Location, GeoFenceTrigger Trigger, Severity Severity, bool Enabled, string? Color, int? MaxDwellMinutes, Guid? ItemTypeId, int Inside);
    public record MapData(List<MapItem> Items, List<FenceDto> Fences);

    public async Task<MapData> MapAsync(TimeSpan? maxAge = null, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - (maxAge ?? TimeSpan.FromDays(7));
        var items = await _db.Items.Include(i => i.ItemType).Include(i => i.CurrentLocation).Where(i => i.Latitude != null && i.GpsAt >= cutoff && i.Status != ItemStatus.Disposed).OrderBy(i => i.Name).Take(2000).ToListAsync(ct);
        var fences = await _db.GeoFences.OrderBy(f => f.Name).ToListAsync(ct);
        var locNames = await _db.Locations.Where(l => fences.Select(f => f.LocationId).Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.Name, ct);
        var inside = await _db.GeoFenceStates.Where(s => s.Inside).GroupBy(s => s.FenceId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return new MapData(
            items.Select(i => new MapItem(i.Id, i.Name, i.Identifier, i.ItemType?.Name, i.Latitude!.Value, i.Longitude!.Value, i.GpsAt, i.GpsSpeedKph, i.CurrentLocation?.Name, fences.Where(f => f.Enabled && Contains(f, i.Latitude.Value, i.Longitude.Value)).Select(f => f.Name).ToList())).ToList(),
            fences.Select(f => new FenceDto(f.Id, f.Name, f.Kind, f.CenterLat, f.CenterLng, f.RadiusM, f.Points, f.LocationId, f.LocationId.HasValue ? locNames.GetValueOrDefault(f.LocationId.Value) : null, f.Trigger, f.Severity, f.Enabled, f.Color, f.MaxDwellMinutes, f.ItemTypeId, inside.GetValueOrDefault(f.Id))).ToList());
    }

    public record TrackPoint(double Lat, double Lng, DateTime At, double? SpeedKph);
    public async Task<List<TrackPoint>> TrackAsync(Guid itemId, DateTime from, DateTime to, int take = 5000, CancellationToken ct = default)
        => await _db.GpsFixes.Where(f => f.ItemId == itemId && f.At >= from && f.At <= to).OrderBy(f => f.At).Take(Math.Clamp(take, 1, 50000)).Select(f => new TrackPoint(f.Lat, f.Lng, f.At, f.SpeedKph)).ToListAsync(ct);
}
