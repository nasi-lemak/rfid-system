using Rfid.Protocols;
using System.Net;
using System.Net.Sockets;
using Microsoft.EntityFrameworkCore;
using Rfid.Application.Contracts;
using Rfid.Protocols.Llrp;
using Rfid.Application.Positioning;
using Rfid.Application.Services;
using Rfid.Domain;
using Rfid.Domain.Entities;
using Xunit;

namespace Rfid.Tests;

public class LlrpConfigTests
{
    [Fact]
    public void Capabilities_round_trip_and_power_index_lookup()
    {
        var resp = LlrpConfigCodec.CapabilitiesResponse(5, 4, 25882, 2001005, "7.6.1", 4, 4, new[] { (1, 10.0), (2, 20.0), (3, 30.0), (4, 31.5) });
        var caps = LlrpConfigCodec.ParseCapabilities(resp.AsSpan(LlrpCodec.HeaderLength));
        Assert.Equal("Impinj", caps.Manufacturer); Assert.Equal("7.6.1", caps.Firmware); Assert.Equal(4, caps.MaxAntennas); Assert.Equal(4, caps.Gpis); Assert.True(caps.HasUtcClock);
        Assert.Equal(4, caps.PowerTable.Count); Assert.Equal(3, caps.PowerIndexFor(29)); Assert.Equal(4, caps.PowerIndexFor(40));
    }

    [Fact]
    public void Antenna_config_gpo_and_gpi_rospec_encode()
    {
        var caps = new ReaderCapabilities(4, true, 0, 0, "", 4, 4, new[] { (1, 10.0), (2, 25.0) });
        var msg = LlrpConfigCodec.SetAntennaConfig(1, new LlrpReaderOptions { TransmitPowerDbm = 24, Session = 2, TagPopulation = 100, AntennaIds = new ushort[] { 1, 3 } }, caps);
        Assert.Equal(LlrpMsg.SetReaderConfig, LlrpCodec.DecodeHeader(msg).type);
        var ants = LlrpCodec.ParseTlvs(msg.AsSpan(LlrpCodec.HeaderLength + 1)).Where(p => p.Type == LlrpParamEx.AntennaConfiguration).ToList();
        Assert.Equal(2, ants.Count);
        var sub = LlrpCodec.ParseTlvs(ants[0].Value.AsSpan(2));
        var tx = sub.First(p => p.Type == LlrpParamEx.RfTransmitter); Assert.Equal(2, System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(tx.Value.AsSpan(4))); // index of 25 dBm
        var inv = sub.First(p => p.Type == LlrpParamEx.C1G2InventoryCommand);
        var sing = LlrpCodec.ParseTlvs(inv.Value.AsSpan(1)).First(p => p.Type == LlrpParamEx.C1G2SingulationControl);
        Assert.Equal(2, sing.Value[0] >> 6); Assert.Equal(100, System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(sing.Value.AsSpan(1)));

        var gpo = LlrpConfigCodec.SetGpo(2, 3, true);
        var gpoParam = LlrpCodec.ParseTlvs(gpo.AsSpan(LlrpCodec.HeaderLength + 1)).Single(); Assert.Equal(LlrpParamEx.GpoWriteData, gpoParam.Type); Assert.Equal(3, gpoParam.Value[1]); Assert.Equal(0x80, gpoParam.Value[2]);

        var ro = LlrpConfigCodec.AddRoSpecGpiTriggered(3, 1, 2);
        var roSpec = LlrpCodec.ParseTlvs(ro.AsSpan(LlrpCodec.HeaderLength)).Single();
        var boundary = LlrpCodec.ParseTlvs(roSpec.Value.AsSpan(6)).First(p => p.Type == LlrpParam.RoBoundarySpec);
        var start = LlrpCodec.ParseTlvs(boundary.Value).First(p => p.Type == LlrpParam.RoSpecStartTrigger);
        Assert.Equal(2, start.Value[0]); Assert.Contains(LlrpCodec.ParseTlvs(start.Value.AsSpan(1)), p => p.Type == LlrpParamEx.GpiTriggerValue);

        var ev = LlrpConfigCodec.GpiEventNotification(9, 2, true);
        Assert.Equal(new[] { (2, true) }, LlrpConfigCodec.ParseGpiEvents(ev.AsSpan(LlrpCodec.HeaderLength)));
    }

    [Fact]
    public async Task Client_reads_capabilities_applies_config_and_raises_gpi_events()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var received = new List<ushort>();
        var reader = Task.Run(async () =>
        {
            using var c = await listener.AcceptTcpClientAsync(); var s = c.GetStream();
            await s.WriteAsync(LlrpCodec.ReaderEventNotification(1));
            var header = new byte[10];
            for (var i = 0; i < 7; i++)
            {
                await s.ReadExactlyAsync(header); var (type, len, id) = LlrpCodec.DecodeHeader(header); var body = new byte[len - 10]; await s.ReadExactlyAsync(body); received.Add(type);
                if (type == LlrpMsg.GetReaderCapabilities) { await s.WriteAsync(LlrpConfigCodec.CapabilitiesResponse(id, 4, 10642, 1, "3.2", 2, 2, new[] { (1, 15.0), (2, 30.0) })); continue; }
                var respType = type switch { LlrpMsg.SetReaderConfig => LlrpMsg.SetReaderConfigResponse, LlrpMsg.DeleteRoSpec => LlrpMsg.DeleteRoSpecResponse, LlrpMsg.AddRoSpec => LlrpMsg.AddRoSpecResponse, LlrpMsg.EnableRoSpec => LlrpMsg.EnableRoSpecResponse, _ => LlrpMsg.StartRoSpecResponse };
                await s.WriteAsync(LlrpCodec.EncodeMessage(respType, id, LlrpCodec.Status(0)));
            }
            await s.WriteAsync(LlrpConfigCodec.GpiEventNotification(50, 1, true));
            await s.ReadExactlyAsync(header); var (t2, l2, id2) = LlrpCodec.DecodeHeader(header); var b2 = new byte[l2 - 10]; await s.ReadExactlyAsync(b2); received.Add(t2); // GPO write
            await s.WriteAsync(LlrpCodec.EncodeMessage(LlrpMsg.SetReaderConfigResponse, id2, LlrpCodec.Status(0)));
            await Task.Delay(200);
        });
        await using var client = new LlrpClient("127.0.0.1", port);
        var gpi = new TaskCompletionSource<(int, bool)>();
        client.GpiChanged += (p, h) => gpi.TrySetResult((p, h));
        await client.StartAsync(new LlrpReaderOptions { TransmitPowerDbm = 28, Session = 2 });
        Assert.Equal("Zebra/Motorola", client.Capabilities!.Manufacturer); Assert.Equal(2, client.Capabilities.PowerTable.Count);
        Assert.Equal((1, true), await gpi.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        await client.SetGpoAsync(2, true);
        await reader.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new ushort[] { LlrpMsg.GetReaderCapabilities, LlrpMsg.SetReaderConfig, LlrpMsg.SetReaderConfig, LlrpMsg.DeleteRoSpec, LlrpMsg.AddRoSpec, LlrpMsg.EnableRoSpec, LlrpMsg.StartRoSpec, LlrpMsg.SetReaderConfig }, received);
        listener.Stop();
    }
}

public class SmoothingAndRangeTests
{
    [Fact]
    public void Kalman_reduces_noise_on_a_stationary_tag()
    {
        var rnd = new Random(7); var track = new KalmanTrack(10, 10, DateTime.UtcNow);
        double rawErr = 0, smoothErr = 0; var t = DateTime.UtcNow;
        for (var i = 0; i < 60; i++)
        {
            t = t.AddSeconds(1);
            var mx = 10 + (rnd.NextDouble() - 0.5) * 4; var my = 10 + (rnd.NextDouble() - 0.5) * 4; // ±2 m noise
            var (x, y, _) = track.Update(mx, my, 1.5, t);
            rawErr += Math.Abs(mx - 10) + Math.Abs(my - 10); smoothErr += Math.Abs(x - 10) + Math.Abs(y - 10);
        }
        Assert.True(smoothErr < rawErr * 0.6, $"smoothed {smoothErr:F1} vs raw {rawErr:F1}");
    }

    [Fact]
    public void Kalman_follows_a_moving_tag()
    {
        var track = new KalmanTrack(0, 0, DateTime.UtcNow); var t = DateTime.UtcNow; (double x, double y, double a) last = (0, 0, 0);
        for (var i = 1; i <= 30; i++) { t = t.AddSeconds(1); last = track.Update(i * 0.5, 0, 0.5, t); }
        Assert.InRange(last.x, 13.5, 15.5); Assert.InRange(track.Velocity.vx, 0.3, 0.7);
    }

    [Fact]
    public async Task Ranging_input_beats_rssi_and_smoother_is_applied()
    {
        var h = new TestHost();
        var badge = await h.InstallTypeAsync("people-presence", "BADGE");
        var hall = h.Loc(LocationKind.Zone, "Hall", null, new() { ["widthM"] = 20, ["heightM"] = 10 });
        var (item, tag) = h.Item(badge, "B1", hall);
        var gw = new Device { TenantId = h.Ctx.TenantId, Name = "UWB anchors", Kind = DeviceKind.Fixed };
        foreach (var (p, x, y) in new[] { (1, 0.0, 0.0), (2, 20.0, 0.0), (3, 0.0, 10.0), (4, 20.0, 10.0) }) gw.Antennas.Add(new Antenna { TenantId = h.Ctx.TenantId, Port = p, LocationId = hall.Id, X = x, Y = y });
        h.Db.Devices.Add(gw); await h.SaveAsync();
        var smoother = new PositionSmoother();
        var ingest = new ReadIngestionService(h.Db, h.Ctx, new TagResolver(h.Db), h.Rules, new NullLivePublisher(), null, new PositionService(h.Db, smoother));
        double D(double ax, double ay) => Math.Sqrt((7 - ax) * (7 - ax) + (3 - ay) * (3 - ay));
        var t0 = DateTime.UtcNow;
        for (var i = 0; i < 3; i++)
            await ingest.IngestAsync(new ReadBatchRequest { DeviceId = gw.Id, Reads = { new() { Epc = tag.Epc, AntennaPort = 1, RangeM = D(0, 0), ReadAt = t0.AddSeconds(i) }, new() { Epc = tag.Epc, AntennaPort = 2, RangeM = D(20, 0), ReadAt = t0.AddSeconds(i) }, new() { Epc = tag.Epc, AntennaPort = 3, RangeM = D(0, 10), ReadAt = t0.AddSeconds(i) }, new() { Epc = tag.Epc, AntennaPort = 4, RangeM = D(20, 10) + 0.3, Rssi = -90, ReadAt = t0.AddSeconds(i) } } }, ReadSource.Fixed);
        var fresh = await h.Db.Items.FirstAsync(i => i.Id == item.Id);
        Assert.InRange(fresh.PositionX!.Value, 6.7, 7.3); Assert.InRange(fresh.PositionY!.Value, 2.7, 3.3);
        Assert.True(fresh.PositionAccuracyM < 1.0);
        Assert.Single(IngestAdapters.Parse("""[{"epc":"AAAA","rangeM":3.2,"antenna":2}]""").Reads); Assert.Equal(3.2, IngestAdapters.Parse("""[{"epc":"AAAA","rangeM":3.2}]""").Reads[0].RangeM);
    }
}

public class ImportAndPrintQueueTests
{
    [Fact]
    public async Task Csv_import_creates_updates_and_reconciles()
    {
        var h = new TestHost();
        var asset = await h.InstallTypeAsync("asset-management", "ASSET");
        var store = h.Loc(LocationKind.Room, "Store"); var (existing, _) = h.Item(asset, "A-1", store); existing.Cost = 100; await h.SaveAsync();
        var csv = "AssetNumber,Description,AssetClass,Location,Owner,AcquisitionValue,CapitalizationDate,EPC,CostCenter\nA-1,\"Chair, blue\",ASSET,Store,Jane Lee,120,2024-01-15,,CC-1\nA-2,Desk,ASSET,New Wing,Jane Lee,300,2024-02-01,3034ABCD000000000000AAAA,CC-1\n,Broken row,ASSET,,,,,,\n";
        var rows = ImportService.ParseCsv(csv);
        Assert.Equal(3, rows.Count); Assert.Equal("Chair, blue", rows[0].Name); Assert.Equal("CC-1", rows[0].Attributes["CostCenter"]);
        var svc = new ImportService(h.Db, h.Ctx);
        var dry = await svc.ImportAsync(rows, new ImportOptions { DryRun = true });
        Assert.Equal(1, dry.Created); Assert.Equal(1, dry.Updated); Assert.Equal(1, dry.Errors); Assert.Equal(1, await h.Db.Items.CountAsync());
        var real = await svc.ImportAsync(rows, new ImportOptions { Source = "sap" });
        Assert.Equal(1, real.Created); Assert.Equal(1, real.Updated);
        var a1 = await h.Db.Items.Include(i => i.CustodianParty).FirstAsync(i => i.Identifier == "A-1");
        Assert.Equal("Chair, blue", a1.Name); Assert.Equal(120, a1.Cost); Assert.Equal("Jane Lee", a1.CustodianParty!.Name); Assert.Equal("CC-1", a1.Attributes["CostCenter"]?.ToString());
        var a2 = await h.Db.Items.Include(i => i.Tags).Include(i => i.CurrentLocation).FirstAsync(i => i.Identifier == "A-2");
        Assert.Equal("New Wing", a2.CurrentLocation!.Name); Assert.Single(a2.Tags); Assert.Equal(2, await h.Db.ItemEvents.CountAsync(e => e.Type == ItemEventType.Imported));
        var again = await svc.ImportAsync(rows, new ImportOptions()); Assert.Equal(2, again.Unchanged);

        var rec = await svc.ReconcileAsync(ImportService.ParseCsv("AssetNumber,AssetClass,Location,AcquisitionValue\nA-1,ASSET,Store,999\nA-9,ASSET,Store,1\n"));
        Assert.Equal(1, rec.Matched); Assert.Equal(new[] { "A-9" }, rec.MissingInPlatform); Assert.Equal(new[] { "A-2" }, rec.MissingInErp);
        Assert.Contains(rec.Differences, d => d.identifier == "A-1" && d.field == "cost" && d.erp == "999" && d.platform == "120");
        Assert.DoesNotContain(rec.Differences, d => d.field == "location");
        Assert.Single(ImportService.ParseJson("""{"rows":[{"identifier":"J-1","type":"ASSET","site":"North"}]}""")); Assert.Equal("North", ImportService.ParseJson("""[{"identifier":"J-1","site":"North"}]""")[0].Attributes["site"]);
    }

    private class FakePrinter : IPrinterClient { public List<string> Sent = new(); public bool Fail; public Task SendAsync(string host, int port, string zpl, CancellationToken ct) { if (Fail) throw new IOException("printer offline"); Sent.Add(zpl); return Task.CompletedTask; } }

    [Fact]
    public async Task Print_queue_prints_retries_audits_and_tracks_label_stock()
    {
        var h = new TestHost();
        var asset = await h.InstallTypeAsync("asset-management", "ASSET");
        var store = h.Loc(LocationKind.Room, "Store"); var (item, tag) = h.Item(asset, "A-1", store);
        var printer = new Device { TenantId = h.Ctx.TenantId, Name = "ZT411R", Kind = DeviceKind.Printer, Config = new() { ["host"] = "10.0.0.5", ["labelStock"] = 51, ["labelStockMin"] = 50 } };
        h.Db.Devices.Add(printer); await h.SaveAsync();
        var fake = new FakePrinter();
        var q = new PrintQueueService(h.Db, h.Ctx, fake) { MaxAttempts = 2 };
        var now = DateTime.UtcNow;
        var jobs = await q.EnqueueAsync(new[] { item.Id }, printer.Id, copies: 2);
        Assert.Single(jobs); Assert.Equal(PrintReason.Initial, jobs[0].Reason); Assert.Contains(tag.Epc, jobs[0].Zpl);
        Assert.Equal(1, await q.ProcessAsync(now));
        Assert.Single(fake.Sent); Assert.Contains("^PQ2", fake.Sent[0]);
        Assert.Equal(PrintJobStatus.Printed, (await h.Db.PrintJobs.SingleAsync()).Status);
        Assert.Equal(49, Convert.ToInt32((await h.Db.Devices.FirstAsync(d => d.Id == printer.Id)).Config["labelStock"]));
        Assert.Contains(await h.Db.Alerts.ToListAsync(), a => a.Message.Contains("Label stock low"));
        Assert.Single(await h.Db.ItemEvents.Where(e => e.Type == ItemEventType.LabelPrinted).ToListAsync());

        var reprint = await q.EnqueueAsync(new[] { item.Id }, printer.Id, note: "damaged label");
        Assert.Equal(PrintReason.Reprint, reprint[0].Reason);
        fake.Fail = true;
        Assert.Equal(0, await q.ProcessAsync(now.AddMinutes(1)));
        var j = await h.Db.PrintJobs.FirstAsync(x => x.Id == reprint[0].Id); Assert.Equal(PrintJobStatus.Queued, j.Status); Assert.Equal(1, j.Attempts); Assert.NotNull(j.NextAttemptAt);
        Assert.Equal(0, await q.ProcessAsync(now.AddMinutes(1).AddSeconds(5))); // not due yet
        Assert.Equal(0, await q.ProcessAsync(now.AddMinutes(2)));
        Assert.Equal(PrintJobStatus.Failed, (await h.Db.PrintJobs.FirstAsync(x => x.Id == reprint[0].Id)).Status);
        var (cols, rows) = await new ReportService(h.Db).RunAsync("print-jobs", new Dictionary<string, string?>()); Assert.Equal(2, rows.Count);
    }
}
