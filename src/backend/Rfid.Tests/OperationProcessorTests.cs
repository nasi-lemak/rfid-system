using Microsoft.EntityFrameworkCore;
using Rfid.Application.Contracts;
using Rfid.Domain;
using Rfid.Domain.Entities;
using Xunit;

namespace Rfid.Tests;

public class OperationProcessorTests
{
    [Fact]
    public async Task Issue_and_return_follow_tool_lifecycle_and_reject_invalid_transition()
    {
        var h = new TestHost();
        var tool = await h.InstallTypeAsync("tool-tracking", "TOOL");
        var crib = h.Loc(LocationKind.Room, "Crib");
        var jane = h.Party("Jane");
        var (item, tag) = h.Item(tool, "TL-1", crib);
        await h.SaveAsync();

        var issue = await h.Ops.ProcessAsync(new OperationRequest { Type = OperationType.Issue, PartyId = jane.Id, DueBackAt = DateTime.UtcNow.AddHours(4), Lines = { new() { Epc = tag.Epc } } });
        Assert.Equal(1, issue.Ok);
        Assert.Equal("CheckedOut", issue.Lines[0].NewState);
        var fresh = await h.Db.Items.FirstAsync(i => i.Id == item.Id);
        Assert.Equal(jane.Id, fresh.CustodianPartyId);
        Assert.NotNull(fresh.DueBackAt);

        var again = await h.Ops.ProcessAsync(new OperationRequest { Type = OperationType.Issue, PartyId = jane.Id, Lines = { new() { Epc = tag.Epc } } });
        Assert.Equal(1, again.Rejected);
        Assert.Contains("not allowed", again.Lines[0].Message);

        var ret = await h.Ops.ProcessAsync(new OperationRequest { Type = OperationType.Return, ToLocationId = crib.Id, Lines = { new() { Epc = tag.Epc } } });
        Assert.Equal(1, ret.Ok);
        fresh = await h.Db.Items.FirstAsync(i => i.Id == item.Id);
        Assert.Equal("InCrib", fresh.State);
        Assert.Null(fresh.CustodianPartyId);
        Assert.Null(fresh.DueBackAt);

        var events = await h.Db.ItemEvents.Where(e => e.ItemId == item.Id).OrderBy(e => e.OccurredAt).ToListAsync();
        Assert.Contains(events, e => e.Type == ItemEventType.CustodyChanged && e.ToPartyId == jane.Id);
        Assert.Contains(events, e => e.Type == ItemEventType.StateChanged && e.ToState == "CheckedOut");
    }

    [Fact]
    public async Task Commission_creates_item_and_tag_and_rejects_duplicate_epc()
    {
        var h = new TestHost();
        var asset = await h.InstallTypeAsync("asset-management", "ASSET");
        var store = h.Loc(LocationKind.Room, "Store");
        await h.SaveAsync();

        var res = await h.Ops.ProcessAsync(new OperationRequest
        {
            Type = OperationType.Commission, ToLocationId = store.Id,
            Lines = { new() { Epc = "e2801160600002000000abcd", NewItem = new() { ItemTypeId = asset.Id, Identifier = "A-1", Name = "Projector", Attributes = new() { ["model"] = "X1" } } } }
        });
        Assert.Equal(1, res.Ok);
        var tag = await h.Db.Tags.Include(t => t.Item).FirstAsync(t => t.Epc == "E2801160600002000000ABCD");
        Assert.Equal("Projector", tag.Item!.Name);
        Assert.Equal("InStorage", tag.Item.State);
        Assert.Equal(store.Id, tag.Item.CurrentLocationId);
        Assert.Equal(TagStatus.Active, tag.Status);

        var dup = await h.Ops.ProcessAsync(new OperationRequest
        {
            Type = OperationType.Commission, Lines = { new() { Epc = "E2801160600002000000ABCD", NewItem = new() { ItemTypeId = asset.Id, Identifier = "A-2", Name = "Other" } } }
        });
        Assert.Equal(1, dup.Rejected);
        Assert.Contains("already bound", dup.Lines[0].Message);
    }

    [Fact]
    public async Task Transfer_of_container_moves_packed_children()
    {
        var h = new TestHost();
        var pallet = await h.InstallTypeAsync("warehouse", "PALLET");
        var carton = await h.Db.ItemTypes.FirstAsync(t => t.Code == "CARTON");
        var recv = h.Loc(LocationKind.Zone, "Receiving");
        var rack = h.Loc(LocationKind.Rack, "Rack A");
        var (p, ptag) = h.Item(pallet, "PLT-1", recv);
        var (c1, c1tag) = h.Item(carton, "CTN-1", recv);
        var (c2, c2tag) = h.Item(carton, "CTN-2", recv);
        await h.SaveAsync();

        var pack = await h.Ops.ProcessAsync(new OperationRequest { Type = OperationType.Pack, ContainerItemId = p.Id, Lines = { new() { Epc = c1tag.Epc }, new() { Epc = c2tag.Epc } } });
        Assert.Equal(2, pack.Ok);

        var move = await h.Ops.ProcessAsync(new OperationRequest { Type = OperationType.Transfer, ToLocationId = rack.Id, Lines = { new() { Epc = ptag.Epc } } });
        Assert.Equal(1, move.Ok);
        Assert.Equal("PutAway", move.Lines[0].NewState);
        var children = await h.Db.Items.Where(i => i.ParentItemId == p.Id).ToListAsync();
        Assert.Equal(2, children.Count);
        Assert.All(children, c => Assert.Equal(rack.Id, c.CurrentLocationId));

        var unpack = await h.Ops.ProcessAsync(new OperationRequest { Type = OperationType.Unpack, ToLocationId = recv.Id, Lines = { new() { Epc = c1tag.Epc } } });
        Assert.Equal(1, unpack.Ok);
        var c1fresh = await h.Db.Items.FirstAsync(i => i.Id == c1.Id);
        Assert.Null(c1fresh.ParentItemId);
        Assert.Equal(recv.Id, c1fresh.CurrentLocationId);
    }

    [Fact]
    public async Task Linen_wash_cycle_increments_count_and_max_cycle_rule_raises_alert()
    {
        var h = new TestHost();
        var linen = await h.InstallTypeAsync("linen-laundry", "LINEN");
        linen.MaxCycles = 2;
        var laundry = h.Loc(LocationKind.Room, "Laundry");
        var ward = h.Loc(LocationKind.Room, "Ward");
        var nurse = h.Party("Nurse");
        var (sheet, tag) = h.Item(linen, "SHT-1", laundry);
        await h.SaveAsync();

        for (var cycle = 1; cycle <= 2; cycle++)
        {
            Assert.Equal(1, (await h.Ops.ProcessAsync(new OperationRequest { Type = OperationType.Issue, PartyId = nurse.Id, ToLocationId = ward.Id, Lines = { new() { Epc = tag.Epc } } })).Ok);
            Assert.Equal(1, (await h.Ops.ProcessAsync(new OperationRequest { Type = OperationType.Return, ToLocationId = laundry.Id, Lines = { new() { Epc = tag.Epc } } })).Ok);
            var wash = await h.Ops.ProcessAsync(new OperationRequest { Type = OperationType.ProcessStage, TargetState = "InWash", Lines = { new() { Epc = tag.Epc } } });
            Assert.Equal("InWash", wash.Lines[0].NewState);
            var clean = await h.Ops.ProcessAsync(new OperationRequest { Type = OperationType.ProcessStage, TargetState = "Clean", Lines = { new() { Epc = tag.Epc } } });
            Assert.Equal("Clean", clean.Lines[0].NewState);
        }
        var fresh = await h.Db.Items.FirstAsync(i => i.Id == sheet.Id);
        Assert.Equal(2, fresh.CycleCount);
        var alerts = await h.Db.Alerts.Where(a => a.ItemId == sheet.Id).ToListAsync();
        Assert.Contains(alerts, a => a.Message.Contains("retire"));
    }

    [Fact]
    public async Task Unknown_epc_is_reported_and_adjust_cannot_go_negative()
    {
        var h = new TestHost();
        var lot = await h.InstallTypeAsync("inventory", "STOCK-LOT");
        var rack = h.Loc(LocationKind.Rack, "Rack");
        var (stock, tag) = h.Item(lot, "LOT-1", rack);
        stock.Quantity = 5;
        await h.SaveAsync();

        var res = await h.Ops.ProcessAsync(new OperationRequest { Type = OperationType.Adjust, Lines = { new() { Epc = tag.Epc, Quantity = -3 }, new() { Epc = "DEADBEEF" } } });
        Assert.Equal(1, res.Ok); Assert.Equal(1, res.Unknown);
        Assert.Equal(2, (await h.Db.Items.FirstAsync(i => i.Id == stock.Id)).Quantity);
        Assert.Single(await h.Db.Alerts.ToListAsync()); // below reorder point (10)

        var neg = await h.Ops.ProcessAsync(new OperationRequest { Type = OperationType.Adjust, Lines = { new() { Epc = tag.Epc, Quantity = -10 } } });
        Assert.Equal(1, neg.Rejected);
    }

    [Fact]
    public async Task Dispose_retires_tags_and_blocks_further_operations()
    {
        var h = new TestHost();
        var asset = await h.InstallTypeAsync("asset-management", "ASSET");
        var store = h.Loc(LocationKind.Room, "Store");
        var (item, tag) = h.Item(asset, "A-9", store);
        await h.SaveAsync();
        Assert.Equal(1, (await h.Ops.ProcessAsync(new OperationRequest { Type = OperationType.Dispose, Lines = { new() { Epc = tag.Epc } } })).Ok);
        Assert.Equal(ItemStatus.Disposed, (await h.Db.Items.FirstAsync(i => i.Id == item.Id)).Status);
        Assert.Equal(TagStatus.Retired, (await h.Db.Tags.FirstAsync(t => t.Id == tag.Id)).Status);
        var after = await h.Ops.ProcessAsync(new OperationRequest { Type = OperationType.Transfer, ToLocationId = store.Id, Lines = { new() { Epc = tag.Epc } } });
        Assert.Equal(1, after.Rejected);
    }

    [Fact]
    public async Task Missing_destination_throws_domain_exception()
    {
        var h = new TestHost();
        await Assert.ThrowsAsync<DomainException>(() => h.Ops.ProcessAsync(new OperationRequest { Type = OperationType.Transfer }));
    }
}
