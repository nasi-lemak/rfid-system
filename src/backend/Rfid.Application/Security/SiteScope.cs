using Rfid.Application.Contracts;
using Rfid.Domain.Entities;

namespace Rfid.Application.Security;

/// <summary>
/// One site-scoping rule for every aggregate query (reports, analytics, dashboards): a restricted principal sees
/// locations inside their site subtrees, items currently there, and events/alerts/reads/operations/sessions
/// that touch those locations or items. Unrestricted principals get the tenant-wide query unchanged.
/// </summary>
public sealed class SiteScope
{
    private readonly IAppDb? _db;
    public List<string>? Paths { get; }
    public bool Restricted => Paths != null;
    private SiteScope(List<string>? paths, IAppDb? db) { Paths = paths; _db = db; }

    public static readonly SiteScope All = new(null, null);
    public static SiteScope For(List<string>? paths, IAppDb db) => paths == null ? All : new(paths, db);
    public static async Task<SiteScope> ForAsync(ISiteAccess access, IAppDb db, CancellationToken ct = default) => For(await access.AllowedPathsAsync(ct), db);

    private IQueryable<Guid> LocIds => SiteAccess.Filter(_db!.Locations, Paths).Select(l => l.Id);
    private IQueryable<Guid> ItemIds => SiteAccess.Filter(_db!.Items, Paths).Select(i => i.Id);

    public IQueryable<Item> Items(IQueryable<Item> q) => Restricted ? SiteAccess.Filter(q, Paths) : q;
    public IQueryable<Location> Locations(IQueryable<Location> q) => Restricted ? SiteAccess.Filter(q, Paths) : q;
    public IQueryable<ItemEvent> Events(IQueryable<ItemEvent> q)
    {
        if (!Restricted) return q;
        var locs = LocIds; var items = ItemIds;
        return q.Where(e => (e.ToLocationId != null && locs.Contains(e.ToLocationId.Value)) || (e.FromLocationId != null && locs.Contains(e.FromLocationId.Value)) || items.Contains(e.ItemId));
    }
    public IQueryable<Alert> Alerts(IQueryable<Alert> q)
    {
        if (!Restricted) return q;
        var locs = LocIds; var items = ItemIds;
        return q.Where(a => (a.LocationId != null && locs.Contains(a.LocationId.Value)) || (a.LocationId == null && a.ItemId != null && items.Contains(a.ItemId.Value)));
    }
    public IQueryable<TagRead> Reads(IQueryable<TagRead> q) { if (!Restricted) return q; var locs = LocIds; return q.Where(r => r.LocationId != null && locs.Contains(r.LocationId.Value)); }
    public IQueryable<Operation> Operations(IQueryable<Operation> q) { if (!Restricted) return q; var locs = LocIds; return q.Where(o => (o.ToLocationId != null && locs.Contains(o.ToLocationId.Value)) || (o.FromLocationId != null && locs.Contains(o.FromLocationId.Value))); }
    public IQueryable<Stocktake> Stocktakes(IQueryable<Stocktake> q) { if (!Restricted) return q; var locs = LocIds; return q.Where(s => locs.Contains(s.LocationId)); }
    public IQueryable<PresenceSession> Sessions(IQueryable<PresenceSession> q) { if (!Restricted) return q; var locs = LocIds; return q.Where(s => locs.Contains(s.LocationId)); }
    public IQueryable<Device> Devices(IQueryable<Device> q) { if (!Restricted) return q; var locs = LocIds; return q.Where(d => d.SiteLocationId != null && locs.Contains(d.SiteLocationId.Value)); }
    public IQueryable<PrintJob> PrintJobs(IQueryable<PrintJob> q) { if (!Restricted) return q; var items = ItemIds; return q.Where(j => items.Contains(j.ItemId)); }
}
