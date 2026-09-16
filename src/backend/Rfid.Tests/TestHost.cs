using Microsoft.EntityFrameworkCore;
using Rfid.Application.Contracts;
using Rfid.Application.Services;
using Rfid.Application.Templates;
using Rfid.Domain;
using Rfid.Domain.Entities;
using Rfid.Infrastructure.Persistence;

namespace Rfid.Tests;

public class FixedContext : ICurrentContext
{
    public Guid TenantId { get; init; } = Guid.NewGuid();
    public Guid? UserId { get; init; } = Guid.NewGuid();
    public Guid? DeviceId { get; init; }
}

/// <summary>In-memory EF host wiring the real application services against the real DbContext model.</summary>
public class TestHost
{
    public FixedContext Ctx { get; } = new();
    public AppDbContext Db { get; }
    public Rfid.Application.Platform.Outbox Outbox { get; }
    public RuleEngine Rules { get; }
    public OperationProcessor Ops { get; }
    public StocktakeService Stocktakes { get; }
    public ReadIngestionService Ingest { get; }
    public TemplateProvisioner Templates { get; }

    public TestHost()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase("t-" + Guid.NewGuid()).Options;
        Db = new AppDbContext(opts, Ctx);
        var resolver = new TagResolver(Db);
        Outbox = new Rfid.Application.Platform.Outbox(Db, Ctx);
        Rules = new RuleEngine(Db, Ctx, new NullLivePublisher(), new NullWebhookDispatcher(), null, Outbox);
        Ops = new OperationProcessor(Db, Ctx, resolver, Rules, new NullLivePublisher(), new Rfid.Application.Operations.OperationDefinitions(Db));
        Stocktakes = new StocktakeService(Db, Ctx, resolver, Rules);
        Ingest = new ReadIngestionService(Db, Ctx, resolver, Rules, new NullLivePublisher(), new PresenceService(Db, Ctx, Rules, new NullLivePublisher()));
        Templates = new TemplateProvisioner(Db, Ctx);
    }

    public async Task<ItemType> InstallTypeAsync(string template, string code)
    {
        if (!await Db.ItemTypes.AnyAsync(t => t.Code == code))
        {
            foreach (var t in SolutionTemplateCatalog.All) if (!Db.SolutionTemplates.Local.Any(x => x.Code == t.Code) && !await Db.SolutionTemplates.AnyAsync(x => x.Code == t.Code)) Db.SolutionTemplates.Add(t);
            await Db.SaveChangesAsync();
            await Templates.ApplyAsync(template);
        }
        return await Db.ItemTypes.FirstAsync(t => t.Code == code);
    }

    public Location Loc(LocationKind kind, string name, Location? parent = null, Dictionary<string, object?>? attrs = null)
    {
        var l = new Location { TenantId = Ctx.TenantId, Kind = kind, Name = name, Code = name.ToUpperInvariant().Replace(' ', '-'), ParentId = parent?.Id, Attributes = attrs ?? new() };
        l.Path = (parent?.Path ?? "/") + l.Id.ToString("N") + "/";
        Db.Locations.Add(l);
        return l;
    }

    public Party Party(string name, PartyKind kind = PartyKind.Employee)
    {
        var p = new Party { TenantId = Ctx.TenantId, Name = name, Kind = kind };
        Db.Parties.Add(p);
        return p;
    }

    public (Item item, Tag tag) Item(ItemType type, string ident, Location? loc, string? state = null, string? epc = null)
    {
        var i = new Item { TenantId = Ctx.TenantId, ItemTypeId = type.Id, ItemType = type, Identifier = ident, Name = ident, CurrentLocationId = loc?.Id, State = state ?? type.Lifecycle?.Initial, Unit = type.Unit };
        var t = new Tag { TenantId = Ctx.TenantId, Epc = epc ?? ("E280" + ident.GetHashCode().ToString("X8") + "0000"), ItemId = i.Id, Status = TagStatus.Active };
        Db.Items.Add(i); Db.Tags.Add(t);
        return (i, t);
    }

    public Task<int> SaveAsync() => Db.SaveChangesAsync();
}
