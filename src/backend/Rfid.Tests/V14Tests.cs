using Microsoft.EntityFrameworkCore;
using Rfid.Application.Cluster;
using Rfid.Application.Contracts;
using Rfid.Application.Positioning;
using Rfid.Application.Security;
using Rfid.Application.Services;
using Rfid.Domain;
using Rfid.Domain.Entities;
using Xunit;

namespace Rfid.Tests;

public class ClusterLeaseTests
{
    [Fact]
    public async Task Lease_is_exclusive_renewable_and_expires()
    {
        var h = new TestHost(); var svc = new LeaseService(h.Db);
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.True(await svc.TryAcquireAsync("job:print", "node-a", TimeSpan.FromSeconds(30), t0));
        Assert.False(await svc.TryAcquireAsync("job:print", "node-b", TimeSpan.FromSeconds(30), t0.AddSeconds(10)));
        Assert.True(await svc.TryAcquireAsync("job:print", "node-a", TimeSpan.FromSeconds(30), t0.AddSeconds(20)));   // renew
        Assert.False(await svc.TryAcquireAsync("job:print", "node-b", TimeSpan.FromSeconds(30), t0.AddSeconds(45))); // still held (renewed to t0+50)
        Assert.True(await svc.TryAcquireAsync("job:print", "node-b", TimeSpan.FromSeconds(30), t0.AddSeconds(51)));  // expired → takeover
        var lease = (await svc.ListAsync()).Single();
        Assert.Equal("node-b", lease.Owner); Assert.Equal(t0.AddSeconds(51), lease.AcquiredAt);
        await svc.ReleaseAsync("job:print", "node-b", t0.AddSeconds(51));
        Assert.True(await svc.TryAcquireAsync("job:print", "node-a", TimeSpan.FromSeconds(30), t0.AddSeconds(52)));
        Assert.True(await svc.TryAcquireAsync("llrp:reader-1", "node-a", TimeSpan.FromSeconds(30), t0));
        Assert.Equal(2, (await svc.ListAsync()).Count);
    }

    [Fact]
    public void Node_id_is_stable_for_the_process()
    {
        Assert.Equal(ClusterNode.Id, ClusterNode.Id);
        Assert.StartsWith(Environment.MachineName, ClusterNode.Id);
    }
}

public class PositionHistoryTests
{
    private static async Task<(TestHost h, Item item, Tag tag, Device gw, Location hall, ReadIngestionService ingest)> ArenaAsync(IPositionSmoother? smoother)
    {
        var h = new TestHost();
        var badge = await h.InstallTypeAsync("people-presence", "BADGE");
        var hall = h.Loc(LocationKind.Zone, "Hall", null, new() { ["widthM"] = 20, ["heightM"] = 10 });
        var (item, tag) = h.Item(badge, "B1", hall);
        var gw = new Device { TenantId = h.Ctx.TenantId, Name = "UWB anchors", Kind = DeviceKind.Fixed };
        foreach (var (p, x, y) in new[] { (1, 0.0, 0.0), (2, 20.0, 0.0), (3, 0.0, 10.0), (4, 20.0, 10.0) }) gw.Antennas.Add(new Antenna { TenantId = h.Ctx.TenantId, Port = p, LocationId = hall.Id, X = x, Y = y });
        h.Db.Devices.Add(gw); await h.SaveAsync();
        var ingest = new ReadIngestionService(h.Db, h.Ctx, new TagResolver(h.Db), h.Rules, new NullLivePublisher(), null, new PositionService(h.Db, smoother));
        return (h, item, tag, gw, hall, ingest);
    }

    private static ReadBatchRequest At(Device gw, Tag tag, double px, double py, DateTime t)
    {
        double D(double ax, double ay) => Math.Sqrt((px - ax) * (px - ax) + (py - ay) * (py - ay));
        return new ReadBatchRequest { DeviceId = gw.Id, Reads = { new() { Epc = tag.Epc, AntennaPort = 1, RangeM = D(0, 0), ReadAt = t }, new() { Epc = tag.Epc, AntennaPort = 2, RangeM = D(20, 0), ReadAt = t }, new() { Epc = tag.Epc, AntennaPort = 3, RangeM = D(0, 10), ReadAt = t }, new() { Epc = tag.Epc, AntennaPort = 4, RangeM = D(20, 10), ReadAt = t } } };
    }

    [Fact]
    public async Task Persistent_smoother_keeps_track_state_on_the_item_and_survives_a_new_instance()
    {
        var (h, item, tag, gw, hall, _) = await ArenaAsync(null);
        var smoother1 = new PersistentPositionSmoother(); var t0 = DateTime.UtcNow;
        var svc1 = new PositionService(h.Db, smoother1);
        var ants = gw.Antennas.ToList();
        IEnumerable<PositionService.Sighting> S(double px, double py) => ants.Select(a => new PositionService.Sighting(a, null, Math.Sqrt((px - a.X!.Value) * (px - a.X.Value) + (py - a.Y!.Value) * (py - a.Y.Value))));
        for (var i = 0; i < 5; i++) svc1.Update(item, S(2 + i * 0.5, 5), t0.AddSeconds(i));
        Assert.False(string.IsNullOrEmpty(item.PositionTrack));
        var state = System.Text.Json.JsonSerializer.Deserialize<KalmanTrack.State>(item.PositionTrack!, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        Assert.Equal(hall.Id, state.Zone); Assert.True(state.Vx > 0.1, $"vx={state.Vx}"); // learned the eastward motion
        // A different node (new smoother instance) continues the same track: the prediction carries the velocity forward.
        var svc2 = new PositionService(h.Db, new PersistentPositionSmoother());
        var est = svc2.Update(item, S(4.5, 5), t0.AddSeconds(5))!;
        Assert.InRange(est.X, 4.0, 5.0); Assert.InRange(est.Y, 4.5, 5.5);
        // After a long gap the track is restarted rather than extrapolated.
        var far = svc2.Update(item, S(15, 2), t0.AddMinutes(30))!;
        Assert.InRange(far.X, 14.9, 15.1); Assert.InRange(far.Y, 1.9, 2.1);
    }

    [Fact]
    public async Task Fixes_are_sampled_on_movement_and_feed_history_and_heat_map()
    {
        var (h, item, tag, gw, hall, ingest) = await ArenaAsync(new PersistentPositionSmoother());
        var t0 = DateTime.UtcNow.AddHours(-2);
        // Stationary for 10 reads 1 s apart → only the first fix is recorded (below MinMoveM and StationarySampleEvery).
        for (var i = 0; i < 10; i++) await ingest.IngestAsync(At(gw, tag, 3, 3, t0.AddSeconds(i)), ReadSource.Fixed);
        Assert.Equal(1, await h.Db.PositionFixes.CountAsync());
        // Walk across the hall 2 m per step → each step is a fix.
        for (var i = 1; i <= 6; i++) await ingest.IngestAsync(At(gw, tag, 3 + i * 2, 3 + i * 0.5, t0.AddMinutes(1).AddSeconds(i * 2)), ReadSource.Fixed);
        Assert.Equal(7, await h.Db.PositionFixes.CountAsync());
        var svc = new PositionService(h.Db);
        var path = await svc.HistoryAsync(item.Id, t0, t0.AddHours(1));
        Assert.Equal(7, path.Count); Assert.True(path.First().At < path.Last().At);
        Assert.InRange(path.Last().X, 14, 16);
        var heat = await svc.HeatMapAsync(hall.Id, t0, t0.AddHours(1), cellM: 2);
        Assert.Equal(20, heat.WidthM); Assert.Equal(10, heat.HeightM); Assert.Equal(2, heat.CellM);
        Assert.NotEmpty(heat.Cells);
        var hottest = heat.Cells.OrderByDescending(c => c.Seconds).First();
        Assert.Equal(1, hottest.Ix); Assert.Equal(1, hottest.Iy); // the ~60 s spent at (3,3) dominates
        Assert.True(hottest.Seconds >= 50, $"dwell={hottest.Seconds}");
        Assert.All(heat.Cells, c => Assert.Equal(1, c.Items));
        // Retention prune drops everything older than the retention window.
        Assert.Equal(0, await svc.PruneAsync(TimeSpan.FromDays(1)));
        var old = new PositionFix { TenantId = h.Ctx.TenantId, ItemId = item.Id, LocationId = hall.Id, X = 1, Y = 1, At = DateTime.UtcNow.AddDays(-40) };
        h.Db.PositionFixes.Add(old); await h.SaveAsync();
        Assert.Equal(1, await svc.PruneAsync(TimeSpan.FromDays(30)));
    }
}

public class SsoAndSiteAccessTests
{
    private static SsoOptions Opts() => new() { Enabled = true, Authority = "https://idp.example.com", ClientId = "rfid", TenantCode = "demo", RoleMap = new() { ["rfid-admins"] = "Admin", ["rfid-ops"] = "Operator" } };

    [Fact]
    public void Role_mapping_takes_the_highest_matching_group()
    {
        var o = Opts();
        Assert.Equal(UserRole.Viewer, SsoUserMapper.MapRole(o, Array.Empty<string>()));
        Assert.Equal(UserRole.Operator, SsoUserMapper.MapRole(o, new[] { "staff", "rfid-ops" }));
        Assert.Equal(UserRole.Admin, SsoUserMapper.MapRole(o, new[] { "rfid-ops", "rfid-admins" }));
    }

    [Fact]
    public async Task External_identity_is_provisioned_then_matched_by_subject_and_by_email()
    {
        var h = new TestHost();
        h.Db.Tenants.Add(new Tenant { Id = h.Ctx.TenantId, Code = "demo", Name = "Demo" }); await h.SaveAsync();
        var mapper = new SsoUserMapper(h.Db); var o = Opts();
        var u1 = await mapper.MapAsync(new SsoIdentity("https://idp.example.com", "sub-1", "Ann@Example.com", "Ann Lee", new[] { "rfid-ops" }), o);
        Assert.NotNull(u1); Assert.Equal("ann@example.com", u1!.Email); Assert.Equal(UserRole.Operator, u1.Role); Assert.Equal("sso", u1.PasswordHash); Assert.NotNull(u1.LastLoginAt);
        var again = await mapper.MapAsync(new SsoIdentity("https://idp.example.com", "sub-1", null, "Ann Lee", new[] { "rfid-admins" }), o);
        Assert.Equal(u1.Id, again!.Id); Assert.Equal(UserRole.Admin, again.Role); Assert.Equal(1, await h.Db.Users.CountAsync());
        // A pre-existing local account is linked by e-mail on first SSO login.
        var local = new User { TenantId = h.Ctx.TenantId, Email = "bob@example.com", DisplayName = "Bob", PasswordHash = "x", Role = UserRole.Admin };
        h.Db.Users.Add(local); await h.SaveAsync();
        var bob = await mapper.MapAsync(new SsoIdentity("https://idp.example.com", "sub-2", "bob@example.com", "Bob K", Array.Empty<string>()), o);
        Assert.Equal(local.Id, bob!.Id); Assert.Equal("sub-2", bob.ExternalSubject); Assert.Equal(UserRole.Admin, bob.Role); // no groups → local role kept
        // Unknown users are rejected when auto-provisioning is off; inactive users are always rejected.
        Assert.Null(await mapper.MapAsync(new SsoIdentity("https://idp.example.com", "sub-3", "cat@example.com", null, Array.Empty<string>()), new SsoOptions { TenantCode = "demo", AutoProvision = false }));
        local.IsActive = false; await h.SaveAsync();
        Assert.Null(await mapper.MapAsync(new SsoIdentity("https://idp.example.com", "sub-2", null, null, Array.Empty<string>()), o));
    }

    [Fact]
    public async Task Site_access_scopes_queries_and_roles_to_the_users_sites()
    {
        var h = new TestHost();
        var asset = await h.InstallTypeAsync("asset-management", "ASSET");
        var north = h.Loc(LocationKind.Site, "North"); var nRoom = h.Loc(LocationKind.Room, "N-Store", north);
        var south = h.Loc(LocationKind.Site, "South"); var sRoom = h.Loc(LocationKind.Room, "S-Store", south);
        h.Item(asset, "N-1", nRoom); h.Item(asset, "N-2", nRoom); h.Item(asset, "S-1", sRoom); await h.SaveAsync();
        var access = new SiteAccess(h.Db, restricted: true, UserRole.Viewer, new Dictionary<Guid, UserRole> { [north.Id] = UserRole.Operator });
        var paths = await access.AllowedPathsAsync();
        Assert.Equal(new[] { north.Path }, paths);
        Assert.Equal(2, await SiteAccess.Filter(h.Db.Items.Include(i => i.CurrentLocation), paths).CountAsync());
        Assert.Equal(new[] { "N-Store", "North" }, (await SiteAccess.Filter(h.Db.Locations, paths).Select(l => l.Name).ToListAsync()).OrderBy(n => n));
        Assert.Equal(UserRole.Operator, await access.RoleForLocationAsync(nRoom.Id));
        Assert.Equal(UserRole.Operator, await access.RoleForLocationAsync(north.Id));
        await access.EnsureAsync(nRoom.Id, UserRole.Operator);
        await Assert.ThrowsAsync<DomainException>(() => access.EnsureAsync(nRoom.Id, UserRole.Admin));
        await Assert.ThrowsAsync<DomainException>(() => access.EnsureAsync(sRoom.Id, UserRole.Viewer)); // outside the user's sites
        await Assert.ThrowsAsync<DomainException>(() => access.EnsureAsync(null, UserRole.Operator));  // no location → global (Viewer) role applies
        var unrestricted = SiteAccess.Unrestricted(h.Db, UserRole.Operator);
        Assert.Null(await unrestricted.AllowedPathsAsync());
        Assert.Equal(3, await SiteAccess.Filter(h.Db.Items.Include(i => i.CurrentLocation), await unrestricted.AllowedPathsAsync()).CountAsync());
        await unrestricted.EnsureAsync(sRoom.Id, UserRole.Operator);
        await Assert.ThrowsAsync<DomainException>(() => unrestricted.EnsureAsync(sRoom.Id, UserRole.Admin));
    }
}
