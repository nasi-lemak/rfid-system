using Microsoft.EntityFrameworkCore;
using Rfid.Application.Contracts;
using Rfid.Domain;
using Rfid.Domain.Entities;

namespace Rfid.Application.Services;

/// <summary>
/// Zone presence engine for continuously-read tags (active RFID, BLE beacons, UWB, ceiling readers).
/// Tracks open <see cref="PresenceSession"/>s, closes them on dwell timeout and derives occupancy,
/// muster roll-calls and checkpoint timing.
/// </summary>
public class PresenceService
{
    private readonly IAppDb _db;
    private readonly ICurrentContext _ctx;
    private readonly RuleEngine _rules;
    private readonly ILivePublisher _live;

    /// <summary>Default dwell timeout; a location can override with attribute "presenceTimeoutSec".</summary>
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromMinutes(5);

    public PresenceService(IAppDb db, ICurrentContext ctx, RuleEngine rules, ILivePublisher live) { _db = db; _ctx = ctx; _rules = rules; _live = live; }

    /// <summary>Called by ingestion for each resolved (item, location) sighting. Returns true when a new session was opened.</summary>
    public async Task<bool> TrackAsync(Item item, Location location, Guid? deviceId, double? rssi, DateTime at, CancellationToken ct = default)
    {
        var open = await _db.PresenceSessions.Where(p => p.ItemId == item.Id && p.ExitedAt == null).ToListAsync(ct);
        var opened = false;
        foreach (var s in open.Where(s => s.LocationId != location.Id)) s.ExitedAt = at; // seen elsewhere => left previous zone
        var current = open.FirstOrDefault(s => s.LocationId == location.Id);
        if (current == null)
        {
            current = new PresenceSession { TenantId = _ctx.TenantId, ItemId = item.Id, LocationId = location.Id, DeviceId = deviceId, EnteredAt = at, LastSeenAt = at };
            _db.PresenceSessions.Add(current);
            opened = true;
        }
        current.LastSeenAt = at > current.LastSeenAt ? at : current.LastSeenAt;
        current.ReadCount++;
        current.LastRssi = rssi;
        if (rssi.HasValue && (current.PeakRssi == null || rssi > current.PeakRssi)) current.PeakRssi = rssi;
        return opened;
    }

    /// <summary>Closes sessions whose zone has not seen the item within its dwell timeout and emits an exit movement.</summary>
    public async Task<int> SweepAsync(DateTime now, CancellationToken ct = default)
    {
        var horizon = now - DefaultTimeout;
        var stale = await _db.PresenceSessions.Where(p => p.ExitedAt == null && p.LastSeenAt < now.AddSeconds(-5)).ToListAsync(ct);
        if (stale.Count == 0) return 0;
        var locIds = stale.Select(s => s.LocationId).Distinct().ToList();
        var locs = await _db.Locations.Where(l => locIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, ct);
        var closed = 0;
        foreach (var s in stale)
        {
            var loc = locs.GetValueOrDefault(s.LocationId);
            var timeout = loc != null && loc.Attributes.TryGetValue("presenceTimeoutSec", out var t) && double.TryParse(t?.ToString(), out var secs) ? TimeSpan.FromSeconds(secs) : DefaultTimeout;
            if (s.LastSeenAt >= now - timeout) continue;
            s.ExitedAt = s.LastSeenAt + timeout;
            closed++;
            var item = await _db.Items.Include(i => i.ItemType).FirstOrDefaultAsync(i => i.Id == s.ItemId, ct);
            if (item == null || loc == null) continue;
            if (item.CurrentLocationId == loc.Id)
            {
                var parent = loc.ParentId.HasValue ? await _db.Locations.FindAsync(new object[] { loc.ParentId.Value }, ct) : null;
                item.CurrentLocationId = parent?.Id;
                var ev = new ItemEvent { TenantId = _ctx.TenantId, ItemId = item.Id, Type = ItemEventType.Moved, FromLocationId = loc.Id, ToLocationId = parent?.Id, OccurredAt = s.ExitedAt.Value, DeviceId = s.DeviceId, Data = new() { ["direction"] = "Exit", ["zone"] = loc.Name, ["dwellSeconds"] = (s.ExitedAt.Value - s.EnteredAt).TotalSeconds } };
                _db.ItemEvents.Add(ev);
                await _live.PublishEventAsync(ev, ct);
                await _rules.EvaluateAsync(new RuleContext { Event = ev, Item = item, ItemType = item.ItemType, FromLocation = loc, ToLocation = parent }, ct);
            }
        }
        await _db.SaveChangesAsync(ct);
        return closed;
    }

    public record ZoneOccupancy(Guid LocationId, string Location, string Kind, int Present, List<PresentItem> Items);
    public record PresentItem(Guid ItemId, string Name, string Identifier, string? ItemType, DateTime EnteredAt, DateTime LastSeenAt, double DwellMinutes, double? Rssi, string? Person);

    public async Task<List<ZoneOccupancy>> OccupancyAsync(Guid? underLocationId = null, CancellationToken ct = default)
    {
        var open = await _db.PresenceSessions.Where(p => p.ExitedAt == null).ToListAsync(ct);
        var locs = await _db.Locations.ToDictionaryAsync(l => l.Id, ct);
        if (underLocationId.HasValue && locs.TryGetValue(underLocationId.Value, out var root)) open = open.Where(s => locs.TryGetValue(s.LocationId, out var l) && l.Path.StartsWith(root.Path)).ToList();
        var itemIds = open.Select(s => s.ItemId).Distinct().ToList();
        var items = await _db.Items.Include(i => i.ItemType).Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        var now = DateTime.UtcNow;
        return open.GroupBy(s => s.LocationId).Select(g => new ZoneOccupancy(g.Key, locs.GetValueOrDefault(g.Key)?.Name ?? "?", locs.GetValueOrDefault(g.Key)?.Kind.ToString() ?? "", g.Count(),
            g.Select(s => { var it = items.GetValueOrDefault(s.ItemId); return new PresentItem(s.ItemId, it?.Name ?? "?", it?.Identifier ?? "", it?.ItemType?.Name, s.EnteredAt, s.LastSeenAt, (now - s.EnteredAt).TotalMinutes, s.LastRssi, it?.Attributes.GetValueOrDefault("person")?.ToString() ?? it?.Attributes.GetValueOrDefault("athlete")?.ToString()); }).OrderBy(x => x.Name).ToList()))
            .OrderByDescending(z => z.Present).ToList();
    }

    public record MusterReport(int OnSite, int Accounted, int Unaccounted, List<PresentItem> AccountedItems, List<PresentItem> UnaccountedItems);

    /// <summary>Everyone with an open session under the site: accounted if their latest zone is a muster point.</summary>
    public async Task<MusterReport> MusterAsync(Guid siteId, CancellationToken ct = default)
    {
        // Checkpoints (race mats, process gates) are transit points, not places people stay.
        var zones = (await OccupancyAsync(siteId, ct)).Where(z => z.Kind != LocationKind.Checkpoint.ToString()).ToList();
        var accounted = zones.Where(z => z.Kind == LocationKind.MusterPoint.ToString()).SelectMany(z => z.Items).ToList();
        var unaccounted = zones.Where(z => z.Kind != LocationKind.MusterPoint.ToString()).SelectMany(z => z.Items).Where(i => accounted.All(a => a.ItemId != i.ItemId)).ToList();
        return new MusterReport(accounted.Count + unaccounted.Count, accounted.Count, unaccounted.Count, accounted, unaccounted);
    }

    public record TimingRow(Guid ItemId, string Bib, string? Athlete, string? Category, Dictionary<string, DateTime?> Checkpoints, double? ElapsedSeconds, int Rank);

    /// <summary>Checkpoint timing: first read of each tag at each Checkpoint location under the event location, ordered by course order (attribute "order", then name).</summary>
    public async Task<(List<string> checkpoints, List<TimingRow> rows)> TimingAsync(Guid eventLocationId, CancellationToken ct = default)
    {
        var root = await _db.Locations.FindAsync(new object[] { eventLocationId }, ct) ?? throw new NotFoundException("Event location");
        var cps = (await _db.Locations.Where(l => l.Kind == LocationKind.Checkpoint && l.Path.StartsWith(root.Path)).ToListAsync(ct))
            .OrderBy(l => l.Attributes.TryGetValue("order", out var o) && double.TryParse(o?.ToString(), out var d) ? d : double.MaxValue).ThenBy(l => l.Name).ToList();
        if (cps.Count == 0) return (new(), new());
        var cpIds = cps.Select(c => c.Id).ToList();
        var reads = await _db.TagReads.Where(r => r.ItemId != null && r.LocationId != null && cpIds.Contains(r.LocationId.Value)).GroupBy(r => new { r.ItemId, r.LocationId }).Select(g => new { g.Key.ItemId, g.Key.LocationId, First = g.Min(r => r.ReadAt) }).ToListAsync(ct);
        var itemIds = reads.Select(r => r.ItemId!.Value).Distinct().ToList();
        var items = await _db.Items.Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        var rows = new List<TimingRow>();
        foreach (var it in items.Values)
        {
            var times = cps.ToDictionary(c => c.Name, c => reads.FirstOrDefault(r => r.ItemId == it.Id && r.LocationId == c.Id)?.First);
            var start = times[cps[0].Name]; var finish = times[cps[^1].Name];
            rows.Add(new TimingRow(it.Id, it.Identifier, it.Attributes.GetValueOrDefault("athlete")?.ToString(), it.Attributes.GetValueOrDefault("category")?.ToString(), times, start.HasValue && finish.HasValue && cps.Count > 1 && finish > start ? (finish.Value - start.Value).TotalSeconds : null, 0));
        }
        rows = rows.OrderBy(r => r.ElapsedSeconds ?? double.MaxValue).ThenBy(r => r.Bib).ToList();
        for (var i = 0; i < rows.Count; i++) rows[i] = rows[i] with { Rank = rows[i].ElapsedSeconds.HasValue ? i + 1 : 0 };
        return (cps.Select(c => c.Name).ToList(), rows);
    }
}
