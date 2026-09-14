using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Rfid.Application.Contracts;
using Rfid.Application.Integrations;
using Rfid.Application.Labels;
using Rfid.Application.Llrp;
using Rfid.Application.Positioning;
using Rfid.Application.Services;
using Rfid.Domain;
using Rfid.Domain.Entities;
using Xunit;

namespace Rfid.Tests;

public class LlrpTests
{
    [Fact]
    public void Add_rospec_encodes_valid_header_and_report_round_trips()
    {
        var msg = LlrpCodec.AddRoSpec(7, 1);
        var (type, length, id) = LlrpCodec.DecodeHeader(msg);
        Assert.Equal(LlrpMsg.AddRoSpec, type); Assert.Equal(msg.Length, length); Assert.Equal(7u, id);
        var top = LlrpCodec.ParseTlvs(msg.AsSpan(LlrpCodec.HeaderLength));
        Assert.Single(top); Assert.Equal(LlrpParam.RoSpec, top[0].Type);
        Assert.Contains(LlrpCodec.ParseTlvs(top[0].Value.AsSpan(6)), p => p.Type == LlrpParam.AiSpec); // skip ROSpecID/Priority/State

        var now = new DateTime(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);
        var report = LlrpCodec.RoAccessReport(9, new[] { new LlrpTag("3034F8B20001000000000001", 2, -52, now, null, 3, null), new LlrpTag("E2801160600002000000ABCDEF01", 1, -61, null, null, null, null) });
        var tags = LlrpCodec.ParseRoAccessReport(report.AsSpan(LlrpCodec.HeaderLength));
        Assert.Equal(2, tags.Count);
        Assert.Equal("3034F8B20001000000000001", tags[0].Epc); Assert.Equal(2, tags[0].AntennaId); Assert.Equal(-52, tags[0].PeakRssi); Assert.Equal(now, tags[0].FirstSeenUtc); Assert.Equal(3, tags[0].SeenCount);
        Assert.Equal("E2801160600002000000ABCDEF01", tags[1].Epc); // 112-bit EPC via EPCData
        Assert.Equal((0, ""), LlrpCodec.ParseStatus(LlrpCodec.Status(0)));
        Assert.Equal((100, "bad"), LlrpCodec.ParseStatus(LlrpCodec.Status(100, "bad")));
    }

    [Fact]
    public async Task Client_runs_the_handshake_against_a_fake_reader_and_receives_tags()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var received = new List<ushort>();
        var reader = Task.Run(async () =>
        {
            using var c = await listener.AcceptTcpClientAsync();
            var s = c.GetStream();
            await s.WriteAsync(LlrpCodec.ReaderEventNotification(1));
            var header = new byte[10];
            for (var i = 0; i < 6; i++)
            {
                await s.ReadExactlyAsync(header); var (type, len, id) = LlrpCodec.DecodeHeader(header);
                var body = new byte[len - 10]; await s.ReadExactlyAsync(body); received.Add(type);
                if (type == LlrpMsg.GetReaderCapabilities) { await s.WriteAsync(LlrpConfigCodec.CapabilitiesResponse(id, 2, 25882, 1, "8.0", 2, 2, new[] { (1, 30.0) })); continue; }
                var respType = type switch { LlrpMsg.SetReaderConfig => LlrpMsg.SetReaderConfigResponse, LlrpMsg.DeleteRoSpec => LlrpMsg.DeleteRoSpecResponse, LlrpMsg.AddRoSpec => LlrpMsg.AddRoSpecResponse, LlrpMsg.EnableRoSpec => LlrpMsg.EnableRoSpecResponse, _ => LlrpMsg.StartRoSpecResponse };
                await s.WriteAsync(LlrpCodec.EncodeMessage(respType, id, LlrpCodec.Status(0)));
            }
            await s.WriteAsync(LlrpCodec.EncodeMessage(LlrpMsg.Keepalive, 99));
            await s.WriteAsync(LlrpCodec.RoAccessReport(100, new[] { new LlrpTag("3034F8B20001000000000001", 1, -50, DateTime.UtcNow, null, 1, null) }));
            await s.ReadExactlyAsync(header); // keepalive ack
            Assert.Equal(LlrpMsg.KeepaliveAck, LlrpCodec.DecodeHeader(header).type);
            await Task.Delay(300);
        });
        await using var client = new LlrpClient("127.0.0.1", port);
        var got = new TaskCompletionSource<IReadOnlyList<LlrpTag>>();
        client.TagsReported += t => got.TrySetResult(t);
        await client.StartAsync();
        var tags = await got.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Single(tags); Assert.Equal("3034F8B20001000000000001", tags[0].Epc);
        Assert.Equal(new ushort[] { LlrpMsg.GetReaderCapabilities, LlrpMsg.SetReaderConfig, LlrpMsg.DeleteRoSpec, LlrpMsg.AddRoSpec, LlrpMsg.EnableRoSpec, LlrpMsg.StartRoSpec }, received);
        await reader.WaitAsync(TimeSpan.FromSeconds(5));
        listener.Stop();
    }
}

public class PositioningTests
{
    [Fact]
    public void Trilateration_recovers_a_known_point_from_three_anchors()
    {
        var truth = (x: 6.0, y: 4.0);
        double D(double ax, double ay) => Math.Sqrt((truth.x - ax) * (truth.x - ax) + (truth.y - ay) * (truth.y - ay));
        var est = Trilateration.Estimate(new[] { new Anchor(0, 0, D(0, 0)), new Anchor(10, 0, D(10, 0)), new Anchor(0, 8, D(0, 8)), new Anchor(10, 8, D(10, 8) * 1.1) })!;
        Assert.InRange(est.X, 5.5, 6.5); Assert.InRange(est.Y, 3.5, 4.5); Assert.Equal(4, est.Anchors);
        var two = Trilateration.Estimate(new[] { new Anchor(0, 0, 2), new Anchor(10, 0, 8) })!;
        Assert.InRange(two.X, 1.9, 2.1); Assert.Equal(0, two.Y);
        Assert.Equal((0.0, 0.0), (Trilateration.Estimate(new[] { new Anchor(0, 0, 3) })!.X, Trilateration.Estimate(new[] { new Anchor(0, 0, 3) })!.Y));
        Assert.InRange(Trilateration.RssiToDistance(-45), 0.99, 1.01); Assert.InRange(Trilateration.RssiToDistance(-67, -45, 2.2), 9.5, 10.5);
    }

    [Fact]
    public async Task Ingestion_positions_items_seen_by_positioned_antennas()
    {
        var h = new TestHost();
        var badge = await h.InstallTypeAsync("people-presence", "BADGE");
        var hall = h.Loc(LocationKind.Zone, "Hall", null, new() { ["widthM"] = 20, ["heightM"] = 10 });
        var (item, tag) = h.Item(badge, "B1", hall);
        var gw = new Device { TenantId = h.Ctx.TenantId, Name = "BLE gateways", Kind = DeviceKind.Fixed };
        gw.Antennas.Add(new Antenna { TenantId = h.Ctx.TenantId, Port = 1, LocationId = hall.Id, X = 0, Y = 0 });
        gw.Antennas.Add(new Antenna { TenantId = h.Ctx.TenantId, Port = 2, LocationId = hall.Id, X = 20, Y = 0 });
        gw.Antennas.Add(new Antenna { TenantId = h.Ctx.TenantId, Port = 3, LocationId = hall.Id, X = 0, Y = 10 });
        h.Db.Devices.Add(gw); await h.SaveAsync();
        // Tag at (5,5): distances 7.07, 15.8, 7.07 → rssi via model
        double Rssi(double d) => -45 - 10 * 2.2 * Math.Log10(d);
        var ingest = new ReadIngestionService(h.Db, h.Ctx, new TagResolver(h.Db), h.Rules, new NullLivePublisher(), null, new PositionService(h.Db));
        await ingest.IngestAsync(new ReadBatchRequest { DeviceId = gw.Id, Reads = { new() { Epc = tag.Epc, AntennaPort = 1, Rssi = Rssi(7.07) }, new() { Epc = tag.Epc, AntennaPort = 2, Rssi = Rssi(15.8) }, new() { Epc = tag.Epc, AntennaPort = 3, Rssi = Rssi(7.07) } } }, ReadSource.Fixed);
        var fresh = await h.Db.Items.FirstAsync(i => i.Id == item.Id);
        Assert.InRange(fresh.PositionX!.Value, 4, 6); Assert.InRange(fresh.PositionY!.Value, 4, 6); Assert.Equal(hall.Id, fresh.PositionLocationId);
        var plan = await new PositionService(h.Db).FloorPlanAsync(hall.Id);
        Assert.Equal(3, plan.Anchors.Count); Assert.Single(plan.Items); Assert.Equal(20, plan.WidthM);
    }
}

public class ErpFormatterAndLabelDesignTests
{
    private static DeliveryBatch Batch(IntegrationFormat f)
    {
        var loc = new Location { Name = "Ward 3", Code = "W3", Kind = LocationKind.Room };
        var party = new Party { Name = "Jane Lee", Code = "EMP001", Kind = PartyKind.Employee };
        var item = new Item { Identifier = "LT-0002", Name = "MacBook", ItemType = new ItemType { Code = "IT-ASSET" }, Cost = 2399, CurrentLocationId = loc.Id, CurrentLocation = loc, CustodianPartyId = party.Id, Attributes = new() { ["costCentre"] = "CC-200" }, Tags = { new Tag { Epc = "3034ABCD" } } };
        var ev = new ItemEvent { ItemId = item.Id, Type = ItemEventType.CustodyChanged, ToPartyId = party.Id, OccurredAt = DateTime.UtcNow };
        var mv = new ItemEvent { ItemId = item.Id, Type = ItemEventType.Moved, ToLocationId = loc.Id, OccurredAt = DateTime.UtcNow };
        var alert = new Alert { ItemId = item.Id, Severity = Severity.Critical, Message = "left the building", RaisedAt = DateTime.UtcNow };
        var ep = new IntegrationEndpoint { Name = "ERP", Format = f, Mapping = new() { ["companyCode"] = "2000", ["siteId"] = "BEDFORD" } };
        return new DeliveryBatch(ep, DateTime.UtcNow, new() { ev, mv }, new() { alert }, new() { [item.Id] = item }, new() { [loc.Id] = loc }, new() { [party.Id] = party });
    }

    [Theory]
    [InlineData(IntegrationFormat.SapAssetManagement, "AssetMaster", "\"CompanyCode\":\"2000\"")]
    [InlineData(IntegrationFormat.Dynamics365, "msdyn_customerasset", "\"msdyn_serialnumber\":\"LT-0002\"")]
    [InlineData(IntegrationFormat.Maximo, "MXASSET", "\"SITEID\":\"BEDFORD\"")]
    [InlineData(IntegrationFormat.Generic, "\"events\"", "\"identifier\":\"LT-0002\"")]
    public void Formatters_produce_vendor_shapes(IntegrationFormat f, string marker, string field)
    {
        var json = JsonSerializer.Serialize(PayloadFormatters.For(f).Build(Batch(f)), IntegrationService.SerializerFor(f));
        Assert.Contains(marker, json); Assert.Contains(field, json); Assert.Contains("left the building", json);
        if (f == IntegrationFormat.SapAssetManagement) { Assert.Contains("\"CostCenter\":\"CC-200\"", json); Assert.Contains("AssetTransfer", json); Assert.Contains("\"ToPersonnelNumber\":\"EMP001\"", json); }
        if (f == IntegrationFormat.Maximo) Assert.Contains("ASSETTRANS", json);
    }

    private class FakeTransport : IIntegrationTransport { public IDictionary<string, string>? Headers; public Task<(bool ok, string? error)> PostAsync(string url, string body, IDictionary<string, string> headers, CancellationToken ct) { Headers = headers; return Task.FromResult((true, (string?)null)); } }
    private class FakeOAuth : IOAuthTokenProvider { public Task<string> GetTokenAsync(string tokenUrl, string clientId, string clientSecret, string? scope, CancellationToken ct) => Task.FromResult($"tok-{clientId}"); }

    [Fact]
    public async Task Delivery_applies_auth_headers()
    {
        var h = new TestHost();
        var asset = await h.InstallTypeAsync("asset-management", "ASSET");
        var store = h.Loc(LocationKind.Room, "Store"); var (item, tag) = h.Item(asset, "A-1", store); await h.SaveAsync();
        await h.Ops.ProcessAsync(new OperationRequest { Type = OperationType.Count, Lines = { new() { Epc = tag.Epc } } });
        var t = new FakeTransport();
        var ep = new IntegrationEndpoint { TenantId = h.Ctx.TenantId, Name = "x", Url = "https://x", EventCursor = DateTime.UtcNow.AddMinutes(-5), AlertCursor = DateTime.UtcNow, AuthType = IntegrationAuth.OAuth2ClientCredentials, TokenUrl = "https://idp/token", ClientId = "c1", ClientSecret = "s", Format = IntegrationFormat.Dynamics365 };
        h.Db.IntegrationEndpoints.Add(ep); await h.SaveAsync();
        var svc = new IntegrationService(h.Db, t, new FakeOAuth());
        Assert.Equal(1, await svc.DispatchAsync(ep, DateTime.UtcNow.AddSeconds(1)));
        Assert.Equal("Bearer tok-c1", t.Headers!["Authorization"]);
        ep.AuthType = IntegrationAuth.Basic; ep.Username = "u"; ep.Password = "p";
        await h.Ops.ProcessAsync(new OperationRequest { Type = OperationType.Count, Lines = { new() { Epc = tag.Epc } } });
        Assert.Equal(1, await svc.DispatchAsync(ep, DateTime.UtcNow.AddSeconds(2)));
        Assert.Equal("Basic " + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("u:p")), t.Headers!["Authorization"]);
    }

    [Fact]
    public void Label_design_compiles_to_zpl_and_renders()
    {
        var d = LabelDesign.Default();
        var zpl = LabelCompiler.Compile(d);
        Assert.StartsWith("^XA", zpl); Assert.Contains("^PW813", zpl); Assert.Contains("^RFW,H,,,A^FD{epc}^FS", zpl); Assert.Contains("^BCN,112", zpl); Assert.Contains("^BQN,2,4^FDQA,{identifier}", zpl);
        var item = new Item { Name = "Drill", Identifier = "TL-1", ItemType = new ItemType { Name = "Tool" }, CurrentLocation = new Location { Name = "Crib" } };
        var rendered = LabelService.Render(item, "3034ABCD", zpl);
        Assert.Contains("^FD3034ABCD^FS", rendered); Assert.Contains("^FDQA,TL-1^FS", rendered); Assert.DoesNotContain("{", rendered);
        var round = LabelDesign.Parse(d.ToJson())!;
        Assert.Equal(d.Elements.Count, round.Elements.Count);
        var noRfid = LabelCompiler.Compile(new LabelDesign { EncodeRfid = false, Dpmm = 12, WidthMm = 50, HeightMm = 25, Elements = { new() { Type = "box", X = 1, Y = 1, Width = 48, Height = 23 } } });
        Assert.DoesNotContain("^RFW", noRfid); Assert.Contains("^PW600", noRfid); Assert.Contains("^GB576,276", noRfid);
    }
}
