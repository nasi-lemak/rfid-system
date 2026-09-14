using Microsoft.EntityFrameworkCore;
using Rfid.Domain.Entities;

namespace Rfid.Application.Contracts;

public interface IAppDb
{
    DbSet<Tenant> Tenants { get; }
    DbSet<User> Users { get; }
    DbSet<Party> Parties { get; }
    DbSet<Location> Locations { get; }
    DbSet<ItemType> ItemTypes { get; }
    DbSet<Item> Items { get; }
    DbSet<Tag> Tags { get; }
    DbSet<Device> Devices { get; }
    DbSet<Antenna> Antennas { get; }
    DbSet<TagRead> TagReads { get; }
    DbSet<Operation> Operations { get; }
    DbSet<OperationLine> OperationLines { get; }
    DbSet<ItemEvent> ItemEvents { get; }
    DbSet<Stocktake> Stocktakes { get; }
    DbSet<StocktakeLine> StocktakeLines { get; }
    DbSet<Rule> Rules { get; }
    DbSet<Alert> Alerts { get; }
    DbSet<SolutionTemplate> SolutionTemplates { get; }
    DbSet<PresenceSession> PresenceSessions { get; }
    DbSet<StocktakeSchedule> StocktakeSchedules { get; }
    DbSet<IntegrationEndpoint> IntegrationEndpoints { get; }
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>Ambient tenant/user for the current request (JWT claims in the API, fixed in tests).</summary>
public interface ICurrentContext
{
    Guid TenantId { get; }
    Guid? UserId { get; }
    Guid? DeviceId { get; }
}

/// <summary>Fan-out of live events (SignalR in the API; no-op in tests).</summary>
public interface ILivePublisher
{
    Task PublishReadsAsync(IEnumerable<LiveRead> reads, CancellationToken ct = default);
    Task PublishAlertAsync(Alert alert, CancellationToken ct = default);
    Task PublishEventAsync(ItemEvent ev, CancellationToken ct = default);
}

public record LiveRead(string Epc, Guid? ItemId, string? ItemName, Guid? DeviceId, Guid? LocationId, double? Rssi, DateTime ReadAt);

public interface IWebhookDispatcher
{
    Task SendAsync(string url, object payload, CancellationToken ct = default);
}

public class NullLivePublisher : ILivePublisher
{
    public Task PublishReadsAsync(IEnumerable<LiveRead> reads, CancellationToken ct = default) => Task.CompletedTask;
    public Task PublishAlertAsync(Alert alert, CancellationToken ct = default) => Task.CompletedTask;
    public Task PublishEventAsync(ItemEvent ev, CancellationToken ct = default) => Task.CompletedTask;
}

public class NullWebhookDispatcher : IWebhookDispatcher
{
    public Task SendAsync(string url, object payload, CancellationToken ct = default) => Task.CompletedTask;
}

public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
}

public class NotFoundException : DomainException
{
    public NotFoundException(string what) : base($"{what} not found") { }
}
