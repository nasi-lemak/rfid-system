using Microsoft.EntityFrameworkCore;
using Rfid.Application.Contracts;
using Rfid.Application.Devices;
using Rfid.Application.Operations;
using Rfid.Domain;
using Rfid.Domain.Entities;
using Rfid.Edge;
using Rfid.Protocols;
using Xunit;

namespace Rfid.Tests;

public class WorkflowAndEdgeConfigTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rfid-edge-tests", Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public async Task Template_installs_workflows_and_steps_run_as_operations_with_a_shared_run_id()
    {
        var h = new TestHost();
        var tray = await h.InstallTypeAsync("medical-assets", "TRAY");
        var wf = await h.Db.Workflows.SingleAsync(w => w.Code == "cssd-reprocess");
        Assert.Equal(3, wf.Steps.Count); Assert.Equal("Healthcare", wf.Vertical); Assert.Contains("TRAY", wf.ItemTypeCodes);
        Assert.Equal(new[] { "Return", "Decontaminate", "Sterilise" }, wf.Steps.Select(s => s.Operation));

        var theatre = h.Loc(LocationKind.Room, "Theatre 1"); var store = h.Loc(LocationKind.Room, "Sterile store"); var nurse = h.Party("Theatre nurse");
        var (t1, tag) = h.Item(tray, "TRAY-1", theatre, state: "Sterilised"); t1.CustodianPartyId = nurse.Id; t1.State = "InUse";
        await h.SaveAsync();

        // The handheld runs each step as an ordinary operation stamped with the run id, code and step key (offline-safe through the same queue).
        var run = "hh1-" + Guid.NewGuid().ToString("N")[..8];
        foreach (var step in wf.Steps)
        {
            var r = await h.Ops.ProcessAsync(new OperationRequest { Operation = step.Operation, WorkflowRunId = run, WorkflowCode = wf.Code, WorkflowStep = step.Key, ToLocationId = step.Key == "sterilise" ? store.Id : null, ClientId = $"{run}:{step.Key}", Lines = { new() { Epc = tag.Epc } } });
            Assert.Equal(1, r.Ok);
        }
        var fresh = await h.Db.Items.FirstAsync(i => i.Id == t1.Id);
        Assert.Equal("Sterilised", fresh.State); Assert.Equal(store.Id, fresh.CurrentLocationId); Assert.Null(fresh.CustodianPartyId);

        var svc = new WorkflowService(h.Db);
        var runs = await svc.RunsAsync();
        var summary = Assert.Single(runs);
        Assert.Equal(run, summary.RunId); Assert.Equal("CSSD reprocessing", summary.WorkflowName); Assert.Equal(3, summary.StepsDone); Assert.Equal(3, summary.StepsTotal); Assert.True(summary.Completed); Assert.Equal(3, summary.Ok);
        var detail = await svc.RunAsync(run);
        Assert.NotNull(detail); Assert.Equal(new[] { "return", "decontaminate", "sterilise" }, detail!.Value.operations.Select(o => o.WorkflowStep));
        Assert.Equal(3, await h.Db.Operations.CountAsync(o => o.WorkflowRunId == run));
    }

    [Fact]
    public async Task Workflow_definitions_are_validated_against_operation_definitions()
    {
        var h = new TestHost();
        await h.InstallTypeAsync("tool-tracking", "TOOL");
        var ops = await new OperationDefinitions(h.Db).ListAsync();
        var ok = new WorkflowDefinition { Code = "quick-check", Name = "Quick check", Steps = { new() { Key = "a", Title = "Return", Operation = "Return" }, new() { Key = "b", Title = "Calibrate", Operation = "Calibrate", Optional = true } } };
        Assert.Empty(WorkflowCatalog.Validate(ok, ops));

        var bad = new WorkflowDefinition { Code = "x", Name = "", Steps = { new() { Key = "a", Operation = "Teleport" }, new() { Key = "a", Operation = "Commission" }, new() { Key = "c", Operation = "Return", Ask = { "colour" }, OnRejected = "explode" } } };
        var errors = WorkflowCatalog.Validate(bad, ops);
        Assert.Contains(errors, e => e.Contains("Code")); Assert.Contains(errors, e => e.Contains("Name"));
        Assert.Contains(errors, e => e.Contains("unknown or disabled operation 'Teleport'")); Assert.Contains(errors, e => e.Contains("duplicate key")); Assert.Contains(errors, e => e.Contains("Commission"));
        Assert.Contains(errors, e => e.Contains("unknown input 'colour'")); Assert.Contains(errors, e => e.Contains("onRejected"));
        Assert.Contains(await h.Db.Workflows.Select(w => w.Code).ToListAsync(), c => c == "tool-return-check");
    }

    [Fact]
    public async Task Edge_config_lists_only_readers_assigned_to_the_gateway_with_a_stable_revision()
    {
        var h = new TestHost();
        var site = h.Loc(LocationKind.Site, "Hangar");
        var gw = new Device { TenantId = h.Ctx.TenantId, Name = "Hangar gateway", Kind = DeviceKind.Gateway, SiteLocationId = site.Id };
        var other = new Device { TenantId = h.Ctx.TenantId, Name = "Other gateway", Kind = DeviceKind.Gateway };
        var r1 = new Device { TenantId = h.Ctx.TenantId, Name = "Dock 1", Kind = DeviceKind.Portal, Config = new() { ["llrpHost"] = "10.0.0.11", ["llrpPower"] = 27, ["llrpAntennas"] = "1,2", ["edgeManaged"] = true, ["edgeGatewayId"] = gw.Id.ToString() } };
        var r2 = new Device { TenantId = h.Ctx.TenantId, Name = "Dock 2", Kind = DeviceKind.Portal, Config = new() { ["llrpHost"] = "10.0.0.12", ["edgeManaged"] = true, ["edgeGatewayId"] = other.Id.ToString() } };
        var r3 = new Device { TenantId = h.Ctx.TenantId, Name = "Server-driven", Kind = DeviceKind.Portal, Config = new() { ["llrpHost"] = "10.0.0.13" } };
        var r4 = new Device { TenantId = h.Ctx.TenantId, Name = "Paused", Kind = DeviceKind.Portal, Config = new() { ["llrpHost"] = "10.0.0.14", ["llrpEnabled"] = false, ["edgeManaged"] = true, ["edgeGatewayId"] = gw.Id.ToString() } };
        h.Db.Devices.AddRange(gw, other, r1, r2, r3, r4); await h.SaveAsync();

        var svc = new EdgeConfigService(h.Db);
        var cfg = await svc.ForGatewayAsync(gw.Id);
        Assert.Equal(2, cfg.Readers.Count);
        var dock1 = cfg.Readers.Single(r => r.DeviceId == r1.Id);
        Assert.Equal("10.0.0.11", dock1.Host); Assert.Equal(27, dock1.PowerDbm); Assert.Equal(new ushort[] { 1, 2 }, dock1.Antennas); Assert.True(dock1.Enabled);
        Assert.False(cfg.Readers.Single(r => r.DeviceId == r4.Id).Enabled);
        Assert.Equal(cfg.Revision, (await svc.ForGatewayAsync(gw.Id)).Revision);
        r1.Config["llrpPower"] = 30; await h.SaveAsync();
        Assert.NotEqual(cfg.Revision, (await svc.ForGatewayAsync(gw.Id)).Revision);
        // The server never drives an edge-managed reader itself.
        Assert.Null(Rfid.Api.Background.LlrpReaderService.Endpoint(r1)); Assert.NotNull(Rfid.Api.Background.LlrpReaderService.Endpoint(r3));
    }

    [Fact]
    public void Reader_catalog_applies_server_configuration_reports_changes_and_survives_restart()
    {
        var local = new[] { new EdgeOptions.ReaderOptions { DeviceId = Guid.NewGuid(), Host = "192.168.0.5" } };
        var cat = new ReaderCatalog(_dir, local);
        Assert.False(cat.FromServer); Assert.Single(cat.Current()); Assert.Equal("192.168.0.5", cat.Current()[0].Host);

        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var v1 = new EdgeConfig { Revision = "r1", Readers = { new EdgeReader { DeviceId = a, Host = "10.0.0.1", PowerDbm = 25 }, new EdgeReader { DeviceId = b, Host = "10.0.0.2" } } };
        var changed = cat.Apply(v1);
        Assert.Equal(3, changed.Count);                                  // local reader removed, a and b added
        Assert.True(cat.FromServer); Assert.Equal(2, cat.Current().Count);
        Assert.Empty(cat.Apply(v1));                                      // same revision → nothing to do

        var v2 = new EdgeConfig { Revision = "r2", Readers = { new EdgeReader { DeviceId = a, Host = "10.0.0.1", PowerDbm = 30 }, new EdgeReader { DeviceId = b, Host = "10.0.0.2" } } };
        Assert.Equal(new[] { a }, cat.Apply(v2));                         // only the changed reader reconnects

        var restarted = new ReaderCatalog(_dir, local);                   // offline restart keeps the last server configuration
        Assert.True(restarted.FromServer); Assert.Equal("r2", restarted.Revision); Assert.Equal(30, restarted.Current().Single(r => r.DeviceId == a).PowerDbm);
    }
}
