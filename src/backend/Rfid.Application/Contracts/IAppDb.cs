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
    DbSet<PrintJob> PrintJobs { get; }
    DbSet<WorkerLease> WorkerLeases { get; }
    DbSet<PositionFix> PositionFixes { get; }
    DbSet<UserSiteAccess> UserSiteAccess { get; }
    DbSet<DeviceHeartbeat> DeviceHeartbeats { get; }
    DbSet<FirmwareRelease> FirmwareReleases { get; }
    DbSet<FirmwareRollout> FirmwareRollouts { get; }
    DbSet<GeoFence> GeoFences { get; }
    DbSet<GpsFix> GpsFixes { get; }
    DbSet<GeoFenceState> GeoFenceStates { get; }
    DbSet<Dashboard> Dashboards { get; }
    DbSet<NotificationChannel> NotificationChannels { get; }
    DbSet<EscalationPolicy> EscalationPolicies { get; }
    DbSet<NotificationLog> NotificationLogs { get; }
    DbSet<WarehouseExportRun> WarehouseExportRuns { get; }
    DbSet<Anomaly> Anomalies { get; }
    DbSet<SerialPool> SerialPools { get; }
    DbSet<EncodingBatch> EncodingBatches { get; }
    DbSet<AuditEntry> AuditEntries { get; }
    DbSet<RetentionPolicy> RetentionPolicies { get; }
    DbSet<RateCard> RateCards { get; }
    DbSet<LedgerEntry> LedgerEntries { get; }
    DbSet<Invoice> Invoices { get; }
    DbSet<BillingCursor> BillingCursors { get; }
    DbSet<MaintenanceForecast> MaintenanceForecasts { get; }
    DbSet<EpcisCapture> EpcisCaptures { get; }
    DbSet<OperationDefinition> OperationDefinitions { get; }
    DbSet<WorkflowDefinition> Workflows { get; }
    DbSet<OutboxMessage> Outbox { get; }
    DbSet<IdempotencyKey> IdempotencyKeys { get; }
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

/// <summary>Sends ZPL to a label printer (raw TCP in the API, fake in tests).</summary>
public interface IPrinterClient { Task SendAsync(string host, int port, string zpl, CancellationToken ct); }

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
