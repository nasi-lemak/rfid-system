using Rfid.Protocols;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Rfid.Application.Contracts;
using Rfid.Application.Operations;
using Rfid.Application.Platform;
using Rfid.Application.Services;
using Rfid.Domain;
using Rfid.Domain.Entities;
using Rfid.Infrastructure.Persistence;
using Xunit;
using static Rfid.Domain.Entities.OperationEffectKinds;

namespace Rfid.Tests;

/// <summary>Architectural invariants introduced by the consolidation: outbox, idempotency, containers, configurable operations, RLS coverage.</summary>
public class ConsolidationTests
{
    private sealed class RecordingTransport : ILiveTransport
    {
        public List<(Guid tenant, string method, string payload)> Sent { get; } = new();
        public bool Fail { get; set; }
        public Task SendAsync(Guid tenantId, string method, object payload, CancellationToken ct = default)
        {
            if (Fail) throw new InvalidOperationException("hub down");
            Sent.Add((tenantId, method, JsonSerializer.Serialize(payload))); return Task.CompletedTask;
        }
    }
    private sealed class RecordingWebhooks : IWebhookTransport
    {
        public List<(string url, string json)> Posted { get; } = new();
        public bool Fail { get; set; }
        public Task<(bool ok, string? error)> PostJsonAsync(string url, string json, CancellationToken ct = default) { if (Fail) return Task.FromResult((false, (string?)"502 Bad Gateway")); Posted.Add((url, json)); return Task.FromResult((true, (string?)null)); }
    }

    /// <summary>Wires the real services on top of the outbox exactly as Program.cs does.</summary>
    private static (OperationProcessor ops, RuleEngine rules, RecordingTransport live, RecordingWebhooks hooks, OutboxDispatcher dispatcher) OutboxStack(TestHost h)
    {
        var outbox = new Outbox(h.Db, h.Ctx);
        var live = new RecordingTransport(); var hooks = new RecordingWebhooks();
        var publisher = new OutboxLivePublisher(outbox, live, h.Ctx);
        var rules = new RuleEngine(h.Db, h.Ctx, publisher, new OutboxWebhookDispatcher(outbox));
        var ops = new OperationProcessor(h.Db, h.Ctx, new TagResolver(h.Db), rules, publisher, new OperationDefinitions(h.Db));
        return (ops, rules, live, hooks, new OutboxDispatcher(h.Db, live, hooks, new NullNotificationSender()));
    }

    [Fact]
    public async Task Live_events_and_webhooks_are_committed_with_state_and_delivered_only_by_the_dispatcher()
    {
        var h = new TestHost();
        var tool = await h.InstallTypeAsync("tool-tracking", "TOOL");
        var a = h.Loc(LocationKind.Room, "A"); var b = h.Loc(LocationKind.Room, "B");
        var (item, tag) = h.Item(tool, "TL-1", a);
        h.Db.Rules.Add(new Rule { TenantId = h.Ctx.TenantId, Name = "notify erp", Enabled = true, Trigger = ItemEventType.Moved, Action = RuleAction.Webhook, Params = new() { ["url"] = "https://erp.example/hook" } });
        await h.SaveAsync();
        var (ops, _, live, hooks, dispatcher) = OutboxStack(h);

        await ops.ProcessAsync(new OperationRequest { Type = OperationType.Transfer, ToLocationId = b.Id, Lines = { new() { Epc = tag.Epc } } });

        // Nothing left the process during the request: side effects are rows in the same unit of work as the state change.
        Assert.Empty(live.Sent); Assert.Empty(hooks.Posted);
        Assert.False(h.Db.ChangeTracker.HasChanges());
        var pending = await h.Db.Outbox.Where(m => m.ProcessedAt == null).ToListAsync();
        Assert.Contains(pending, m => m.Kind == OutboxKinds.LiveEvent);
        Assert.Contains(pending, m => m.Kind == OutboxKinds.Webhook && m.Destination == "https://erp.example/hook");
        Assert.All(pending, m => Assert.Equal(h.Ctx.TenantId, m.TenantId));

        var stats = await dispatcher.DispatchAsync(DateTime.UtcNow);
        Assert.Equal(pending.Count, stats.Delivered);
        Assert.Contains(live.Sent, s => s.method == "event" && s.payload.Contains(item.Id.ToString()));
        Assert.Single(hooks.Posted);
        Assert.Empty(await h.Db.Outbox.Where(m => m.ProcessedAt == null).ToListAsync());
    }

    [Fact]
    public async Task Outbox_retries_with_backoff_and_dead_letters_after_max_attempts()
    {
        var h = new TestHost();
        var tool = await h.InstallTypeAsync("tool-tracking", "TOOL");
        var a = h.Loc(LocationKind.Room, "A"); var b = h.Loc(LocationKind.Room, "B");
        var (_, tag) = h.Item(tool, "TL-1", a);
        await h.SaveAsync();
        var (ops, _, live, _, dispatcher) = OutboxStack(h);
        await ops.ProcessAsync(new OperationRequest { Type = OperationType.Transfer, ToLocationId = b.Id, Lines = { new() { Epc = tag.Epc } } });

        live.Fail = true; dispatcher.MaxAttempts = 2;
        var t0 = DateTime.UtcNow;
        var first = await dispatcher.DispatchAsync(t0);
        Assert.Equal(0, first.Delivered); Assert.True(first.Failed > 0);
        var m = await h.Db.Outbox.FirstAsync();
        Assert.Null(m.ProcessedAt); Assert.Equal(1, m.Attempts); Assert.NotNull(m.NextAttemptAt); Assert.True(m.NextAttemptAt > t0); Assert.Contains("hub down", m.Error);

        Assert.Equal(0, (await dispatcher.DispatchAsync(t0)).Failed);          // not due yet – untouched
        var second = await dispatcher.DispatchAsync(m.NextAttemptAt!.Value.AddSeconds(1));
        Assert.True(second.Dead > 0);
        m = await h.Db.Outbox.FirstAsync();
        Assert.NotNull(m.ProcessedAt); Assert.Equal(2, m.Attempts); Assert.NotNull(m.Error);   // dead-lettered, kept for inspection
    }

    [Fact]
    public async Task Operation_replays_with_the_same_client_id_are_acknowledged_not_reapplied()
    {
        var h = new TestHost();
        var tool = await h.InstallTypeAsync("tool-tracking", "TOOL");
        var a = h.Loc(LocationKind.Room, "A"); var b = h.Loc(LocationKind.Room, "B");
        var (item, tag) = h.Item(tool, "TL-1", a);
        await h.SaveAsync();

        var req = new OperationRequest { Type = OperationType.Transfer, ToLocationId = b.Id, ClientId = "hh-42-0001", Lines = { new() { Epc = tag.Epc } } };
        var first = await h.Ops.ProcessAsync(req);
        var replay = await h.Ops.ProcessAsync(req);
        Assert.Equal(first.OperationId, replay.OperationId);
        Assert.Equal(1, await h.Db.Operations.CountAsync());
        Assert.Equal(1, await h.Db.ItemEvents.CountAsync(e => e.ItemId == item.Id && e.Type == ItemEventType.Moved));
        Assert.Equal("hh-42-0001", (await h.Db.Operations.SingleAsync()).ClientId);
    }

    [Fact]
    public async Task Read_batches_with_a_batch_id_are_idempotent()
    {
        var h = new TestHost();
        var tool = await h.InstallTypeAsync("tool-tracking", "TOOL");
        var dock = h.Loc(LocationKind.Zone, "Dock");
        var (_, tag) = h.Item(tool, "TL-1", null);
        await h.SaveAsync();

        var batch = new ReadBatchRequest { BatchId = "edge-7:00012", Reads = { new() { Epc = tag.Epc, LocationId = dock.Id, Rssi = -50 } } };
        var first = await h.Ingest.IngestAsync(batch, ReadSource.Fixed);
        var readsAfterFirst = await h.Db.TagReads.CountAsync(); var eventsAfterFirst = await h.Db.ItemEvents.CountAsync();
        var replay = await h.Ingest.IngestAsync(batch, ReadSource.Fixed);

        Assert.False(first.Duplicate); Assert.True(replay.Duplicate);
        Assert.Equal(first.Resolved, replay.Resolved); Assert.Equal(first.Events, replay.Events);
        Assert.Equal(readsAfterFirst, await h.Db.TagReads.CountAsync());
        Assert.Equal(eventsAfterFirst, await h.Db.ItemEvents.CountAsync());
        Assert.Single(await h.Db.IdempotencyKeys.Where(k => k.Scope == IdempotencyScopes.ReadBatch).ToListAsync());

        // A different batch id from the same source is new work.
        var next = await h.Ingest.IngestAsync(new ReadBatchRequest { BatchId = "edge-7:00013", Reads = { new() { Epc = tag.Epc, LocationId = dock.Id } } }, ReadSource.Fixed);
        Assert.False(next.Duplicate);
    }

    [Fact]
    public async Task Nested_containers_move_together_fire_rules_for_children_and_refuse_cycles()
    {
        var h = new TestHost();
        var tool = await h.InstallTypeAsync("tool-tracking", "TOOL");
        var kitType = await h.Db.ItemTypes.FirstAsync(t => t.Code == "TOOL-KIT");
        var crib = h.Loc(LocationKind.Room, "Crib"); var hangar = h.Loc(LocationKind.Room, "Hangar");
        var (tl, tlTag) = h.Item(tool, "TL-1", crib);
        var (kit, kitTag) = h.Item(kitType, "KIT-1", crib);
        var (cage, cageTag) = h.Item(kitType, "CAGE-1", crib);
        h.Db.Rules.Add(new Rule { TenantId = h.Ctx.TenantId, Name = "tool moved", Enabled = true, Trigger = ItemEventType.Moved, Action = RuleAction.CreateAlert, Severity = Severity.Info, Conditions = { new() { Field = "itemType.code", Op = "eq", Value = "TOOL" } }, Params = new() { ["message"] = "{item.name} moved" } });
        await h.SaveAsync();

        Assert.Equal(1, (await h.Ops.ProcessAsync(new OperationRequest { Type = OperationType.Pack, ContainerItemId = kit.Id, Lines = { new() { Epc = tlTag.Epc } } })).Ok);
        Assert.Equal(1, (await h.Ops.ProcessAsync(new OperationRequest { Type = OperationType.Pack, ContainerItemId = cage.Id, Lines = { new() { Epc = kitTag.Epc } } })).Ok);

        // Cycle: the cage is now the kit's parent, so the cage cannot go into the kit (nor into the tool).
        var cycle = await h.Ops.ProcessAsync(new OperationRequest { Type = OperationType.Pack, ContainerItemId = kit.Id, Lines = { new() { Epc = cageTag.Epc } } });
        Assert.Equal(1, cycle.Rejected); Assert.Contains("cycle", cycle.Lines[0].Message);
        var self = await h.Ops.ProcessAsync(new OperationRequest { Type = OperationType.Pack, ContainerItemId = cage.Id, Lines = { new() { Epc = cageTag.Epc } } });
        Assert.Equal(1, self.Rejected);

        // Moving the outer container moves the whole subtree and every child move is a first-class event that rules see.
        var alertsBefore = await h.Db.Alerts.CountAsync();
        var move = await h.Ops.ProcessAsync(new OperationRequest { Type = OperationType.Transfer, ToLocationId = hangar.Id, Lines = { new() { Epc = cageTag.Epc } } });
        Assert.Equal(1, move.Ok);
        foreach (var id in new[] { tl.Id, kit.Id, cage.Id }) Assert.Equal(hangar.Id, (await h.Db.Items.FirstAsync(i => i.Id == id)).CurrentLocationId);
        var childMove = await h.Db.ItemEvents.FirstOrDefaultAsync(e => e.ItemId == tl.Id && e.Type == ItemEventType.Moved && e.ToLocationId == hangar.Id);
        Assert.NotNull(childMove); Assert.Equal(move.OperationId, childMove!.OperationId);
        Assert.Equal(alertsBefore + 1, await h.Db.Alerts.CountAsync());
        Assert.True(await ContainerRules.IsDescendantAsync(h.Db, tl.Id, cage.Id));
        Assert.False(await ContainerRules.IsDescendantAsync(h.Db, cage.Id, tl.Id));
    }

    [Fact]
    public async Task Template_defined_operation_is_configuration_composed_from_built_in_effects()
    {
        var h = new TestHost();
        var tray = await h.InstallTypeAsync("medical-assets", "TRAY");
        var device = await h.Db.ItemTypes.FirstAsync(t => t.Code == "MED-DEVICE");
        var sterileStore = h.Loc(LocationKind.Room, "Sterile store");
        var (t1, t1Tag) = h.Item(tray, "TRAY-1", null, state: "Decontaminated");
        var (d1, d1Tag) = h.Item(device, "PUMP-1", null);
        await h.SaveAsync();

        var defs = await h.Db.OperationDefinitions.ToListAsync();
        Assert.Contains(defs, d => d.Code == "Sterilise" && !d.IsBuiltIn && d.Vertical == "Healthcare");
        Assert.Contains(defs, d => d.Code == "Decontaminate");

        var res = await h.Ops.ProcessAsync(new OperationRequest { Operation = "Sterilise", ToLocationId = sterileStore.Id, Lines = { new() { Epc = t1Tag.Epc }, new() { Epc = d1Tag.Epc } } });
        Assert.Equal(1, res.Ok); Assert.Equal(1, res.Rejected);
        Assert.Contains("does not apply", res.Lines[1].Message);

        var fresh = await h.Db.Items.FirstAsync(i => i.Id == t1.Id);
        Assert.Equal("Sterilised", fresh.State); Assert.Equal(1, fresh.CycleCount); Assert.Equal(sterileStore.Id, fresh.CurrentLocationId);
        Assert.True(fresh.Attributes.TryGetValue("lastSterilisedAt", out var when) && DateTime.TryParse(when?.ToString(), out _), "'{now}' placeholder must be substituted");
        var op = await h.Db.Operations.SingleAsync();
        Assert.Equal("Sterilise", op.DefinitionCode); Assert.Equal(OperationType.ProcessStage, op.Type);
        var ev = await h.Db.ItemEvents.FirstAsync(e => e.ItemId == t1.Id && e.Type == ItemEventType.StateChanged);
        Assert.Equal("Sterilise", ev.Data["operation"]?.ToString());
        Assert.Equal("Sterilised", ev.ToState);

        // A second sterilisation is refused by the lifecycle (no Sterilised → ? on "Sterilise").
        var again = await h.Ops.ProcessAsync(new OperationRequest { Operation = "Sterilise", Lines = { new() { Epc = t1Tag.Epc } } });
        Assert.Equal(1, again.Rejected);
        Assert.Equal("Clean", (await h.Db.Items.FirstAsync(i => i.Id == d1.Id)).State);   // the rejected line left the device untouched
    }

    [Fact]
    public async Task Tenant_defined_operation_falls_back_to_base_type_lifecycle_and_definitions_are_validated()
    {
        var h = new TestHost();
        var tool = await h.InstallTypeAsync("tool-tracking", "TOOL");
        var crib = h.Loc(LocationKind.Room, "Crib"); var bob = h.Party("Bob");
        var (item, tag) = h.Item(tool, "TL-1", crib);
        var quick = new OperationDefinition { TenantId = h.Ctx.TenantId, Code = "QuickIssue", Name = "Quick issue", BaseType = OperationType.Issue, EventType = ItemEventType.CustodyChanged, Requires = new() { Party = true }, Effects = { new(SetCustodian, new()), new(SetDueBack, new()) }, Enabled = true };
        Assert.Empty(OperationCatalog.Validate(quick));
        h.Db.OperationDefinitions.Add(quick);
        await h.SaveAsync();

        var res = await h.Ops.ProcessAsync(new OperationRequest { Operation = "QuickIssue", PartyId = bob.Id, DueBackAt = DateTime.UtcNow.AddDays(1), Lines = { new() { Epc = tag.Epc } } });
        Assert.Equal(1, res.Ok);
        var fresh = await h.Db.Items.FirstAsync(i => i.Id == item.Id);
        Assert.Equal("CheckedOut", fresh.State);          // TOOL lifecycle has no "QuickIssue" transitions → base type Issue's apply
        Assert.Equal(bob.Id, fresh.CustodianPartyId); Assert.NotNull(fresh.DueBackAt);

        await Assert.ThrowsAsync<DomainException>(() => h.Ops.ProcessAsync(new OperationRequest { Operation = "QuickIssue", Lines = { new() { Epc = tag.Epc } } }));   // party required
        await Assert.ThrowsAsync<DomainException>(() => h.Ops.ProcessAsync(new OperationRequest { Operation = "Teleport", Lines = { new() { Epc = tag.Epc } } }));     // unknown operation

        var bad = new OperationDefinition { Code = "Bad", Effects = { new("Teleport", new()), new(Pack, new()) } };
        var errors = OperationCatalog.Validate(bad);
        Assert.Contains(errors, e => e.Contains("Unknown effect")); Assert.Contains(errors, e => e.Contains("container"));
        Assert.Contains(OperationCatalog.Validate(new OperationDefinition { Code = "Transfer", Effects = { new(Move, new()) }, Requires = new() { ToLocation = true } }), e => e.Contains("built-in"));
        Assert.Contains(OperationCatalog.Validate(new OperationDefinition { Code = "X1", BaseType = OperationType.Commission, Effects = { new(Move, new()) } }), e => e.Contains("Commission"));
    }

    [Fact]
    public void Every_built_in_operation_is_a_definition_in_the_same_vocabulary_as_custom_ones()
    {
        foreach (var t in Enum.GetValues<OperationType>())
        {
            var d = OperationCatalog.Get(t.ToString());
            Assert.NotNull(d); Assert.True(d!.IsBuiltIn); Assert.Equal(t, d.BaseType);
            Assert.All(d.Effects, e => Assert.Contains(e.Kind, All));
            if (t != OperationType.Commission) Assert.Empty(OperationCatalog.Validate(d).Where(e => !e.Contains("built-in")));
        }
        Assert.Equal(ItemEventType.Moved, OperationCatalog.DefaultEvent(OperationType.Transfer));
        Assert.Equal(ItemEventType.CustodyChanged, OperationCatalog.DefaultEvent(OperationType.Issue));
    }

    [Fact]
    public async Task Rule_set_state_cannot_write_a_state_the_lifecycle_does_not_know()
    {
        var h = new TestHost();
        var tool = await h.InstallTypeAsync("tool-tracking", "TOOL");
        var a = h.Loc(LocationKind.Room, "A"); var b = h.Loc(LocationKind.Room, "B");
        var (item, tag) = h.Item(tool, "TL-1", a);
        h.Db.Rules.Add(new Rule { TenantId = h.Ctx.TenantId, Name = "bad state", Enabled = true, Trigger = ItemEventType.Moved, Action = RuleAction.SetState, Params = new() { ["state"] = "Teleported" } });
        h.Db.Rules.Add(new Rule { TenantId = h.Ctx.TenantId, Name = "repair on move", Enabled = true, Trigger = ItemEventType.Moved, Action = RuleAction.SetState, Params = new() { ["state"] = "UnderRepair" } });
        await h.SaveAsync();
        await h.Ops.ProcessAsync(new OperationRequest { Type = OperationType.Transfer, ToLocationId = b.Id, Lines = { new() { Epc = tag.Epc } } });
        var fresh = await h.Db.Items.FirstAsync(i => i.Id == item.Id);
        Assert.Equal("UnderRepair", fresh.State);
        Assert.DoesNotContain(await h.Db.ItemEvents.Where(e => e.ItemId == item.Id).ToListAsync(), e => e.ToState == "Teleported");
    }

    [Fact]
    public async Task Presence_exit_uses_the_same_direction_vocabulary_as_portal_reads()
    {
        var h = new TestHost();
        var tool = await h.InstallTypeAsync("tool-tracking", "TOOL");
        var site = h.Loc(LocationKind.Site, "Site"); var zone = h.Loc(LocationKind.Zone, "Bay 1", site);
        var (item, tag) = h.Item(tool, "TL-1", null);
        await h.SaveAsync();
        var t0 = DateTime.UtcNow.AddMinutes(-30);
        await h.Ingest.IngestAsync(new ReadBatchRequest { Reads = { new() { Epc = tag.Epc, LocationId = zone.Id, ReadAt = t0 } } }, ReadSource.Fixed);
        Assert.Equal(zone.Id, (await h.Db.Items.FirstAsync(i => i.Id == item.Id)).CurrentLocationId);

        var presence = new PresenceService(h.Db, h.Ctx, h.Rules, new NullLivePublisher());
        Assert.Equal(1, await presence.SweepAsync(DateTime.UtcNow));
        var exit = await h.Db.ItemEvents.OrderByDescending(e => e.OccurredAt).FirstAsync(e => e.ItemId == item.Id && e.Type == ItemEventType.Moved);
        Assert.Equal("Out", exit.Data["direction"]?.ToString());
        Assert.Equal(site.Id, exit.ToLocationId);
    }

    [Fact]
    public void Every_tenant_entity_table_is_covered_by_a_row_level_security_policy()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=localhost;Database=model-only").Options;
        using var db = new AppDbContext(opts, new FixedContext());
        var tables = RowLevelSecurity.TenantTables(db.Model);
        var tenantTypes = typeof(TenantEntity).Assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(TenantEntity)) && !t.IsAbstract).ToList();
        Assert.NotEmpty(tenantTypes);
        foreach (var t in tenantTypes)
        {
            var et = db.Model.FindEntityType(t);
            Assert.True(et != null, $"{t.Name} is not mapped");
            Assert.Contains(tables, x => x.table == et!.GetTableName());
        }
        Assert.DoesNotContain(tables, x => x.table == "tenants");
        var sql = RowLevelSecurity.EnableSql(tables);
        Assert.Equal(tables.Count, sql.Split("CREATE POLICY").Length - 1);
        Assert.Contains("ENABLE ROW LEVEL SECURITY", sql);
        Assert.Equal(RowLevelSecurity.SystemScope, TenantConnectionInterceptor.Scope(Guid.Empty));
        var id = Guid.NewGuid(); Assert.Equal(id.ToString(), TenantConnectionInterceptor.Scope(id));
    }
}
