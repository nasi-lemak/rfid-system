using Microsoft.EntityFrameworkCore;
using Rfid.Application.Contracts;
using Rfid.Application.Integrations;
using Rfid.Application.Operations;
using Rfid.Application.Platform;
using Rfid.Application.Services;
using Rfid.Domain;
using Rfid.Domain.Entities;
using Xunit;

namespace Rfid.Tests;

/// <summary>Foundation roadmap items: schedule rules, RunOperation through the outbox, integration filters, EPCIS vocabulary from definitions.</summary>
public class FoundationTests
{
    /// <summary>Test-side runner: executes the rule-requested operation with the host's processor (the API opens a tenant scope instead).</summary>
    private sealed class DirectRunner : IOperationRunner
    {
        private readonly TestHost _h; public DirectRunner(TestHost h) => _h = h;
        public Task<OperationResult> RunAsync(Guid tenantId, OperationRequest request, CancellationToken ct = default) => _h.Ops.ProcessAsync(request, ct);
    }
    private static OutboxDispatcher Dispatcher(TestHost h) => new(h.Db, new IOutboxHandler[] { new RunOperationOutboxHandler(new DirectRunner(h)) });

    [Fact]
    public async Task Schedule_rule_alerts_once_per_item_and_respects_its_interval()
    {
        var h = new TestHost();
        var tool = await h.InstallTypeAsync("tool-tracking", "TOOL");
        var crib = h.Loc(LocationKind.Room, "Crib");
        var (stale, _) = h.Item(tool, "TL-OLD", crib); stale.LastSeenAt = DateTime.UtcNow.AddHours(-72);
        var (fresh, _) = h.Item(tool, "TL-NEW", crib); fresh.LastSeenAt = DateTime.UtcNow.AddMinutes(-5);
        h.Db.Rules.Add(new Rule { TenantId = h.Ctx.TenantId, Name = "not seen 48h", Kind = RuleKind.Schedule, IntervalMinutes = 30, Action = RuleAction.CreateAlert, Severity = Severity.Warning,
            Conditions = { new() { Field = "item.hoursSinceSeen", Op = "gt", Value = 48 } }, Params = new() { ["message"] = "{item.name} not seen for {item.hoursSinceSeen} h" } });
        await h.SaveAsync();

        var t0 = DateTime.UtcNow;
        var run1 = await h.Rules.EvaluateScheduledAsync(t0);
        Assert.Equal(1, run1.RulesRun); Assert.Equal(1, run1.ItemsMatched); Assert.Equal(1, run1.AlertsRaised);
        var alert = await h.Db.Alerts.SingleAsync();
        Assert.Equal(stale.Id, alert.ItemId); Assert.StartsWith("TL-OLD not seen for", alert.Message);

        Assert.Equal(0, (await h.Rules.EvaluateScheduledAsync(t0.AddMinutes(10))).RulesRun);           // not due yet
        var run3 = await h.Rules.EvaluateScheduledAsync(t0.AddMinutes(31));
        Assert.Equal(1, run3.RulesRun); Assert.Equal(1, run3.ItemsMatched); Assert.Equal(0, run3.AlertsRaised); Assert.Equal(1, run3.Skipped);   // still open → no storm
        Assert.Equal(1, await h.Db.Alerts.CountAsync());

        alert.Status = AlertStatus.Closed; await h.SaveAsync();
        Assert.Equal(1, (await h.Rules.EvaluateScheduledAsync(t0.AddMinutes(62))).AlertsRaised);           // closed → may alert again
    }

    [Fact]
    public async Task Schedule_rule_can_run_an_operation_which_executes_after_commit_through_the_outbox()
    {
        var h = new TestHost();
        var linen = await h.InstallTypeAsync("linen-laundry", "LINEN");
        var store = h.Loc(LocationKind.Room, "Linen store");
        var (worn, wornTag) = h.Item(linen, "SHEET-OLD", store); worn.CycleCount = linen.MaxCycles!.Value;
        var (ok, _) = h.Item(linen, "SHEET-NEW", store); ok.CycleCount = 3;
        h.Db.Rules.Add(new Rule { TenantId = h.Ctx.TenantId, Name = "retire at max cycles", Kind = RuleKind.Schedule, IntervalMinutes = 60, Action = RuleAction.RunOperation,
            Conditions = { new() { Field = "item.cyclesRemaining", Op = "lte", Value = 0 } }, Params = new() { ["operation"] = "Dispose", ["reference"] = "auto-retire {item.identifier}" } });
        await h.SaveAsync();

        var run = await h.Rules.EvaluateScheduledAsync(DateTime.UtcNow);
        Assert.Equal(1, run.ItemsMatched);
        Assert.Equal(ItemStatus.Active, (await h.Db.Items.FirstAsync(i => i.Id == worn.Id)).Status);        // nothing ran inside the sweep
        var msg = await h.Db.Outbox.SingleAsync(m => m.Kind == OutboxKinds.RunOperation);
        Assert.Null(msg.ProcessedAt);

        var stats = await Dispatcher(h).DispatchAsync(DateTime.UtcNow);
        Assert.Equal(1, stats.Delivered);
        var disposed = await h.Db.Items.FirstAsync(i => i.Id == worn.Id);
        Assert.Equal(ItemStatus.Disposed, disposed.Status);
        var op = await h.Db.Operations.SingleAsync();
        Assert.Equal("Dispose", op.DefinitionCode); Assert.Equal("auto-retire SHEET-OLD", op.Reference); Assert.StartsWith("rule:", op.ClientId);
        Assert.Equal(ItemStatus.Active, (await h.Db.Items.FirstAsync(i => i.Id == ok.Id)).Status);

        // Next sweep: the disposed item is out of scope, so the rule is quiet and no second operation is queued.
        var again = await h.Rules.EvaluateScheduledAsync(DateTime.UtcNow.AddHours(2));
        Assert.Equal(0, again.ItemsMatched);
        Assert.Equal(1, await h.Db.Outbox.CountAsync(m => m.Kind == OutboxKinds.RunOperation));
    }

    [Fact]
    public async Task Event_rule_run_operation_is_deferred_and_idempotent_per_message()
    {
        var h = new TestHost();
        var tool = await h.InstallTypeAsync("tool-tracking", "TOOL");
        var crib = h.Loc(LocationKind.Room, "Crib"); var workshop = h.Loc(LocationKind.Room, "Workshop");
        var (item, tag) = h.Item(tool, "TL-1", crib);
        h.Db.Rules.Add(new Rule { TenantId = h.Ctx.TenantId, Name = "failed inspection → workshop", Kind = RuleKind.Event, Trigger = ItemEventType.Inspected, Action = RuleAction.RunOperation,
            Conditions = { new() { Field = "data.result", Op = "contains", Value = "Fail" } }, Params = new() { ["operation"] = "Maintain", ["toLocationId"] = workshop.Id.ToString() } });
        await h.SaveAsync();

        var inspect = await h.Ops.ProcessAsync(new OperationRequest { Type = OperationType.Inspect, TargetState = "Failed", Lines = { new() { Epc = tag.Epc } } });
        Assert.Equal(1, inspect.Ok);
        Assert.Equal(crib.Id, (await h.Db.Items.FirstAsync(i => i.Id == item.Id)).CurrentLocationId);   // deferred: not moved inside the inspection
        var dispatcher = Dispatcher(h);
        Assert.Equal(1, (await dispatcher.DispatchAsync(DateTime.UtcNow)).Delivered);
        var moved = await h.Db.Items.FirstAsync(i => i.Id == item.Id);
        Assert.Equal(workshop.Id, moved.CurrentLocationId); Assert.Equal("UnderRepair", moved.State);
        Assert.Equal(0, (await dispatcher.DispatchAsync(DateTime.UtcNow)).Delivered);                     // processed once
        Assert.Equal(1, await h.Db.Operations.CountAsync(o => o.DefinitionCode == "Maintain"));
    }

    [Fact]
    public async Task Integration_endpoint_filters_by_item_type_and_site()
    {
        var h = new TestHost();
        var tool = await h.InstallTypeAsync("tool-tracking", "TOOL");
        var kit = await h.Db.ItemTypes.FirstAsync(t => t.Code == "TOOL-KIT");
        var siteA = h.Loc(LocationKind.Site, "A"); var a1 = h.Loc(LocationKind.Room, "A1", siteA); var a2 = h.Loc(LocationKind.Room, "A2", siteA);
        var siteB = h.Loc(LocationKind.Site, "B"); var b1 = h.Loc(LocationKind.Room, "B1", siteB); var b2 = h.Loc(LocationKind.Room, "B2", siteB);
        var (toolA, toolATag) = h.Item(tool, "TL-A", a1); var (kitA, kitATag) = h.Item(kit, "KIT-A", a1); var (toolB, toolBTag) = h.Item(tool, "TL-B", b1);
        var start = DateTime.UtcNow.AddMinutes(-1);
        var byType = new IntegrationEndpoint { TenantId = h.Ctx.TenantId, Name = "tools only", Url = "https://x/tools", EventCursor = start, AlertCursor = start, ItemTypeCodes = { "TOOL" } };
        var bySite = new IntegrationEndpoint { TenantId = h.Ctx.TenantId, Name = "site B", Url = "https://x/b", EventCursor = start, AlertCursor = start, SiteLocationId = siteB.Id };
        h.Db.IntegrationEndpoints.AddRange(byType, bySite);
        await h.SaveAsync();
        await h.Ops.ProcessAsync(new OperationRequest { Type = OperationType.Transfer, ToLocationId = a2.Id, Lines = { new() { Epc = toolATag.Epc }, new() { Epc = kitATag.Epc } } });
        await h.Ops.ProcessAsync(new OperationRequest { Type = OperationType.Transfer, ToLocationId = b2.Id, Lines = { new() { Epc = toolBTag.Epc } } });

        var svc = new IntegrationService(h.Db, h.Outbox);
        var now = DateTime.UtcNow.AddSeconds(1);
        Assert.Equal(2, await svc.EnqueueAsync(byType, now));      // TL-A and TL-B moved; KIT-A excluded
        Assert.Equal(1, await svc.EnqueueAsync(bySite, now));      // only the move inside site B
        var msgs = await h.Db.Outbox.Where(m => m.Kind == OutboxKinds.Integration).ToListAsync();
        var toolsBody = msgs.Single(m => m.Destination == "https://x/tools").Payload;
        Assert.Contains("TL-A", toolsBody); Assert.Contains("TL-B", toolsBody); Assert.DoesNotContain("KIT-A", toolsBody);
        var siteBody = msgs.Single(m => m.Destination == "https://x/b").Payload;
        Assert.Contains("TL-B", siteBody); Assert.DoesNotContain("TL-A", siteBody);
    }

    [Fact]
    public async Task Epcis_vocabulary_comes_from_the_operation_definition_and_maps_back_on_capture()
    {
        var h = new TestHost();
        var tray = await h.InstallTypeAsync("medical-assets", "TRAY");
        var store = h.Loc(LocationKind.Room, "Sterile store");
        var (t1, t1Tag) = h.Item(tray, "TRAY-1", null, state: "Decontaminated");
        await h.SaveAsync();
        await h.Ops.ProcessAsync(new OperationRequest { Operation = "Sterilise", ToLocationId = store.Id, Lines = { new() { Epc = t1Tag.Epc } } });
        await h.Ops.ProcessAsync(new OperationRequest { Type = OperationType.Receive, ToLocationId = store.Id, Lines = { new() { Epc = t1Tag.Epc } } });

        var events = await h.Db.ItemEvents.Where(e => e.ItemId == t1.Id).OrderBy(e => e.CreatedAt).ToListAsync();
        var sterilised = events.First(e => e.Type == ItemEventType.StateChanged);
        Assert.Equal(("sterilizing", "sterile"), (EpcisService.Cbv(sterilised).bizStep, EpcisService.Cbv(sterilised).disposition));
        var received = events.First(e => e.Type == ItemEventType.Moved && e.Data.ContainsKey("bizStep") && e.Data["bizStep"]!.ToString() == "receiving");
        Assert.Equal("receiving", EpcisService.Cbv(received).bizStep);
        // Reader-produced events carry no vocabulary and keep the type default.
        Assert.Equal("storing", EpcisService.Cbv(new ItemEvent { Type = ItemEventType.Moved }).bizStep);

        var epcis = new EpcisService(h.Db, h.Ctx, definitions: new OperationDefinitions(h.Db));
        Assert.Equal("Sterilise", await epcis.OperationForBizStepAsync("sterilizing", default));
        Assert.Equal("Receive", await epcis.OperationForBizStepAsync("receiving", default));
        Assert.Equal("Dispose", await epcis.OperationForBizStepAsync("destroying", default));
        Assert.Equal("Transfer", await epcis.OperationForBizStepAsync("unknown_step", default));
    }
}
