using Microsoft.EntityFrameworkCore;
using Rfid.Application.Contracts;
using Rfid.Application.Services;
using Rfid.Application.Templates;
using Rfid.Domain;
using Rfid.Domain.Entities;
using Rfid.Infrastructure.Persistence.Demo;

namespace Rfid.Infrastructure.Persistence;

/// <summary>
/// First-run seeding: refreshes the global template catalog, creates the demo tenant and users, installs
/// every solution template and loads the demo scenarios selected by configuration (default: all).
/// </summary>
public static class SeedData
{
    public static readonly Guid DemoTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid DemoAdminId = Guid.Parse("11111111-1111-1111-1111-00000000adde");

    public static async Task<List<SeedResult>> EnsureSeededAsync(DbContextOptions<AppDbContext> options, string adminEmail, string adminPassword, string scenarios = "all", CancellationToken ct = default)
    {
        var results = new List<SeedResult>();
        await using (var db = new AppDbContext(options))
        {
            var existing = await db.SolutionTemplates.IgnoreQueryFilters().ToDictionaryAsync(t => t.Code, ct);
            foreach (var tpl in SolutionTemplateCatalog.All)
            {
                if (existing.TryGetValue(tpl.Code, out var e)) { e.Name = tpl.Name; e.Vertical = tpl.Vertical; e.Description = tpl.Description; e.Definition = tpl.Definition; }
                else db.SolutionTemplates.Add(new SolutionTemplate { Code = tpl.Code, Name = tpl.Name, Vertical = tpl.Vertical, Description = tpl.Description, Definition = tpl.Definition });
            }
            await db.SaveChangesAsync(ct);

            if (await db.Tenants.AnyAsync(t => t.Id == DemoTenantId, ct)) return results;
            db.Tenants.Add(new Tenant { Id = DemoTenantId, Name = "Demo Tenant", Code = "demo" });
            db.Users.Add(new User { Id = DemoAdminId, TenantId = DemoTenantId, Email = adminEmail.ToLowerInvariant(), PasswordHash = PasswordHasher.Hash(adminPassword), DisplayName = "Administrator", Role = UserRole.Admin });
            db.Users.Add(new User { TenantId = DemoTenantId, Email = "operator@demo.local", PasswordHash = PasswordHasher.Hash("operator123"), DisplayName = "Warehouse Operator", Role = UserRole.Operator });
            db.Users.Add(new User { TenantId = DemoTenantId, Email = "viewer@demo.local", PasswordHash = PasswordHasher.Hash("viewer123"), DisplayName = "Read-only Viewer", Role = UserRole.Viewer });
            await db.SaveChangesAsync(ct);
        }

        var selected = SelectScenarios(scenarios);
        foreach (var sc in selected)
        {
            // Fresh context + service graph per scenario so the rule cache picks up freshly provisioned rules.
            var ctx = new SeedContext(DemoTenantId, DemoAdminId);
            await using var db = new AppDbContext(options, ctx);
            var resolver = new TagResolver(db);
            var rules = new RuleEngine(db, ctx, new NullLivePublisher(), new NullWebhookDispatcher());
            var seeder = new DemoSeeder(db, ctx, new TemplateProvisioner(db, ctx), new OperationProcessor(db, ctx, resolver, rules, new NullLivePublisher()), new ReadIngestionService(db, ctx, resolver, rules, new NullLivePublisher(), new PresenceService(db, ctx, rules, new NullLivePublisher()), new PositionService(db)), rules);
            try { results.Add(await seeder.SeedAsync(sc, ct)); }
            catch (Exception ex) { results.Add(new SeedResult { Scenario = sc.Template, Warnings = { "FAILED: " + (ex.InnerException?.Message ?? ex.Message) } }); }
        }
        // Templates that have no scenario still get installed so their item types are available.
        if (selected.Count > 0)
        {
            var ctx = new SeedContext(DemoTenantId, DemoAdminId);
            await using var db = new AppDbContext(options, ctx);
            var prov = new TemplateProvisioner(db, ctx);
            foreach (var tpl in SolutionTemplateCatalog.All) await prov.ApplyAsync(tpl.Code, ct);
        }
        return results;
    }

    public static List<Scenario> SelectScenarios(string spec)
    {
        spec = (spec ?? "all").Trim();
        if (spec.Equals("none", StringComparison.OrdinalIgnoreCase)) return new();
        if (spec.Equals("all", StringComparison.OrdinalIgnoreCase)) return DemoScenarios.All.ToList();
        var codes = spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return DemoScenarios.All.Where(s => codes.Contains(s.Template)).ToList();
    }
}
