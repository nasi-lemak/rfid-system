using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Rfid.Application.Contracts;
using Rfid.Domain;
using Rfid.Domain.Entities;
using Rfid.Domain.Lifecycle;

namespace Rfid.Infrastructure.Persistence;

public class AppDbContext : DbContext, IAppDb
{
    private readonly ICurrentContext? _ctx;

    public AppDbContext(DbContextOptions<AppDbContext> options, ICurrentContext? ctx = null) : base(options) => _ctx = ctx;

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Party> Parties => Set<Party>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<ItemType> ItemTypes => Set<ItemType>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<Antenna> Antennas => Set<Antenna>();
    public DbSet<TagRead> TagReads => Set<TagRead>();
    public DbSet<Operation> Operations => Set<Operation>();
    public DbSet<OperationLine> OperationLines => Set<OperationLine>();
    public DbSet<ItemEvent> ItemEvents => Set<ItemEvent>();
    public DbSet<Stocktake> Stocktakes => Set<Stocktake>();
    public DbSet<StocktakeLine> StocktakeLines => Set<StocktakeLine>();
    public DbSet<Rule> Rules => Set<Rule>();
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<SolutionTemplate> SolutionTemplates => Set<SolutionTemplate>();
    public DbSet<PresenceSession> PresenceSessions => Set<PresenceSession>();
    public DbSet<StocktakeSchedule> StocktakeSchedules => Set<StocktakeSchedule>();
    public DbSet<IntegrationEndpoint> IntegrationEndpoints => Set<IntegrationEndpoint>();
    public DbSet<PrintJob> PrintJobs => Set<PrintJob>();
    public DbSet<WorkerLease> WorkerLeases => Set<WorkerLease>();
    public DbSet<PositionFix> PositionFixes => Set<PositionFix>();
    public DbSet<UserSiteAccess> UserSiteAccess => Set<UserSiteAccess>();
    public DbSet<DeviceHeartbeat> DeviceHeartbeats => Set<DeviceHeartbeat>();
    public DbSet<FirmwareRelease> FirmwareReleases => Set<FirmwareRelease>();
    public DbSet<FirmwareRollout> FirmwareRollouts => Set<FirmwareRollout>();
    public DbSet<GeoFence> GeoFences => Set<GeoFence>();
    public DbSet<GpsFix> GpsFixes => Set<GpsFix>();
    public DbSet<GeoFenceState> GeoFenceStates => Set<GeoFenceState>();
    public DbSet<Dashboard> Dashboards => Set<Dashboard>();
    public DbSet<NotificationChannel> NotificationChannels => Set<NotificationChannel>();
    public DbSet<EscalationPolicy> EscalationPolicies => Set<EscalationPolicy>();
    public DbSet<NotificationLog> NotificationLogs => Set<NotificationLog>();
    public DbSet<WarehouseExportRun> WarehouseExportRuns => Set<WarehouseExportRun>();

    private Guid CurrentTenant => _ctx?.TenantId ?? Guid.Empty;

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("rfid");
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var isNpgsql = Database.ProviderName?.Contains("Npgsql") == true;

        // Tenant scoping: every TenantEntity gets a global query filter.
        foreach (var et in b.Model.GetEntityTypes().Where(e => typeof(TenantEntity).IsAssignableFrom(e.ClrType)))
        {
            var method = typeof(AppDbContext).GetMethod(nameof(ApplyTenantFilter), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .MakeGenericMethod(et.ClrType);
            method.Invoke(this, new object[] { b });
        }

        b.Entity<Tenant>().ToTable("tenants").HasIndex(t => t.Code).IsUnique();

        b.Entity<User>(e =>
        {
            e.ToTable("users");
            e.HasIndex(u => new { u.TenantId, u.Email }).IsUnique();
            e.Property(u => u.Role).HasConversion<string>();
        });

        b.Entity<Party>(e =>
        {
            e.ToTable("parties");
            e.Property(p => p.Kind).HasConversion<string>();
            e.Property(p => p.Attributes).HasColumnType(isNpgsql ? "jsonb" : "text").HasConversion(JsonConv<Dictionary<string, object?>>(json), JsonComparer<Dictionary<string, object?>>());
            e.HasIndex(p => new { p.TenantId, p.Code });
        });

        b.Entity<Location>(e =>
        {
            e.ToTable("locations");
            e.Property(l => l.Kind).HasConversion<string>();
            e.HasOne(l => l.Parent).WithMany().HasForeignKey(l => l.ParentId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(l => new { l.TenantId, l.Path });
            e.HasIndex(l => new { l.TenantId, l.Code });
            e.Property(l => l.Attributes).HasColumnType(isNpgsql ? "jsonb" : "text").HasConversion(JsonConv<Dictionary<string, object?>>(json), JsonComparer<Dictionary<string, object?>>());
        });

        b.Entity<ItemType>(e =>
        {
            e.ToTable("item_types");
            e.Property(t => t.Category).HasConversion<string>();
            e.HasIndex(t => new { t.TenantId, t.Code }).IsUnique();
            e.Property(t => t.AttributeSchema).HasColumnType(isNpgsql ? "jsonb" : "text").HasConversion(JsonConv<List<AttributeDefinition>>(json), JsonComparer<List<AttributeDefinition>>());
            e.Property(t => t.Lifecycle).HasColumnType(isNpgsql ? "jsonb" : "text").HasConversion(JsonConvNullable<LifecycleDefinition>(json), JsonComparerNullable<LifecycleDefinition>());
        });

        b.Entity<Item>(e =>
        {
            e.ToTable("items");
            e.Property(i => i.Status).HasConversion<string>();
            e.HasIndex(i => new { i.TenantId, i.Identifier }).IsUnique();
            e.HasIndex(i => new { i.TenantId, i.CurrentLocationId });
            e.HasIndex(i => new { i.TenantId, i.ItemTypeId, i.State });
            e.HasIndex(i => i.CustodianPartyId);
            e.HasIndex(i => i.ParentItemId);
            e.HasIndex(i => i.ExpiryDate);
            e.HasIndex(i => i.NextInspectionDue);
            e.HasOne(i => i.ItemType).WithMany().HasForeignKey(i => i.ItemTypeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(i => i.CurrentLocation).WithMany().HasForeignKey(i => i.CurrentLocationId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(i => i.CustodianParty).WithMany().HasForeignKey(i => i.CustodianPartyId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(i => i.ParentItem).WithMany().HasForeignKey(i => i.ParentItemId).OnDelete(DeleteBehavior.SetNull);
            e.Property(i => i.Quantity).HasPrecision(18, 4);
            e.Property(i => i.Cost).HasPrecision(18, 2);
            e.Property(i => i.Attributes).HasColumnType(isNpgsql ? "jsonb" : "text").HasConversion(JsonConv<Dictionary<string, object?>>(json), JsonComparer<Dictionary<string, object?>>());
        });

        b.Entity<Tag>(e =>
        {
            e.ToTable("tags");
            e.Property(t => t.Technology).HasConversion<string>();
            e.Property(t => t.Status).HasConversion<string>();
            e.HasIndex(t => new { t.TenantId, t.Epc }).IsUnique();
            e.HasIndex(t => t.Tid);
            e.HasOne(t => t.Item).WithMany(i => i.Tags).HasForeignKey(t => t.ItemId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Device>(e =>
        {
            e.ToTable("devices");
            e.Property(d => d.Kind).HasConversion<string>();
            e.Property(d => d.Health).HasConversion<string>();
            e.Property(d => d.Config).HasColumnType(isNpgsql ? "jsonb" : "text").HasConversion(JsonConv<Dictionary<string, object?>>(json), JsonComparer<Dictionary<string, object?>>());
            e.HasMany(d => d.Antennas).WithOne(a => a.Device).HasForeignKey(a => a.DeviceId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Antenna>(e =>
        {
            e.ToTable("antennas");
            e.Property(a => a.Direction).HasConversion<string>();
            e.HasOne(a => a.Location).WithMany().HasForeignKey(a => a.LocationId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(a => new { a.DeviceId, a.Port }).IsUnique();
        });

        b.Entity<TagRead>(e =>
        {
            e.ToTable("tag_reads");
            e.Property(r => r.Source).HasConversion<string>();
            e.HasIndex(r => r.ReadAt);
            e.HasIndex(r => new { r.TenantId, r.Epc, r.ReadAt });
            e.HasIndex(r => r.ItemId);
        });

        b.Entity<Operation>(e =>
        {
            e.ToTable("operations");
            e.Property(o => o.Type).HasConversion<string>();
            e.Property(o => o.Status).HasConversion<string>();
            e.HasIndex(o => new { o.TenantId, o.StartedAt });
            e.HasIndex(o => o.Reference);
            e.HasMany(o => o.Lines).WithOne().HasForeignKey(l => l.OperationId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<OperationLine>(e =>
        {
            e.ToTable("operation_lines");
            e.Property(l => l.Result).HasConversion<string>();
            e.Property(l => l.Quantity).HasPrecision(18, 4);
            e.HasIndex(l => l.ItemId);
        });

        b.Entity<ItemEvent>(e =>
        {
            e.ToTable("item_events");
            e.Property(v => v.Type).HasConversion<string>();
            e.HasIndex(v => new { v.ItemId, v.OccurredAt }).IsDescending(false, true);
            e.HasIndex(v => new { v.TenantId, v.OccurredAt });
            e.Property(v => v.Data).HasColumnType(isNpgsql ? "jsonb" : "text").HasConversion(JsonConv<Dictionary<string, object?>>(json), JsonComparer<Dictionary<string, object?>>());
        });

        b.Entity<Stocktake>(e =>
        {
            e.ToTable("stocktakes");
            e.Property(s => s.Status).HasConversion<string>();
            e.HasMany(s => s.Lines).WithOne().HasForeignKey(l => l.StocktakeId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<StocktakeLine>(e =>
        {
            e.ToTable("stocktake_lines");
            e.Property(l => l.Result).HasConversion<string>();
            e.HasIndex(l => new { l.StocktakeId, l.ItemId });
        });

        b.Entity<Rule>(e =>
        {
            e.ToTable("rules");
            e.Property(r => r.Trigger).HasConversion<string>();
            e.Property(r => r.Action).HasConversion<string>();
            e.Property(r => r.Severity).HasConversion<string>();
            e.Property(r => r.Conditions).HasColumnType(isNpgsql ? "jsonb" : "text").HasConversion(JsonConv<List<RuleCondition>>(json), JsonComparer<List<RuleCondition>>());
            e.Property(r => r.NotifyChannelIds).HasColumnType(isNpgsql ? "jsonb" : "text").HasConversion(JsonConv<List<Guid>>(json), JsonComparer<List<Guid>>());
            e.Property(r => r.Params).HasColumnType(isNpgsql ? "jsonb" : "text").HasConversion(JsonConv<Dictionary<string, object?>>(json), JsonComparer<Dictionary<string, object?>>());
        });

        b.Entity<Alert>(e =>
        {
            e.ToTable("alerts");
            e.Property(a => a.Severity).HasConversion<string>();
            e.Property(a => a.Status).HasConversion<string>();
            e.HasIndex(a => new { a.TenantId, a.Status, a.RaisedAt });
        });

        b.Entity<PresenceSession>(e =>
        {
            e.ToTable("presence_sessions");
            e.HasIndex(p => new { p.TenantId, p.ItemId, p.ExitedAt });
            e.HasIndex(p => new { p.TenantId, p.LocationId, p.ExitedAt });
            e.HasIndex(p => p.LastSeenAt);
        });

        b.Entity<StocktakeSchedule>(e =>
        {
            e.ToTable("stocktake_schedules");
            e.HasIndex(s => new { s.TenantId, s.Enabled, s.NextRunAt });
        });

        b.Entity<IntegrationEndpoint>(e =>
        {
            e.ToTable("integration_endpoints");
            e.Property(i => i.EventTypes).HasColumnType(isNpgsql ? "jsonb" : "text").HasConversion(JsonConv<List<ItemEventType>>(json), JsonComparer<List<ItemEventType>>());
            e.Property(i => i.Headers).HasColumnType(isNpgsql ? "jsonb" : "text").HasConversion(JsonConv<Dictionary<string, object?>>(json), JsonComparer<Dictionary<string, object?>>());
            e.Property(i => i.Mapping).HasColumnType(isNpgsql ? "jsonb" : "text").HasConversion(JsonConv<Dictionary<string, object?>>(json), JsonComparer<Dictionary<string, object?>>());
            e.Property(i => i.Format).HasConversion<string>();
            e.Property(i => i.AuthType).HasConversion<string>();
        });

        b.Entity<PrintJob>(e =>
        {
            e.ToTable("print_jobs");
            e.Property(p => p.Status).HasConversion<string>();
            e.Property(p => p.Reason).HasConversion<string>();
            e.HasIndex(p => new { p.TenantId, p.Status, p.NextAttemptAt });
            e.HasIndex(p => new { p.ItemId, p.RequestedAt });
        });

        b.Entity<WorkerLease>(e =>
        {
            e.ToTable("worker_leases");
            e.HasIndex(l => l.Name).IsUnique();
            e.Property(l => l.Version).IsConcurrencyToken();
        });

        b.Entity<PositionFix>(e =>
        {
            e.ToTable("position_fixes");
            e.HasIndex(f => new { f.TenantId, f.LocationId, f.At });
            e.HasIndex(f => new { f.ItemId, f.At });
        });

        b.Entity<UserSiteAccess>(e =>
        {
            e.ToTable("user_site_access");
            e.Property(u => u.Role).HasConversion<string>();
            e.HasIndex(u => new { u.UserId, u.SiteLocationId }).IsUnique();
        });

        b.Entity<DeviceHeartbeat>(e => { e.ToTable("device_heartbeats"); e.HasIndex(h => new { h.DeviceId, h.At }); e.Property(h => h.Metrics).HasColumnType(isNpgsql ? "jsonb" : "text").HasConversion(JsonConv<Dictionary<string, object?>>(json), JsonComparer<Dictionary<string, object?>>()); });
        b.Entity<FirmwareRelease>(e => { e.ToTable("firmware_releases"); e.HasIndex(f => new { f.TenantId, f.Vendor, f.Model, f.Version }).IsUnique(); });
        b.Entity<FirmwareRollout>(e => { e.ToTable("firmware_rollouts"); e.Property(r => r.Status).HasConversion<string>(); e.HasIndex(r => new { r.DeviceId, r.Status }); });
        b.Entity<GeoFence>(e =>
        {
            e.ToTable("geo_fences");
            e.Property(g => g.Kind).HasConversion<string>(); e.Property(g => g.Trigger).HasConversion<string>(); e.Property(g => g.Severity).HasConversion<string>();
            e.Property(g => g.Points).HasColumnType(isNpgsql ? "jsonb" : "text").HasConversion(JsonConv<List<GeoPoint>>(json), JsonComparer<List<GeoPoint>>());
        });
        b.Entity<GpsFix>(e => { e.ToTable("gps_fixes"); e.HasIndex(f => new { f.ItemId, f.At }); e.HasIndex(f => new { f.TenantId, f.At }); });
        b.Entity<GeoFenceState>(e => { e.ToTable("geo_fence_states"); e.HasIndex(s => new { s.FenceId, s.ItemId }).IsUnique(); });
        b.Entity<Dashboard>(e => { e.ToTable("dashboards"); e.Property(d => d.Widgets).HasColumnType(isNpgsql ? "jsonb" : "text").HasConversion(JsonConv<List<DashboardWidget>>(json), JsonComparer<List<DashboardWidget>>()); });
        b.Entity<NotificationChannel>(e =>
        {
            e.ToTable("notification_channels");
            e.Property(c => c.Kind).HasConversion<string>(); e.Property(c => c.MinSeverity).HasConversion<string>();
            e.Property(c => c.Config).HasColumnType(isNpgsql ? "jsonb" : "text").HasConversion(JsonConv<Dictionary<string, object?>>(json), JsonComparer<Dictionary<string, object?>>());
        });
        b.Entity<EscalationPolicy>(e =>
        {
            e.ToTable("escalation_policies");
            e.Property(p => p.MinSeverity).HasConversion<string>();
            e.Property(p => p.Steps).HasColumnType(isNpgsql ? "jsonb" : "text").HasConversion(JsonConv<List<EscalationStep>>(json), JsonComparer<List<EscalationStep>>());
        });
        b.Entity<NotificationLog>(e => { e.ToTable("notification_logs"); e.Property(l => l.Status).HasConversion<string>(); e.HasIndex(l => new { l.TenantId, l.SentAt }); e.HasIndex(l => l.AlertId); });
        b.Entity<WarehouseExportRun>(e => { e.ToTable("warehouse_export_runs"); e.HasIndex(r => new { r.TenantId, r.Dataset, r.To }); });

        b.Entity<SolutionTemplate>(e =>
        {
            e.ToTable("solution_templates");
            e.HasIndex(t => t.Code).IsUnique();
            e.Property(t => t.Definition).HasColumnType(isNpgsql ? "jsonb" : "text").HasConversion(JsonConv<TemplateDefinition>(json), JsonComparer<TemplateDefinition>());
        });
    }

    private void ApplyTenantFilter<T>(ModelBuilder b) where T : TenantEntity
        => b.Entity<T>().HasQueryFilter(e => e.TenantId == CurrentTenant);

    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        if (_ctx != null)
            foreach (var entry in ChangeTracker.Entries<TenantEntity>().Where(e => e.State == EntityState.Added && e.Entity.TenantId == Guid.Empty))
                entry.Entity.TenantId = _ctx.TenantId;
        return base.SaveChangesAsync(ct);
    }

    private static Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<T, string> JsonConv<T>(JsonSerializerOptions o) where T : class, new()
        => new(v => JsonSerializer.Serialize(v, o), v => string.IsNullOrEmpty(v) ? new T() : JsonSerializer.Deserialize<T>(v, o) ?? new T());

    private static Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<T?, string?> JsonConvNullable<T>(JsonSerializerOptions o) where T : class
        => new(v => v == null ? null : JsonSerializer.Serialize(v, o), v => string.IsNullOrEmpty(v) ? null : JsonSerializer.Deserialize<T>(v, o));

    private static Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<T> JsonComparer<T>() where T : class
        => new((a, b) => JsonSerializer.Serialize(a, (JsonSerializerOptions?)null) == JsonSerializer.Serialize(b, (JsonSerializerOptions?)null),
               v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null).GetHashCode(),
               v => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(v, (JsonSerializerOptions?)null), (JsonSerializerOptions?)null)!);

    private static Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<T?> JsonComparerNullable<T>() where T : class
        => new((a, b) => JsonSerializer.Serialize(a, (JsonSerializerOptions?)null) == JsonSerializer.Serialize(b, (JsonSerializerOptions?)null),
               v => v == null ? 0 : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null).GetHashCode(),
               v => v == null ? null : JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(v, (JsonSerializerOptions?)null), (JsonSerializerOptions?)null));
}

/// <summary>Used by `dotnet ef` at design time.</summary>
public class DesignTimeFactory : Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
                 ?? "Host=localhost;Database=rfid;Username=postgres;Password=postgres";
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(cs).Options;
        return new AppDbContext(opts);
    }
}
