using Microsoft.EntityFrameworkCore;
using Rfid.Application.Templates;
using Rfid.Domain;
using Rfid.Infrastructure.Persistence.Demo;
using Xunit;

namespace Rfid.Tests;

public class DemoSeedTests
{
    [Fact]
    public void Every_template_has_a_demo_scenario_and_vice_versa()
    {
        var templates = SolutionTemplateCatalog.All.Select(t => t.Code).ToHashSet();
        var scenarios = DemoScenarios.All.Select(s => s.Template).ToList();
        Assert.Equal(scenarios.Count, scenarios.Distinct().Count());
        Assert.Empty(templates.Except(scenarios));
        Assert.Empty(scenarios.Except(templates));
        Assert.Equal(DemoScenarios.All.Count, DemoScenarios.All.Select(s => s.CompanyPrefix).Distinct().Count());
        Assert.Equal(DemoScenarios.All.Count, DemoScenarios.All.Select(s => s.SiteCode).Distinct().Count());
        // Item identifiers are unique per tenant in PostgreSQL; the in-memory provider does not enforce that, so check here.
        var ids = DemoScenarios.All.SelectMany(s => s.Items.Select(i => i.Identifier)).ToList();
        var dup = ids.GroupBy(x => x).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(dup.Count == 0, "Duplicate identifiers across scenarios: " + string.Join(", ", dup));
    }

    [Fact]
    public async Task All_scenarios_seed_without_rejected_history_and_produce_alerts()
    {
        var h = new TestHost();
        foreach (var t in SolutionTemplateCatalog.All) h.Db.SolutionTemplates.Add(t);
        await h.SaveAsync();
        var seeder = new DemoSeeder(h.Db, h.Ctx, h.Templates, h.Ops, h.Ingest, h.Rules);

        var totalItems = 0; var totalAlerts = 0; var problems = new List<string>();
        foreach (var sc in DemoScenarios.All)
        {
            SeedResult r;
            try { r = await seeder.SeedAsync(sc); }
            catch (Exception ex) { problems.Add($"{sc.Template}: {ex.Message}"); continue; }
            if (r.Skipped) problems.Add($"{sc.Template}: skipped");
            if (r.Items < 8) problems.Add($"{sc.Template}: only {r.Items} items");
            if (r.Devices < 1) problems.Add($"{sc.Template}: no devices");
            if (r.Operations < 1) problems.Add($"{sc.Template}: no history");
            problems.AddRange(r.Warnings.Select(w => $"{sc.Template}: {w}"));
            totalItems += r.Items; totalAlerts += r.Alerts;
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.Equal(totalItems, await h.Db.Items.CountAsync());
        Assert.Equal(totalItems, await h.Db.Tags.CountAsync(t => t.ItemId != null));
        Assert.True(totalAlerts >= 10, $"only {totalAlerts} alerts raised by reader traffic");
        Assert.True(await h.Db.Alerts.AnyAsync(a => a.Severity == Severity.Critical));
        Assert.True(await h.Db.ItemEvents.CountAsync(e => e.Type == ItemEventType.Moved) > 50);

        // Idempotent: seeding again is a no-op.
        var again = await seeder.SeedAsync(DemoScenarios.All[0]);
        Assert.True(again.Skipped);
    }

    [Fact]
    public async Task Scenario_epcs_are_valid_sgtin96_and_unique()
    {
        var h = new TestHost();
        foreach (var t in SolutionTemplateCatalog.All) h.Db.SolutionTemplates.Add(t);
        await h.SaveAsync();
        var seeder = new DemoSeeder(h.Db, h.Ctx, h.Templates, h.Ops, h.Ingest, h.Rules);
        await seeder.SeedAsync(DemoScenarios.All.First(s => s.Template == "retail"));
        await seeder.SeedAsync(DemoScenarios.All.First(s => s.Template == "library"));
        var epcs = await h.Db.Tags.Select(t => t.Epc).ToListAsync();
        Assert.Equal(epcs.Count, epcs.Distinct().Count());
        Assert.All(epcs, e => Assert.NotNull(Rfid.Domain.Epc.Sgtin96.Decode(e)));
    }
}
