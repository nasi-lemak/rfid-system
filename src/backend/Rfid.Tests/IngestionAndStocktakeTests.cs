using Microsoft.EntityFrameworkCore;
using Rfid.Application.Contracts;
using Rfid.Domain;
using Rfid.Domain.Entities;
using Xunit;

namespace Rfid.Tests;

public class IngestionAndStocktakeTests
{
    [Fact]
    public async Task Exit_gate_read_of_checked_out_tool_raises_FOD_alert_and_moves_item_out()
    {
        var h = new TestHost();
        var tool = await h.InstallTypeAsync("tool-tracking", "TOOL");
        var site = h.Loc(LocationKind.Site, "Site");
        var hangar = h.Loc(LocationKind.Building, "Hangar", site);
        var gate = h.Loc(LocationKind.Gate, "Exit", hangar);
        var (item, tag) = h.Item(tool, "TL-7", hangar, "CheckedOut");
        var reader = new Device { TenantId = h.Ctx.TenantId, Name = "Gate", Kind = DeviceKind.Gate };
        reader.Antennas.Add(new Antenna { TenantId = h.Ctx.TenantId, Port = 1, LocationId = gate.Id, Direction = AntennaDirection.Out });
        h.Db.Devices.Add(reader);
        await h.SaveAsync();

        var res = await h.Ingest.IngestAsync(new ReadBatchRequest { DeviceId = reader.Id, Reads = { new() { Epc = tag.Epc, AntennaPort = 1, Rssi = -55 }, new() { Epc = "UNKNOWN01" } } }, ReadSource.Fixed);
        Assert.Equal(2, res.Received); Assert.Equal(1, res.Resolved); Assert.Equal(1, res.Unknown);
        Assert.Equal(1, res.Events); Assert.Equal(1, res.Alerts);
        var alert = await h.Db.Alerts.SingleAsync();
        Assert.Equal(Severity.Critical, alert.Severity);
        Assert.Contains("exit gate", alert.Message);
        var fresh = await h.Db.Items.FirstAsync(i => i.Id == item.Id);
        Assert.Equal(hangar.Id, fresh.CurrentLocationId); // Out of the gate zone => at its parent
        Assert.Equal(2, await h.Db.TagReads.CountAsync());
    }

    [Fact]
    public async Task Repeated_reads_at_same_location_are_debounced()
    {
        var h = new TestHost();
        var asset = await h.InstallTypeAsync("asset-management", "ASSET");
        var room = h.Loc(LocationKind.Room, "Room");
        var (item, tag) = h.Item(asset, "A-1", room);
        var reader = new Device { TenantId = h.Ctx.TenantId, Name = "Ceiling", Kind = DeviceKind.Fixed };
        reader.Antennas.Add(new Antenna { TenantId = h.Ctx.TenantId, Port = 1, LocationId = room.Id });
        h.Db.Devices.Add(reader);
        await h.SaveAsync();

        var first = await h.Ingest.IngestAsync(new ReadBatchRequest { DeviceId = reader.Id, Reads = { new() { Epc = tag.Epc } } }, ReadSource.Fixed);
        var second = await h.Ingest.IngestAsync(new ReadBatchRequest { DeviceId = reader.Id, Reads = { new() { Epc = tag.Epc } } }, ReadSource.Fixed);
        Assert.Equal(1, first.Events);
        Assert.Equal(0, second.Events);
        Assert.Equal(ItemEventType.Seen, (await h.Db.ItemEvents.SingleAsync()).Type);
    }

    [Fact]
    public async Task Stocktake_reconciles_found_missing_unexpected_and_apply_updates_items()
    {
        var h = new TestHost();
        var asset = await h.InstallTypeAsync("asset-management", "ASSET");
        var site = h.Loc(LocationKind.Site, "Site");
        var office = h.Loc(LocationKind.Room, "Office", site);
        var store = h.Loc(LocationKind.Room, "Store", site);
        var (a1, t1) = h.Item(asset, "A-1", office);
        var (a2, t2) = h.Item(asset, "A-2", office);
        var (a3, t3) = h.Item(asset, "A-3", office);
        var (elsewhere, t4) = h.Item(asset, "A-4", store);
        await h.SaveAsync();

        var st = await h.Stocktakes.CreateAsync("Office audit", office.Id, null);
        Assert.Equal(3, st.ExpectedCount);

        var s = await h.Stocktakes.AddScansAsync(st.Id, new StocktakeScanRequest { Epcs = { t1.Epc, t2.Epc, t4.Epc, "FFFF0000" } });
        Assert.Equal(2, s.Found); Assert.Equal(1, s.Unexpected); Assert.Equal(1, s.Unknown); Assert.Equal(1, s.Missing);

        s = await h.Stocktakes.ReconcileAsync(st.Id);
        Assert.Equal(StocktakeStatus.Reconciled, s.Status); Assert.Equal(1, s.Missing);

        s = await h.Stocktakes.ApplyAsync(st.Id);
        Assert.Equal(StocktakeStatus.Applied, s.Status);
        Assert.Equal(ItemStatus.Missing, (await h.Db.Items.FirstAsync(i => i.Id == a3.Id)).Status);
        Assert.Equal(office.Id, (await h.Db.Items.FirstAsync(i => i.Id == elsewhere.Id)).CurrentLocationId);
        Assert.Contains(await h.Db.Alerts.ToListAsync(), a => a.ItemId == a3.Id && a.Message.Contains("not found"));
    }

    [Fact]
    public async Task Template_provisioning_is_idempotent()
    {
        var h = new TestHost();
        await h.InstallTypeAsync("retail", "SKU-ITEM");
        var again = await h.Templates.ApplyAsync("retail");
        Assert.Equal(0, again.ItemTypesCreated); Assert.Equal(0, again.RulesCreated); Assert.True(again.Skipped > 0);
        Assert.Equal(2, await h.Db.ItemTypes.CountAsync());
    }
}
