using Microsoft.EntityFrameworkCore;
using Rfid.Application.Contracts;
using Rfid.Domain;
using Rfid.Domain.Entities;

namespace Rfid.Application.Services;

/// <summary>
/// Turns raw reads from fixed readers, portals, cabinets and shelves into item presence, zone
/// in/out movements and rule evaluation. Handheld "inventory" scans use the same path with Source=Handheld.
/// </summary>
public class ReadIngestionService
{
    private readonly IAppDb _db;
    private readonly ICurrentContext _ctx;
    private readonly TagResolver _tags;
    private readonly RuleEngine _rules;
    private readonly ILivePublisher _live;
    private readonly PresenceService? _presence;
    private readonly PositionService? _position;

    /// <summary>Repeated reads of the same item at the same location inside this window do not produce events.</summary>
    public TimeSpan Debounce { get; set; } = TimeSpan.FromSeconds(30);

    public ReadIngestionService(IAppDb db, ICurrentContext ctx, TagResolver tags, RuleEngine rules, ILivePublisher live, PresenceService? presence = null, PositionService? position = null)
    {
        _db = db; _ctx = ctx; _tags = tags; _rules = rules; _live = live; _presence = presence; _position = position;
    }

    public async Task<IngestResult> IngestAsync(ReadBatchRequest batch, ReadSource source, CancellationToken ct = default)
    {
        var result = new IngestResult { Received = batch.Reads.Count };
        var deviceId = batch.DeviceId ?? _ctx.DeviceId;
        var batchKey = string.IsNullOrWhiteSpace(batch.BatchId) ? null : $"{deviceId?.ToString("N") ?? "-"}:{batch.BatchId.Trim()}";
        if (batchKey != null)
        {
            var seen = await _db.IdempotencyKeys.FirstOrDefaultAsync(k => k.Scope == IdempotencyScopes.ReadBatch && k.Key == batchKey, ct);
            if (seen != null)
            {
                var prior = seen.Response == null ? null : System.Text.Json.JsonSerializer.Deserialize<IngestResult>(seen.Response);
                return prior == null ? new IngestResult { Received = batch.Reads.Count, Duplicate = true } : new IngestResult { Received = prior.Received, Resolved = prior.Resolved, Unknown = prior.Unknown, Events = prior.Events, Alerts = prior.Alerts, Duplicate = true };
            }
        }
        Device? device = deviceId.HasValue
            ? await _db.Devices.Include(d => d.Antennas).ThenInclude(a => a.Location).FirstOrDefaultAsync(d => d.Id == deviceId, ct)
            : null;
        if (device != null) device.LastSeenAt = DateTime.UtcNow;

        var tagMap = await _tags.ResolveAsync(batch.Reads.Select(r => r.Epc), ct);
        var explicitLocIds = batch.Reads.Where(r => r.LocationId.HasValue).Select(r => r.LocationId!.Value).Distinct().ToList();
        var explicitLocs = explicitLocIds.Count == 0 ? new() : await _db.Locations.Where(l => explicitLocIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, ct);
        var live = new List<LiveRead>();
        var seenThisBatch = new HashSet<(Guid, Guid?)>();
        var sightings = new Dictionary<Guid, (Item item, List<PositionService.Sighting> list, DateTime at)>();

        // Keep the latest read per EPC+antenna to avoid hammering the DB with duplicates from a single batch,
        // and process the strongest read of each EPC first so that, when several antennas/zones see the same
        // tag, the best-RSSI zone wins ("nearest reader" location resolution for active/BLE/ceiling readers).
        foreach (var r in batch.Reads.GroupBy(x => (TagResolver.Normalize(x.Epc), x.AntennaPort)).Select(g => g.OrderByDescending(x => x.ReadAt).First())
                     .OrderBy(x => TagResolver.Normalize(x.Epc)).ThenByDescending(x => x.Rssi ?? double.MinValue))
        {
            var epc = TagResolver.Normalize(r.Epc);
            var at = r.ReadAt?.ToUniversalTime() ?? DateTime.UtcNow;
            var antenna = device?.Antennas.FirstOrDefault(a => a.Port == (r.AntennaPort ?? 1)) ?? device?.Antennas.FirstOrDefault();
            var location = r.LocationId.HasValue ? explicitLocs.GetValueOrDefault(r.LocationId.Value) : antenna?.Location;
            var direction = r.LocationId.HasValue ? AntennaDirection.None : antenna?.Direction ?? AntennaDirection.None;
            var tag = tagMap.GetValueOrDefault(epc);
            var item = tag?.Item;

            _db.TagReads.Add(new TagRead
            {
                TenantId = _ctx.TenantId, Epc = epc, Tid = r.Tid, ItemId = item?.Id, DeviceId = device?.Id,
                AntennaPort = r.AntennaPort, Rssi = r.Rssi, ReadAt = at, LocationId = location?.Id, Source = source, SessionId = batch.SessionId,
            });
            live.Add(new LiveRead(epc, item?.Id, item?.Name, device?.Id, location?.Id, r.Rssi, at));

            if (item == null) { result.Unknown++; continue; }
            result.Resolved++;
            if (antenna?.X != null && (r.Rssi.HasValue || r.RangeM.HasValue))
            {
                if (!sightings.TryGetValue(item.Id, out var sg)) sightings[item.Id] = sg = (item, new(), at);
                sg.list.Add(new PositionService.Sighting(antenna, r.Rssi, r.RangeM));
            }
            if (seenThisBatch.Any(x => x.Item1 == item.Id)) continue; // a stronger antenna already placed this item
            seenThisBatch.Add((item.Id, location?.Id));
            if (location != null && _presence != null && direction != AntennaDirection.Out) await _presence.TrackAsync(item, location, device?.Id, r.Rssi, at, ct);

            var debounced = item.LastSeenAt.HasValue && item.LastSeenLocationId == location?.Id && at - item.LastSeenAt.Value < Debounce
                            && direction == AntennaDirection.None;
            item.LastSeenAt = at; item.LastSeenDeviceId = device?.Id;
            if (location != null) item.LastSeenLocationId = location.Id;
            if (item.Status == ItemStatus.Missing) item.Status = ItemStatus.Active;
            if (debounced || location == null) continue;

            var prevLoc = item.CurrentLocationId;
            Location? from = item.CurrentLocation;
            ItemEvent ev;
            switch (direction)
            {
                case AntennaDirection.Out:
                {
                    // Leaving the zone: item is now at the zone's parent (or unknown).
                    var parent = location.ParentId.HasValue ? await _db.Locations.FindAsync(new object[] { location.ParentId.Value }, ct) : null;
                    item.CurrentLocationId = parent?.Id; item.CurrentLocation = parent;
                    ev = new ItemEvent { ItemId = item.Id, Type = ItemEventType.Moved, FromLocationId = location.Id, ToLocationId = parent?.Id, Data = new() { ["direction"] = "Out", ["zone"] = location.Name, ["rssi"] = r.Rssi } };
                    from = location;
                    break;
                }
                case AntennaDirection.In:
                    item.CurrentLocationId = location.Id; item.CurrentLocation = location;
                    ev = new ItemEvent { ItemId = item.Id, Type = ItemEventType.Moved, FromLocationId = prevLoc, ToLocationId = location.Id, Data = new() { ["direction"] = "In", ["zone"] = location.Name, ["rssi"] = r.Rssi } };
                    break;
                default:
                    if (prevLoc != location.Id)
                    {
                        item.CurrentLocationId = location.Id; item.CurrentLocation = location;
                        ev = new ItemEvent { ItemId = item.Id, Type = ItemEventType.Moved, FromLocationId = prevLoc, ToLocationId = location.Id, Data = new() { ["direction"] = "Seen", ["rssi"] = r.Rssi } };
                    }
                    else
                        ev = new ItemEvent { ItemId = item.Id, Type = ItemEventType.Seen, FromLocationId = prevLoc, ToLocationId = location.Id, Data = new() { ["rssi"] = r.Rssi } };
                    break;
            }
            ev.TenantId = _ctx.TenantId; ev.DeviceId = device?.Id; ev.OccurredAt = at;
            _db.ItemEvents.Add(ev);
            result.Events++;
            await _live.PublishEventAsync(ev, ct);
            var alerts = await _rules.EvaluateAsync(new RuleContext { Event = ev, Item = item, ItemType = item.ItemType, FromLocation = from, ToLocation = direction == AntennaDirection.Out ? item.CurrentLocation : location }, ct);
            result.Alerts += alerts.Count;
        }

        if (_position != null) foreach (var sg in sightings.Values) _position.Update(sg.item, sg.list, sg.at);
        // The idempotency key commits in the same unit of work as the reads/events it guards.
        if (batchKey != null) _db.IdempotencyKeys.Add(new IdempotencyKey { TenantId = _ctx.TenantId, Scope = IdempotencyScopes.ReadBatch, Key = batchKey, Response = System.Text.Json.JsonSerializer.Serialize(result) });
        await _db.SaveChangesAsync(ct);
        await _live.PublishReadsAsync(live, ct);
        return result;
    }
}
