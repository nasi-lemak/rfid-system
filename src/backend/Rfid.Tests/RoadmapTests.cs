using Rfid.Protocols;
using Microsoft.EntityFrameworkCore;
using Rfid.Application.Contracts;
using Rfid.Application.Services;
using Rfid.Domain;
using Rfid.Domain.Entities;
using Rfid.Domain.Epc;
using Xunit;

namespace Rfid.Tests;

public class IngestAdapterTests
{
    [Fact]
    public void Parses_impinj_iot_interface_events()
    {
        var json = """
        [{"timestamp":"2026-09-14T10:00:00.123Z","hostname":"r700-dock1","eventType":"tagInventory","tagInventoryEvent":{"epcHex":"3034F8B20001000000000001","antennaPort":2,"peakRssiCdbm":-5230,"tidHex":"E2801160"}},
         {"timestamp":"2026-09-14T10:00:01.000Z","tagInventoryEvent":{"epc":"MDT4sgABAAAAAAAB","antennaPort":1,"peakRssiCdbm":-6100}}]
        """;
        var b = IngestAdapters.Parse(json);
        Assert.Equal(2, b.Reads.Count);
        Assert.Equal("3034F8B20001000000000001", b.Reads[0].Epc); Assert.Equal(2, b.Reads[0].AntennaPort); Assert.Equal(-52.3, b.Reads[0].Rssi!.Value, 2); Assert.Equal("E2801160", b.Reads[0].Tid);
        Assert.Equal(new DateTime(2026, 9, 14, 10, 0, 0, 123, DateTimeKind.Utc), b.Reads[0].ReadAt!.Value);
        Assert.Equal("3034F8B20001000000000001", b.Reads[1].Epc); // base64 fallback
        Assert.Equal("r700-dock1", b.SessionId);
    }

    [Fact]
    public void Parses_zebra_iot_connector_events()
    {
        var json = """{"timestamp":"2026-09-14T10:00:00Z","type":"SIMPLE","data":{"idHex":"e2801160600002000000abcd","antenna":3,"peakRssi":-48,"TID":"E28011606000020000001234"}}""";
        var b = IngestAdapters.Parse(json);
        Assert.Single(b.Reads);
        Assert.Equal("e2801160600002000000abcd", b.Reads[0].Epc); Assert.Equal(3, b.Reads[0].AntennaPort); Assert.Equal(-48, b.Reads[0].Rssi);
    }

    [Fact]
    public void Parses_generic_shapes()
    {
        Assert.Equal(2, IngestAdapters.Parse("""{"deviceId":"11111111-1111-1111-1111-111111111111","reads":[{"epc":"AAAA","rssi":-50},{"tag":"BBBB","port":2}]}""").Reads.Count);
        Assert.Equal(3, IngestAdapters.Parse("""["AAAA","BBBB",{"epc":"CCCC"}]""").Reads.Count);
        Assert.Single(IngestAdapters.Parse("""{"epc":"DDDD","timestamp":"2026-01-01T00:00:00Z"}""").Reads);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), IngestAdapters.Parse("""{"deviceId":"11111111-1111-1111-1111-111111111111","reads":[]}""").DeviceId);
    }

    [Theory]
    [InlineData("rfid/#", "rfid/site1/dock1", true)]
    [InlineData("rfid/+/dock1", "rfid/site1/dock1", true)]
    [InlineData("rfid/+/dock1", "rfid/site1/dock2", false)]
    [InlineData("rfid/site1", "rfid/site1/dock1", false)]
    [InlineData("rfid/site1/dock1", "rfid/site1/dock1", true)]
    public void Mqtt_topic_matching(string pattern, string topic, bool expected) => Assert.Equal(expected, Rfid.Api.Background.MqttIngestService.TopicMatches(pattern, topic));
}

public class PresenceTests
{
    [Fact]
    public async Task Reads_open_sessions_strongest_zone_wins_and_sweep_closes_with_exit_event()
    {
        var h = new TestHost();
        var badge = await h.InstallTypeAsync("people-presence", "BADGE");
        var site = h.Loc(LocationKind.Site, "Campus");
        var office = h.Loc(LocationKind.Zone, "Office", site, new() { ["presenceTimeoutSec"] = 60 });
        var lab = h.Loc(LocationKind.Zone, "Lab", site, new() { ["restricted"] = true });
        var (item, tag) = h.Item(badge, "BDG-1", site);
        var gw = new Device { TenantId = h.Ctx.TenantId, Name = "BLE gateway", Kind = DeviceKind.Fixed };
        gw.Antennas.Add(new Antenna { TenantId = h.Ctx.TenantId, Port = 1, LocationId = office.Id });
        gw.Antennas.Add(new Antenna { TenantId = h.Ctx.TenantId, Port = 2, LocationId = lab.Id });
        h.Db.Devices.Add(gw);
        await h.SaveAsync();
        var presence = new PresenceService(h.Db, h.Ctx, h.Rules, new NullLivePublisher());
        var ingest = new ReadIngestionService(h.Db, h.Ctx, new TagResolver(h.Db), h.Rules, new NullLivePublisher(), presence);

        var t0 = DateTime.UtcNow.AddMinutes(-10);
        // Seen by both antennas; the office antenna is stronger => office wins.
        await ingest.IngestAsync(new ReadBatchRequest { DeviceId = gw.Id, Reads = { new() { Epc = tag.Epc, AntennaPort = 2, Rssi = -80, ReadAt = t0 }, new() { Epc = tag.Epc, AntennaPort = 1, Rssi = -50, ReadAt = t0 } } }, ReadSource.Fixed);
        var open = await h.Db.PresenceSessions.Where(p => p.ExitedAt == null).ToListAsync();
        Assert.Single(open); Assert.Equal(office.Id, open[0].LocationId); Assert.Equal(office.Id, (await h.Db.Items.FirstAsync(i => i.Id == item.Id)).CurrentLocationId);

        // Later only the lab sees the badge => office session closed, lab session opened.
        await ingest.IngestAsync(new ReadBatchRequest { DeviceId = gw.Id, Reads = { new() { Epc = tag.Epc, AntennaPort = 2, Rssi = -55, ReadAt = t0.AddMinutes(2) } } }, ReadSource.Fixed);
        var all = await h.Db.PresenceSessions.OrderBy(p => p.EnteredAt).ToListAsync();
        Assert.Equal(2, all.Count); Assert.NotNull(all[0].ExitedAt); Assert.Equal(lab.Id, all[1].LocationId);
        var zones = await presence.OccupancyAsync(site.Id);
        Assert.Single(zones); Assert.Equal("Lab", zones[0].Location); Assert.Equal(1, zones[0].Present);

        // Sweep after the dwell timeout closes the lab session and moves the item up to the site.
        var closed = await presence.SweepAsync(t0.AddMinutes(20));
        Assert.Equal(1, closed);
        Assert.Empty(await presence.OccupancyAsync(site.Id));
        var fresh = await h.Db.Items.FirstAsync(i => i.Id == item.Id);
        Assert.Equal(site.Id, fresh.CurrentLocationId);
        Assert.Contains(await h.Db.ItemEvents.ToListAsync(), e => e.Type == ItemEventType.Moved && e.Data.TryGetValue("direction", out var d) && d?.ToString() == "Out");
    }

    [Fact]
    public async Task Muster_and_timing()
    {
        var h = new TestHost();
        var badge = await h.InstallTypeAsync("people-presence", "BADGE");
        var bib = await h.Db.ItemTypes.FirstAsync(t => t.Code == "BIB");
        var site = h.Loc(LocationKind.Site, "Site");
        var office = h.Loc(LocationKind.Zone, "Office", site);
        var muster = h.Loc(LocationKind.MusterPoint, "Muster A", site);
        var race = h.Loc(LocationKind.Site, "Race");
        var start = h.Loc(LocationKind.Checkpoint, "Start", race, new() { ["order"] = 1 });
        var finish = h.Loc(LocationKind.Checkpoint, "Finish", race, new() { ["order"] = 2 });
        var (b1, t1) = h.Item(badge, "B1", site); var (b2, t2) = h.Item(badge, "B2", site);
        var (r1, rt1) = h.Item(bib, "101", race); var (r2, rt2) = h.Item(bib, "102", race);
        await h.SaveAsync();
        var presence = new PresenceService(h.Db, h.Ctx, h.Rules, new NullLivePublisher());
        var now = DateTime.UtcNow;
        await presence.TrackAsync(b1, office, null, -50, now); await presence.TrackAsync(b2, muster, null, -50, now);
        await h.SaveAsync();
        var m = await presence.MusterAsync(site.Id);
        Assert.Equal(2, m.OnSite); Assert.Equal(1, m.Accounted); Assert.Equal(1, m.Unaccounted); Assert.Equal("B1", m.UnaccountedItems[0].Identifier);

        h.Db.TagReads.AddRange(
            new TagRead { TenantId = h.Ctx.TenantId, Epc = rt1.Epc, ItemId = r1.Id, LocationId = start.Id, ReadAt = now }, new TagRead { TenantId = h.Ctx.TenantId, Epc = rt1.Epc, ItemId = r1.Id, LocationId = finish.Id, ReadAt = now.AddMinutes(40) },
            new TagRead { TenantId = h.Ctx.TenantId, Epc = rt2.Epc, ItemId = r2.Id, LocationId = start.Id, ReadAt = now }, new TagRead { TenantId = h.Ctx.TenantId, Epc = rt2.Epc, ItemId = r2.Id, LocationId = finish.Id, ReadAt = now.AddMinutes(35) });
        await h.SaveAsync();
        var (cps, rows) = await presence.TimingAsync(race.Id);
        Assert.Equal(new[] { "Start", "Finish" }, cps);
        Assert.Equal("102", rows[0].Bib); Assert.Equal(1, rows[0].Rank); Assert.Equal(35 * 60, rows[0].ElapsedSeconds!.Value, 1);
    }
}

public class EncodingLabelReportTests
{
    [Fact]
    public void Grai_and_giai_round_trip()
    {
        var grai = Gs1.EncodeGrai96("0614141", "12345", 999, 1);
        Assert.Equal("GRAI-96", Gs1.Scheme(grai));
        var d = Gs1.DecodeGrai96(grai)!.Value; Assert.Equal("0614141", d.companyPrefix); Assert.Equal("12345", d.assetType); Assert.Equal(999UL, d.serial);
        var giai = Gs1.EncodeGiai96("0614141", 123456789);
        Assert.Equal("GIAI-96", Gs1.Scheme(giai));
        var g = Gs1.DecodeGiai96(giai)!.Value; Assert.Equal("0614141", g.companyPrefix); Assert.Equal(123456789UL, g.assetReference);
        Assert.Equal("SGTIN-96", Gs1.Scheme(Sgtin96.Encode("0614141", "812345", 1)));
    }

    [Fact]
    public void Label_zpl_encodes_epc_and_escapes_control_chars()
    {
        var item = new Item { Name = "Drill ^18V", Identifier = "TL-1", ItemType = new ItemType { Name = "Tool" }, CurrentLocation = new Location { Name = "Crib" }, Attributes = new() { ["kit"] = "SM-A" } };
        var zpl = LabelService.Render(item, "3034ABCD", null);
        Assert.Contains("^RFW,H,,,A^FD3034ABCD^FS", zpl); Assert.Contains("Drill  18V", zpl); Assert.Contains("^BCN", zpl); Assert.StartsWith("^XA", zpl); Assert.EndsWith("^XZ", zpl);
        Assert.Equal("KIT SM-A TL-1", LabelService.Render(item, "X", "KIT {attributes.kit} {identifier}"));
    }

    [Fact]
    public void Depreciation_is_straight_line_and_capped()
    {
        var item = new Item { Cost = 1200, PurchasedAt = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-6)), ItemType = new ItemType { UsefulLifeMonths = 12 } };
        var d = ReportService.Depreciate(item, DateTime.UtcNow);
        Assert.Equal(6, d.months); Assert.Equal(100, d.monthly); Assert.Equal(600, d.accumulated); Assert.Equal(600, d.nbv);
        var old = new Item { Cost = 1200, PurchasedAt = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-40)), ItemType = new ItemType { UsefulLifeMonths = 12 } };
        Assert.Equal(0, ReportService.Depreciate(old, DateTime.UtcNow).nbv);
    }

    [Fact]
    public async Task Reports_run_and_csv_escapes()
    {
        var h = new TestHost();
        var asset = await h.InstallTypeAsync("asset-management", "ASSET");
        var store = h.Loc(LocationKind.Room, "Store, main");
        var (i, _) = h.Item(asset, "A-1", store); i.Cost = 500; i.PurchasedAt = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-1)); i.DueBackAt = DateTime.UtcNow.AddDays(-2); i.CustodianPartyId = h.Party("Bob").Id;
        await h.SaveAsync();
        var svc = new ReportService(h.Db);
        foreach (var r in ReportService.Catalog) { var (cols, rows) = await svc.RunAsync(r.Code, new Dictionary<string, string?>()); Assert.NotEmpty(cols); }
        var (c, rs) = await svc.RunAsync("inventory", new Dictionary<string, string?>());
        var csv = ReportService.ToCsv(c, rs);
        Assert.Contains("\"Store, main\"", csv);
        var (_, overdue) = await svc.RunAsync("overdue", new Dictionary<string, string?>()); Assert.Single(overdue);
    }
}

public class ScheduleIntegrationTemplateTests
{
    [Fact]
    public async Task Scheduled_stocktake_opens_when_due_and_advances()
    {
        var h = new TestHost();
        var asset = await h.InstallTypeAsync("asset-management", "ASSET");
        var store = h.Loc(LocationKind.Room, "Store"); h.Item(asset, "A-1", store); h.Item(asset, "A-2", store);
        var now = DateTime.UtcNow;
        var tod = now.AddHours(-6).TimeOfDay; // the next daily slot is ≥ 18 h away, so the auto-reconcile check at +5 h cannot open a second stocktake
        h.Db.StocktakeSchedules.Add(new StocktakeSchedule { TenantId = h.Ctx.TenantId, Name = "Weekly store", LocationId = store.Id, IntervalDays = 7, TimeOfDay = tod, NextRunAt = now.AddMinutes(-1), AutoReconcileHours = 4 });
        await h.SaveAsync();
        var svc = new StocktakeScheduleService(h.Db, h.Ctx, h.Stocktakes);
        Assert.Equal(1, await svc.RunDueAsync(now));
        var st = await h.Db.Stocktakes.SingleAsync(); Assert.Equal(2, st.ExpectedCount);
        var sched = await h.Db.StocktakeSchedules.SingleAsync(); Assert.True(sched.NextRunAt > now); Assert.Equal(st.Id, sched.LastStocktakeId);
        Assert.Equal(0, await svc.RunDueAsync(now.AddMinutes(1)));
        await svc.RunDueAsync(now.AddHours(5)); // auto-reconcile after 4 h
        Assert.Equal(StocktakeStatus.Reconciled, (await h.Db.Stocktakes.SingleAsync()).Status);
        Assert.Equal(new DateTime(2026, 1, 2, 6, 0, 0), StocktakeScheduleService.ComputeNextRun(new DateTime(2026, 1, 1, 7, 0, 0), 1, TimeSpan.FromHours(6)));
    }

    private class FakeTransport : IIntegrationTransport
    {
        public List<(string url, string body, IDictionary<string, string> headers)> Calls = new();
        public bool Fail;
        public Task<(bool ok, string? error)> PostAsync(string url, string body, IDictionary<string, string> headers, CancellationToken ct) { Calls.Add((url, body, headers)); return Task.FromResult(Fail ? (false, "HTTP 503") : (true, (string?)null)); }
    }

    [Fact]
    public async Task Integration_delivers_signed_batches_with_cursor_and_backoff()
    {
        var h = new TestHost();
        var asset = await h.InstallTypeAsync("asset-management", "ASSET");
        var store = h.Loc(LocationKind.Room, "Store"); var other = h.Loc(LocationKind.Room, "Other");
        var (item, tag) = h.Item(asset, "A-1", store);
        var ep = new IntegrationEndpoint { TenantId = h.Ctx.TenantId, Name = "ERP", Url = "https://erp.example/rfid", Secret = "s3cret", EventCursor = DateTime.UtcNow.AddMinutes(-1), AlertCursor = DateTime.UtcNow.AddMinutes(-1), EventTypes = { ItemEventType.Moved } };
        h.Db.IntegrationEndpoints.Add(ep);
        await h.SaveAsync();
        await h.Ops.ProcessAsync(new OperationRequest { Type = OperationType.Transfer, ToLocationId = other.Id, Lines = { new() { Epc = tag.Epc } } });
        await h.Ops.ProcessAsync(new OperationRequest { Type = OperationType.Count, Lines = { new() { Epc = tag.Epc } } }); // filtered out (Counted)
        var transport = new FakeTransport();
        var svc = new IntegrationService(h.Db, h.Outbox);
        var dispatcher = new Rfid.Application.Platform.OutboxDispatcher(h.Db, new Rfid.Application.Platform.IOutboxHandler[] { new IntegrationOutboxHandler(h.Db, transport) });
        var now = DateTime.UtcNow.AddSeconds(1);
        // The batch is built after the cursor and handed to the outbox; nothing is sent until the dispatcher runs.
        Assert.Equal(1, await svc.EnqueueAsync(ep, now));
        Assert.Empty(transport.Calls); Assert.NotNull(ep.InFlightMessageId);
        Assert.Equal(0, await svc.EnqueueAsync(ep, now.AddSeconds(1)));         // one batch in flight per endpoint keeps ordering
        Assert.Equal(1, (await dispatcher.DispatchAsync(now)).Delivered);
        var call = transport.Calls.Single();
        Assert.Equal(IntegrationService.Sign("s3cret", call.body), call.headers["X-Rfid-Signature"]);
        Assert.Contains("\"type\":\"Moved\"", call.body); Assert.DoesNotContain("Counted", call.body); Assert.Contains("A-1", call.body);
        Assert.Equal(1, ep.DeliveredCount); Assert.Null(ep.NextAttemptAt); Assert.Null(ep.InFlightMessageId);
        Assert.Equal(0, await svc.EnqueueAsync(ep, now.AddSeconds(1))); // nothing new after the cursor

        await h.Ops.ProcessAsync(new OperationRequest { Type = OperationType.Transfer, ToLocationId = store.Id, Lines = { new() { Epc = tag.Epc } } });
        transport.Fail = true;
        var t2 = DateTime.UtcNow.AddSeconds(2);
        Assert.Equal(1, await svc.EnqueueAsync(ep, t2));
        var failed = await dispatcher.DispatchAsync(t2);
        Assert.Equal(1, failed.Failed); Assert.Equal(1, ep.FailureCount); Assert.NotNull(ep.NextAttemptAt); Assert.Equal("HTTP 503", ep.LastError);
        var cursorBefore = ep.EventCursor;
        transport.Fail = false;
        Assert.Equal(1, (await dispatcher.DispatchAsync(t2.AddHours(2))).Delivered);   // retried by the outbox, cursor moved
        Assert.Equal(0, ep.FailureCount); Assert.True(ep.EventCursor > cursorBefore);
    }

    [Fact]
    public async Task Template_export_import_round_trip()
    {
        var h = new TestHost();
        await h.InstallTypeAsync("linen-laundry", "LINEN");
        var def = await h.Templates.ExportAsync();
        Assert.Equal(3, def.ItemTypes.Count); Assert.Equal(2, def.Rules.Count);
        Assert.Contains(def.Rules, r => r.Conditions.Any(c => c.Field == "itemType.code")); // scoped on install
        var h2 = new TestHost();
        var r = await h2.Templates.ApplyDefinitionAsync("custom", "Custom", def);
        Assert.Equal(3, r.ItemTypesCreated); Assert.Equal(2, r.RulesCreated);
        var again = await h2.Templates.ApplyDefinitionAsync("custom", "Custom", def);
        Assert.Equal(0, again.ItemTypesCreated); Assert.Equal(5, again.Skipped);
        var linen = await h2.Db.ItemTypes.FirstAsync(t => t.Code == "LINEN"); Assert.Equal(200, linen.MaxCycles); Assert.Equal("Clean", linen.Lifecycle!.Initial);
    }
}
