using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Rfid.Application.Contracts;
using Rfid.Application.Services;
using Rfid.Domain;
using Rfid.Domain.Entities;
using Rfid.Domain.Epc;
using Xunit;

namespace Rfid.Tests;

public class BarcodeResolutionTests
{
    [Fact]
    public void Gs1_element_strings_parse_in_all_three_forms()
    {
        var a = Gs1ElementString.Parse("(01)09506000134352(21)12345(10)LOT7");
        Assert.Equal("09506000134352", a["01"]); Assert.Equal("12345", a["21"]); Assert.Equal("LOT7", a["10"]);
        var b = Gs1ElementString.Parse("]d20109506000134352" + "10LOT7" + Gs1ElementString.Gs + "2112345");
        Assert.Equal("09506000134352", b["01"]); Assert.Equal("LOT7", b["10"]); Assert.Equal("12345", b["21"]);
        var c = Gs1ElementString.Parse("https://id.gs1.org/01/09506000134352/21/12345?17=261231");
        Assert.Equal("09506000134352", c["01"]); Assert.Equal("12345", c["21"]);
        var epcs = Gs1ElementString.CandidateEpcs(a).ToList();
        Assert.Contains(Sgtin96.Encode("9506000", "013435", 12345, 1), epcs); // 7-digit prefix candidate
        Assert.True(epcs.Count >= 5);
        string Kind(string c) => System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(TagResolver.Describe(c))).RootElement.GetProperty("kind").GetString()!;
        Assert.Equal("epc", Kind("3034257BF400B7800004CB2F")); Assert.Equal("gs1", Kind("(01)09506000134352(21)12345")); Assert.Equal("code", Kind("ASSET-0042"));
        Assert.Equal("ASSET-0042", TagResolver.Normalize(" ASSET-0042 ")); Assert.Equal("ABCDEF01", TagResolver.Normalize("abcdef01"));
    }

    [Fact]
    public async Task Scanned_codes_resolve_via_barcode_tag_gs1_string_and_identifier()
    {
        var h = new TestHost();
        var asset = await h.InstallTypeAsync("asset-management", "ASSET");
        var store = h.Loc(LocationKind.Room, "Store");
        var (rfidItem, tag) = h.Item(asset, "A-1", store, epc: Sgtin96.Encode("9506000", "013435", 12345, 1));
        var (bcItem, _) = h.Item(asset, "A-2", store);
        h.Db.Tags.Add(new Tag { TenantId = h.Ctx.TenantId, Epc = "BC-778899", Technology = TagTechnology.Barcode, Symbology = "Code128", ItemId = bcItem.Id, Status = TagStatus.Active });
        var (plain, _) = h.Item(asset, "PLAIN-7", store);
        await h.SaveAsync();
        var r = new TagResolver(h.Db);
        var res = await r.ResolveAsync(new[] { "(01)09506000134352(21)12345", "BC-778899", "PLAIN-7", "NOPE-1" });
        Assert.Equal(rfidItem.Id, res["(01)09506000134352(21)12345"].ItemId);
        Assert.Equal(bcItem.Id, res["BC-778899"].ItemId);
        Assert.Equal(plain.Id, res["PLAIN-7"].ItemId); // identifier printed as a barcode → the item (via its tag)
        Assert.False(res.ContainsKey("NOPE-1"));
        // Hybrid operation: a barcode line and an RFID line in one transfer.
        var lab = h.Loc(LocationKind.Room, "Lab"); await h.SaveAsync();
        var op = await h.Ops.ProcessAsync(new OperationRequest { Type = OperationType.Transfer, ToLocationId = lab.Id, Lines = { new() { Epc = "BC-778899" }, new() { Epc = tag.Epc }, new() { Epc = "PLAIN-7" } } });
        Assert.Equal(3, op.Ok);
        Assert.Equal(lab.Id, (await h.Db.Items.FirstAsync(i => i.Id == bcItem.Id)).CurrentLocationId);
    }
}

public class BillingTests
{
    [Fact]
    public async Task Custody_events_accrue_deposits_fees_and_roll_into_invoices()
    {
        var h = new TestHost();
        var keg = await h.InstallTypeAsync("returnable-assets", "KEG");
        var depot = h.Loc(LocationKind.Yard, "Depot"); var (k1, t1) = h.Item(keg, "KEG-1", depot); var (k2, t2) = h.Item(keg, "KEG-2", depot);
        var pub = h.Party("The Crown", PartyKind.Customer);
        h.Db.RateCards.Add(new RateCard { TenantId = h.Ctx.TenantId, Name = "Kegs", ItemTypeId = keg.Id, Currency = "GBP", DepositAmount = 50, CycleFee = 4, DailyFee = 1, FreeDays = 7, LateFeePerDay = 2, LossFee = 120 });
        h.Db.RateCards.Add(new RateCard { TenantId = h.Ctx.TenantId, Name = "Catch-all", Currency = "GBP", DepositAmount = 10, Priority = -1 });
        await h.SaveAsync();
        var svc = new BillingService(h.Db, h.Ctx);
        var t0 = DateTime.UtcNow.AddDays(-20);
        // Issue two kegs (due back in 10 days), return one after 12 days (late by 2, 5 billable days), dispose the other while out.
        await h.Ops.ProcessAsync(new OperationRequest { Type = OperationType.Issue, PartyId = pub.Id, DueBackAt = t0.AddDays(10), Lines = { new() { Epc = t1.Epc }, new() { Epc = t2.Epc } } });
        foreach (var e in await h.Db.ItemEvents.Where(e => e.Type == ItemEventType.CustodyChanged).ToListAsync()) e.OccurredAt = t0;
        await h.SaveAsync();
        var first = await svc.AccrueAsync(t0.AddHours(1));
        var dbg = string.Join(" | ", (await h.Db.LedgerEntries.ToListAsync()).Select(l => $"{l.Kind}:{l.Amount}:{l.ItemId}")) + " || events: " + string.Join(" | ", (await h.Db.ItemEvents.Where(e => e.Type == ItemEventType.CustodyChanged).ToListAsync()).Select(e => $"{e.ItemId} to={e.ToPartyId} from={e.FromPartyId} at={e.OccurredAt:O}"));
        Assert.True(first == 4, $"expected 4 got {first}: {dbg}"); // 2 × (deposit + cycle fee)
        await h.Ops.ProcessAsync(new OperationRequest { Type = OperationType.Return, ToLocationId = depot.Id, Lines = { new() { Epc = t1.Epc } } });
        var ret = await h.Db.ItemEvents.Where(e => e.Type == ItemEventType.CustodyChanged && e.FromPartyId == pub.Id).SingleAsync(); ret.OccurredAt = t0.AddDays(12); await h.SaveAsync();
        await h.Ops.ProcessAsync(new OperationRequest { Type = OperationType.Dispose, Lines = { new() { Epc = t2.Epc } } });
        var disp = await h.Db.ItemEvents.Where(e => e.Type == ItemEventType.Disposed).SingleAsync(); disp.OccurredAt = t0.AddDays(13); await h.SaveAsync();
        var second = await svc.AccrueAsync(t0.AddDays(14));
        var dbg2 = string.Join(" | ", (await h.Db.LedgerEntries.ToListAsync()).Select(l => $"{l.Kind}:{l.Amount}:{l.Quantity}")) + " || disposed custodian=" + (await h.Db.Items.FirstAsync(i => i.Id == k2.Id)).CustodianPartyId + " || ops due=" + string.Join(",", (await h.Db.Operations.ToListAsync()).Select(o => $"{o.Type}:{o.DueBackAt:O}"));
        Assert.True(second == 4, $"expected 4 got {second}: {dbg2}"); // refund, 5 rental days, late fee, loss fee
        var ledger = await h.Db.LedgerEntries.Where(l => l.PartyId == pub.Id).ToListAsync();
        Assert.Equal(-50, ledger.Single(l => l.Kind == LedgerKind.DepositRefund).Amount);
        Assert.Equal(5, ledger.Single(l => l.Kind == LedgerKind.DailyFee).Amount); Assert.Equal(5, ledger.Single(l => l.Kind == LedgerKind.DailyFee).Quantity);
        Assert.Equal(4, ledger.Single(l => l.Kind == LedgerKind.LateFee).Amount);
        Assert.Equal(120, ledger.Single(l => l.Kind == LedgerKind.LossFee).Amount);
        Assert.Equal(0, await svc.AccrueAsync(t0.AddDays(15))); // idempotent
        var bal = (await svc.BalancesAsync()).Single();
        Assert.Equal(100 + 8 - 50 + 5 + 4 + 120, bal.Unbilled); Assert.Equal(50, bal.DepositsHeld);
        var invoices = await svc.GenerateInvoicesAsync(t0.AddDays(-1), t0.AddDays(30));
        var inv = Assert.Single(invoices);
        Assert.StartsWith("INV-", inv.Number); Assert.Equal(187, inv.Total); Assert.Equal(-50, inv.Credits); Assert.Contains(inv.Lines, l => l.Kind == "LateFee" && l.Amount == 4);
        Assert.Equal(0, await h.Db.LedgerEntries.CountAsync(l => l.InvoiceId == null));
        await svc.SetStatusAsync(inv.Id, InvoiceStatus.Issued, DateTime.UtcNow);
        await svc.SetStatusAsync(inv.Id, InvoiceStatus.Paid, DateTime.UtcNow);
        Assert.Equal(-187, (await h.Db.LedgerEntries.SingleAsync(l => l.Kind == LedgerKind.Payment)).Amount);
        await Assert.ThrowsAsync<DomainException>(() => svc.SetStatusAsync(inv.Id, InvoiceStatus.Void, DateTime.UtcNow));
        // Rate card selection prefers the specific card.
        var other = h.Party("Other", PartyKind.Customer);
        Assert.Equal("Kegs", BillingService.Pick(await h.Db.RateCards.ToListAsync(), k1, other)!.Name);
    }
}

public class MaintenanceTests
{
    [Fact]
    public async Task Risk_reflects_cycles_inspection_failures_and_predicts_a_service_date()
    {
        var h = new TestHost();
        var tool = await h.InstallTypeAsync("tool-tracking", "TOOL");
        tool.MaxCycles = 100; tool.RequiresInspection = true; tool.InspectionIntervalDays = 30;
        var crib = h.Loc(LocationKind.Room, "Crib");
        var (worn, _) = h.Item(tool, "T-WORN", crib); worn.CycleCount = 92; worn.NextInspectionDue = DateTime.UtcNow.AddDays(-5);
        var (fresh, _) = h.Item(tool, "T-NEW", crib); fresh.CycleCount = 3; fresh.NextInspectionDue = DateTime.UtcNow.AddDays(25);
        var now = DateTime.UtcNow;
        for (var i = 0; i < 3; i++) h.Db.ItemEvents.Add(new ItemEvent { TenantId = h.Ctx.TenantId, ItemId = worn.Id, Type = ItemEventType.Inspected, OccurredAt = now.AddDays(-10 * (i + 1)), Data = new() { ["result"] = i == 2 ? "Passed" : "Failed" } });
        for (var i = 0; i < 12; i++) h.Db.ItemEvents.Add(new ItemEvent { TenantId = h.Ctx.TenantId, ItemId = worn.Id, Type = ItemEventType.StateChanged, OccurredAt = now.AddDays(-i * 7) });
        h.Db.ItemEvents.Add(new ItemEvent { TenantId = h.Ctx.TenantId, ItemId = fresh.Id, Type = ItemEventType.Inspected, OccurredAt = now.AddDays(-3), Data = new() { ["result"] = "Passed" } });
        await h.SaveAsync();
        var svc = new MaintenanceService(h.Db, h.Ctx);
        var preds = await svc.PredictAsync(now);
        var w = preds.Single(p => p.ItemId == worn.Id); var f = preds.Single(p => p.ItemId == fresh.Id);
        Assert.Equal("High", w.Level); Assert.True(w.Risk >= 70, $"worn risk {w.Risk}");
        Assert.Equal("Low", f.Level); Assert.True(f.Risk < 40, $"fresh risk {f.Risk}");
        Assert.Contains(w.Factors, x => x.Name == "Inspection failures" && x.Value > 0.5);
        Assert.Contains(w.Factors, x => x.Name == "Cycle wear" && x.Value > 0.9);
        Assert.NotNull(w.PredictedServiceAt); Assert.True(w.PredictedServiceAt <= now.AddDays(1), "overdue inspection → service now");
        Assert.Equal(preds[0].ItemId, worn.Id);
        var (forecasts, alerts) = await svc.RunAsync(now);
        Assert.Equal(2, forecasts); Assert.Equal(1, alerts);
        Assert.Equal(1, await h.Db.Alerts.CountAsync(a => a.Source == "maintenance" && a.ItemId == worn.Id));
        var (_, alerts2) = await svc.RunAsync(now.AddHours(1)); Assert.Equal(0, alerts2); // no duplicate alert
        Assert.Single(await svc.TrendAsync(worn.Id)); // one snapshot per day
    }
}

public class EpcisTests
{
    [Fact]
    public async Task Events_export_as_epcis_2_and_captures_apply_reads_and_transfers()
    {
        var h = new TestHost();
        var asset = await h.InstallTypeAsync("asset-management", "ASSET");
        var store = h.Loc(LocationKind.Room, "Store"); store.Attributes["sgln"] = "0614141.00001.0";
        var dock = h.Loc(LocationKind.Dock, "Dock", null, new() { ["sgln"] = "0614141.00002.0" });
        var epc = Sgtin96.Encode("0614141", "812345", 6789, 1);
        var (item, _) = h.Item(asset, "A-1", store, epc: epc);
        var customer = h.Party("Acme", PartyKind.Customer); await h.SaveAsync();
        await h.Ops.ProcessAsync(new OperationRequest { Type = OperationType.Dispatch, ToLocationId = dock.Id, PartyId = customer.Id, Reference = "DESADV-42", Lines = { new() { Epc = epc } } });
        var svc = new EpcisService(h.Db, h.Ctx, h.Ingest, h.Ops) { BaseUrl = "https://rfid.example" };
        var (doc, total) = await svc.QueryAsync(new EpcisService.Query(Epc: epc));
        Assert.True(total >= 1);
        var ev = doc["epcisBody"]!["eventList"]![0]!;
        Assert.Equal("ObjectEvent", ev["type"]!.ToString()); Assert.Equal("shipping", ev["bizStep"]!.ToString()); Assert.Equal("in_transit", ev["disposition"]!.ToString());
        Assert.Equal("urn:epc:id:sgtin:0614141.812345.6789", ev["epcList"]![0]!.ToString());
        Assert.Equal("urn:epc:id:sgln:0614141.00002.0", ev["readPoint"]!["id"]!.ToString());
        Assert.Equal("DESADV-42", ev["bizTransactionList"]![0]!["bizTransaction"]!.ToString());
        Assert.Contains("/parties/", ev["destinationList"]![0]!["destination"]!.ToString());
        var (filtered, n) = await svc.QueryAsync(new EpcisService.Query(BizStep: "urn:epcglobal:cbv:bizstep:receiving", Epc: epc)); Assert.Equal(0, n);
        // Capture: an OBSERVE at the store (by SGLN) becomes a read; a receiving ADD becomes a Receive operation back to the store.
        var capture = JsonNode.Parse("""
        {"type":"EPCISDocument","schemaVersion":"2.0","epcisBody":{"eventList":[
          {"type":"ObjectEvent","eventID":"urn:uuid:11111111-1111-1111-1111-111111111111","eventTime":"2026-09-16T10:00:00Z","action":"OBSERVE","bizStep":"inspecting","epcList":["urn:epc:id:sgtin:0614141.812345.6789"],"readPoint":{"id":"urn:epc:id:sgln:0614141.00002.0"}},
          {"type":"ObjectEvent","eventID":"urn:uuid:22222222-2222-2222-2222-222222222222","eventTime":"2026-09-16T11:00:00Z","action":"ADD","bizStep":"urn:epcglobal:cbv:bizstep:receiving","epcList":["urn:epc:id:sgtin:0614141.812345.6789"],"bizLocation":{"id":"urn:epc:id:sgln:0614141.00001.0"}},
          {"type":"ObjectEvent","eventID":"urn:uuid:33333333-3333-3333-3333-333333333333","eventTime":"2026-09-16T11:00:00Z","action":"OBSERVE","bizStep":"inspecting","epcList":["urn:epc:id:sgtin:0614141.999999.1"],"readPoint":{"id":"urn:epc:id:sgln:0614141.00001.0"}}
        ]}}
        """)!;
        var r = await svc.CaptureAsync(capture);
        Assert.Equal(3, r.Events); Assert.Equal(2, r.Applied); Assert.Equal(1, r.Rejected);
        Assert.Equal(store.Id, (await h.Db.Items.FirstAsync(i => i.Id == item.Id)).CurrentLocationId);
        Assert.True(await h.Db.TagReads.AnyAsync(t => t.LocationId == dock.Id));
        Assert.Equal("Partial", (await h.Db.EpcisCaptures.SingleAsync()).Status);
        Assert.Equal(epc, EpcisService.UrnToEpc("urn:epc:id:sgtin:0614141.812345.6789"));
        Assert.Equal("BC-1", EpcisService.UrnToEpc("urn:epc:id:code:BC-1"));
    }
}
