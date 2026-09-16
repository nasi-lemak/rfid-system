using Microsoft.EntityFrameworkCore;
using Rfid.Application.Contracts;
using Rfid.Application.Security;
using Rfid.Application.Services;
using Rfid.Domain;
using Rfid.Domain.Entities;
using Rfid.Edge;
using Rfid.Protocols;
using Xunit;

namespace Rfid.Tests;

/// <summary>One site-scoping rule for aggregates, line-table isolation, vendor position ingest, edge MQTT mapping.</summary>
public class ScopeAndIsolationTests
{
    private static async Task<(TestHost h, Location siteA, Location siteB, Item a1, Item b1)> TwoSitesAsync()
    {
        var h = new TestHost();
        var tool = await h.InstallTypeAsync("tool-tracking", "TOOL");
        var siteA = h.Loc(LocationKind.Site, "Site A"); var roomA = h.Loc(LocationKind.Room, "Room A", siteA);
        var siteB = h.Loc(LocationKind.Site, "Site B"); var roomB = h.Loc(LocationKind.Room, "Room B", siteB);
        var (a1, aTag) = h.Item(tool, "TL-A1", roomA); var (b1, bTag) = h.Item(tool, "TL-B1", roomB);
        h.Db.Devices.Add(new Device { TenantId = h.Ctx.TenantId, Name = "Reader A", Kind = DeviceKind.Portal, SiteLocationId = siteA.Id, Health = DeviceHealth.Online });
        h.Db.Devices.Add(new Device { TenantId = h.Ctx.TenantId, Name = "Reader B", Kind = DeviceKind.Portal, SiteLocationId = siteB.Id, Health = DeviceHealth.Offline });
        await h.SaveAsync();
        await h.Ops.ProcessAsync(new OperationRequest { Type = OperationType.Count, Lines = { new() { Epc = aTag.Epc } } });
        await h.Ops.ProcessAsync(new OperationRequest { Type = OperationType.Count, Lines = { new() { Epc = bTag.Epc } } });
        h.Db.Alerts.Add(new Alert { TenantId = h.Ctx.TenantId, ItemId = b1.Id, LocationId = roomB.Id, Severity = Severity.Critical, Message = "B only" });
        await h.SaveAsync();
        return (h, siteA, siteB, a1, b1);
    }

    [Fact]
    public async Task Reports_analytics_and_dashboards_share_one_site_scope()
    {
        var (h, siteA, _, a1, b1) = await TwoSitesAsync();
        var scopeA = SiteScope.For(new List<string> { siteA.Path }, h.Db);

        var reports = new ReportService(h.Db);
        var (_, all) = await reports.RunAsync("inventory", new Dictionary<string, string?>(), SiteScope.All);
        var (_, onlyA) = await reports.RunAsync("inventory", new Dictionary<string, string?>(), scopeA);
        Assert.Equal(2, all.Count); Assert.Single(onlyA); Assert.Equal("TL-A1", onlyA[0][0]);
        var (_, moves) = await reports.RunAsync("movements", new Dictionary<string, string?> { ["type"] = "Counted" }, scopeA);
        Assert.All(moves, r => Assert.Equal("TL-A1", r[2]));

        var analytics = new AnalyticsService(h.Db);
        Assert.Equal(2, (await analytics.TrendAsync("events", 7, "day", null, SiteScope.All)).Total);
        Assert.Equal(1, (await analytics.TrendAsync("events", 7, "day", null, scopeA)).Total);
        Assert.Equal(1, (await analytics.UtilizationAsync(7, null, scopeA)).Single().Items);

        var dashboards = new DashboardService(h.Db);
        var widgets = new List<DashboardWidget> { new() { Id = "items", Type = "stat", Config = new() { ["filter"] = "items" } }, new() { Id = "crit", Type = "stat", Config = new() { ["filter"] = "criticalAlerts" } }, new() { Id = "off", Type = "stat", Config = new() { ["filter"] = "devicesOffline" } } };
        var scoped = await dashboards.EvaluateAsync(widgets, scopeA);
        Assert.Equal(1L, Value(scoped[0])); Assert.Equal(0L, Value(scoped[1])); Assert.Equal(0L, Value(scoped[2]));     // alert and offline reader belong to site B
        var unscoped = await dashboards.EvaluateAsync(widgets, SiteScope.All);
        Assert.Equal(2L, Value(unscoped[0])); Assert.Equal(1L, Value(unscoped[1])); Assert.Equal(1L, Value(unscoped[2]));
        var overview = System.Text.Json.JsonSerializer.SerializeToElement(await dashboards.OverviewAsync(scopeA));
        Assert.Equal(1, overview.GetProperty("totals").GetProperty("items").GetInt32());
        Assert.Equal(1, overview.GetProperty("totals").GetProperty("devices").GetInt32());
    }
    private static long Value(WidgetResult r) => (long)r.Data!.GetType().GetProperty("value")!.GetValue(r.Data)!;

    [Fact]
    public async Task Line_tables_carry_the_tenant_and_are_covered_by_row_level_security()
    {
        var h = new TestHost();
        var tool = await h.InstallTypeAsync("tool-tracking", "TOOL");
        var crib = h.Loc(LocationKind.Room, "Crib"); var (item, tag) = h.Item(tool, "TL-1", crib);
        await h.SaveAsync();
        await h.Ops.ProcessAsync(new OperationRequest { Type = OperationType.Count, Lines = { new() { Epc = tag.Epc } } });
        await h.Stocktakes.CreateAsync("c", crib.Id, null);
        Assert.All(await h.Db.OperationLines.ToListAsync(), l => Assert.Equal(h.Ctx.TenantId, l.TenantId));
        Assert.All(await h.Db.StocktakeLines.ToListAsync(), l => Assert.Equal(h.Ctx.TenantId, l.TenantId));

        var opts = new DbContextOptionsBuilder<Rfid.Infrastructure.Persistence.AppDbContext>().UseNpgsql("Host=localhost;Database=model-only").Options;
        using var db = new Rfid.Infrastructure.Persistence.AppDbContext(opts, new FixedContext());
        var tables = Rfid.Infrastructure.Persistence.RowLevelSecurity.TenantTables(db.Model).Select(t => t.table).ToList();
        Assert.Contains("operation_lines", tables); Assert.Contains("stocktake_lines", tables);
    }

    [Fact]
    public async Task External_positions_update_items_like_trilateration_and_are_idempotent()
    {
        var h = new TestHost();
        var tool = await h.InstallTypeAsync("tool-tracking", "TOOL");
        var hall = h.Loc(LocationKind.Room, "Hall", attrs: new() { ["widthM"] = 40, ["heightM"] = 20 });
        var (item, tag) = h.Item(tool, "TL-1", hall);
        await h.SaveAsync();
        var positions = new PositionService(h.Db); var resolver = new TagResolver(h.Db);
        var t0 = DateTime.UtcNow.AddMinutes(-5);

        var batch = new PositionBatchRequest { BatchId = "uwb-1", Fixes = { new() { Epc = tag.Epc, LocationId = hall.Id, X = 3.5, Y = 4.0, AccuracyM = 0.3, At = t0 }, new() { Epc = "UNKNOWN", LocationId = hall.Id, X = 1, Y = 1 }, new() { ItemId = item.Id, LocationId = Guid.NewGuid(), X = 1, Y = 1 } } };
        var r = await positions.ApplyExternalAsync(batch, resolver, h.Ctx.TenantId);
        Assert.Equal(1, r.Applied); Assert.Equal(1, r.Unknown); Assert.Equal(1, r.Rejected);
        var fresh = await h.Db.Items.FirstAsync(i => i.Id == item.Id);
        Assert.Equal(3.5, fresh.PositionX); Assert.Equal(hall.Id, fresh.PositionLocationId); Assert.Equal(1, await h.Db.PositionFixes.CountAsync());

        Assert.True((await positions.ApplyExternalAsync(batch, resolver, h.Ctx.TenantId)).Duplicate);                                       // replay
        var late = await positions.ApplyExternalAsync(new PositionBatchRequest { Fixes = { new() { ItemId = item.Id, LocationId = hall.Id, X = 9, Y = 9, At = t0.AddMinutes(-1) } } }, resolver, h.Ctx.TenantId);
        Assert.Equal(1, late.Rejected); Assert.Equal(3.5, (await h.Db.Items.FirstAsync(i => i.Id == item.Id)).PositionX);                // older fix ignored
        var move = await positions.ApplyExternalAsync(new PositionBatchRequest { Fixes = { new() { ItemId = item.Id, LocationId = hall.Id, X = 12, Y = 6, At = t0.AddMinutes(1) } } }, resolver, h.Ctx.TenantId);
        Assert.Equal(1, move.Applied); Assert.Equal(2, await h.Db.PositionFixes.CountAsync());
    }

    [Fact]
    public void Edge_mqtt_bridge_maps_topics_to_devices_with_mqtt_wildcards()
    {
        var dock = Guid.NewGuid(); var any = Guid.NewGuid();
        var map = new Dictionary<string, Guid> { ["rfid/site1/dock1"] = dock, ["impinj/+/events"] = any };
        Assert.Equal(dock, MqttBridge.DeviceFor(map, "rfid/site1/dock1"));
        Assert.Equal(any, MqttBridge.DeviceFor(map, "impinj/r700-3/events"));
        Assert.Null(MqttBridge.DeviceFor(map, "rfid/site2/dock1"));
        Assert.True(TopicMatcher.Matches("rfid/#", "rfid/a/b/c")); Assert.False(TopicMatcher.Matches("rfid/+", "rfid/a/b"));
    }
}
