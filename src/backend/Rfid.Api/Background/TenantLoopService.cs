using Microsoft.EntityFrameworkCore;
using Rfid.Api.Auth;
using Rfid.Application.Cluster;
using Rfid.Application.Services;
using Rfid.Infrastructure.Persistence;

namespace Rfid.Api.Background;

/// <summary>
/// Base for periodic jobs that run once per tenant inside an ambient tenant scope. In a multi-node deployment only
/// the node holding the "job:{name}" lease runs the job; the others stand by and take over when the lease expires.
/// </summary>
public abstract class TenantLoopService : BackgroundService
{
    protected readonly IServiceScopeFactory Scopes;
    protected readonly ILogger Log;
    private readonly TimeSpan _interval;
    private bool? _leader;
    protected TenantLoopService(IServiceScopeFactory scopes, ILogger log, TimeSpan interval) { Scopes = scopes; Log = log; _interval = interval; }

    protected virtual string LeaseName => "job:" + GetType().Name.Replace("Service", "").ToLowerInvariant();
    public bool IsLeader => _leader == true;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(5), ct); // let migrations/seed finish
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!await AcquireAsync(ct)) { await Task.Delay(_interval, ct); continue; }
                List<Guid> tenants;
                using (var s = Scopes.CreateScope()) tenants = await s.ServiceProvider.GetRequiredService<AppDbContext>().Tenants.IgnoreQueryFilters().Select(t => t.Id).ToListAsync(ct);
                foreach (var t in tenants)
                {
                    using var _ = AmbientContext.Use(t);
                    using var scope = Scopes.CreateScope();
                    try { await RunForTenantAsync(scope.ServiceProvider, t, ct); }
                    catch (Exception ex) when (ex is not OperationCanceledException) { Log.LogWarning(ex, "{Job} failed for tenant {Tenant}", GetType().Name, t); }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { Log.LogWarning(ex, "{Job} loop error", GetType().Name); }
            await Task.Delay(_interval, ct);
        }
        await ReleaseAsync();
    }

    private async Task<bool> AcquireAsync(CancellationToken ct)
    {
        using var scope = Scopes.CreateScope();
        var ttl = TimeSpan.FromSeconds(Math.Max(15, _interval.TotalSeconds * 3));
        var ok = await scope.ServiceProvider.GetRequiredService<LeaseService>().TryAcquireAsync(LeaseName, ClusterNode.Id, ttl, ct: ct);
        if (_leader != ok) Log.LogInformation("{Job}: node {Node} is now {State}", GetType().Name, ClusterNode.Id, ok ? "leader" : "standby");
        _leader = ok;
        return ok;
    }

    private async Task ReleaseAsync()
    {
        if (_leader != true) return;
        try { using var scope = Scopes.CreateScope(); await scope.ServiceProvider.GetRequiredService<LeaseService>().ReleaseAsync(LeaseName, ClusterNode.Id); }
        catch (Exception ex) { Log.LogDebug(ex, "{Job}: lease release failed", GetType().Name); }
    }

    protected abstract Task RunForTenantAsync(IServiceProvider sp, Guid tenantId, CancellationToken ct);
}

public class PresenceSweeperService : TenantLoopService
{
    public PresenceSweeperService(IServiceScopeFactory s, ILogger<PresenceSweeperService> l, IConfiguration cfg) : base(s, l, TimeSpan.FromSeconds(cfg.GetValue("Presence:SweepSeconds", 30))) { }
    protected override async Task RunForTenantAsync(IServiceProvider sp, Guid tenantId, CancellationToken ct)
    {
        var closed = await sp.GetRequiredService<PresenceService>().SweepAsync(DateTime.UtcNow, ct);
        if (closed > 0) Log.LogInformation("Presence sweep closed {Count} sessions for tenant {Tenant}", closed, tenantId);
    }
}

public class StocktakeSchedulerService : TenantLoopService
{
    public StocktakeSchedulerService(IServiceScopeFactory s, ILogger<StocktakeSchedulerService> l) : base(s, l, TimeSpan.FromSeconds(60)) { }
    protected override async Task RunForTenantAsync(IServiceProvider sp, Guid tenantId, CancellationToken ct)
    {
        var n = await sp.GetRequiredService<StocktakeScheduleService>().RunDueAsync(DateTime.UtcNow, ct);
        if (n > 0) Log.LogInformation("Opened {Count} scheduled stocktake(s) for tenant {Tenant}", n, tenantId);
    }
}

public class IntegrationDispatcherService : TenantLoopService
{
    public IntegrationDispatcherService(IServiceScopeFactory s, ILogger<IntegrationDispatcherService> l, IConfiguration cfg) : base(s, l, TimeSpan.FromSeconds(cfg.GetValue("Integrations:DispatchSeconds", 15))) { }
    protected override async Task RunForTenantAsync(IServiceProvider sp, Guid tenantId, CancellationToken ct)
    {
        var n = await sp.GetRequiredService<IntegrationService>().DispatchAllAsync(DateTime.UtcNow, ct);
        if (n > 0) Log.LogInformation("Delivered {Count} event(s)/alert(s) to integrations for tenant {Tenant}", n, tenantId);
    }
}

public class PrintQueueWorkerService : TenantLoopService
{
    public PrintQueueWorkerService(IServiceScopeFactory s, ILogger<PrintQueueWorkerService> l) : base(s, l, TimeSpan.FromSeconds(5)) { }
    protected override async Task RunForTenantAsync(IServiceProvider sp, Guid tenantId, CancellationToken ct)
    {
        var n = await sp.GetRequiredService<PrintQueueService>().ProcessAsync(DateTime.UtcNow, ct);
        if (n > 0) Log.LogInformation("Printed {Count} label(s) for tenant {Tenant}", n, tenantId);
    }
}

/// <summary>Every minute: reader health SLAs, geofence dwell limits and alert escalation steps.</summary>
public class MonitoringService : TenantLoopService
{
    public MonitoringService(IServiceScopeFactory s, ILogger<MonitoringService> l, IConfiguration cfg) : base(s, l, TimeSpan.FromSeconds(cfg.GetValue("Monitoring:IntervalSeconds", 60))) { }
    protected override async Task RunForTenantAsync(IServiceProvider sp, Guid tenantId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var changed = await sp.GetRequiredService<DeviceHealthService>().EvaluateAsync(now, ct);
        if (changed > 0) Log.LogInformation("Device health: {Count} state change(s) for tenant {Tenant}", changed, tenantId);
        var dwell = await sp.GetRequiredService<GeoService>().SweepDwellAsync(now, ct);
        if (dwell > 0) Log.LogInformation("Geofence dwell: {Count} alert(s) for tenant {Tenant}", dwell, tenantId);
        var esc = await sp.GetRequiredService<NotificationService>().EscalateDueAsync(now, ct);
        if (esc > 0) Log.LogInformation("Escalation: {Count} notification(s) sent for tenant {Tenant}", esc, tenantId);
    }
}

/// <summary>Incremental data-warehouse export (Warehouse:Enabled, every Warehouse:IntervalMinutes).</summary>
public class WarehouseExportJobService : TenantLoopService
{
    private readonly bool _enabled;
    public WarehouseExportJobService(IServiceScopeFactory s, ILogger<WarehouseExportJobService> l, IConfiguration cfg) : base(s, l, TimeSpan.FromMinutes(Math.Max(1, cfg.GetValue("Warehouse:IntervalMinutes", 60)))) => _enabled = cfg.GetValue("Warehouse:Enabled", false);
    protected override async Task RunForTenantAsync(IServiceProvider sp, Guid tenantId, CancellationToken ct)
    {
        if (!_enabled) return;
        var runs = await sp.GetRequiredService<WarehouseExportService>().RunIncrementalAsync(DateTime.UtcNow, null, ct);
        foreach (var r in runs) if (r.Error != null) Log.LogWarning("Warehouse export {Dataset} failed: {Error}", r.Dataset, r.Error);
        if (runs.Count > 0) Log.LogInformation("Warehouse export: {Runs} dataset(s), {Rows} rows for tenant {Tenant}", runs.Count, runs.Sum(r => r.Rows), tenantId);
    }
}

/// <summary>Anomaly detection pass (default every 15 minutes, on the lease holder).</summary>
public class AnomalyDetectionService : TenantLoopService
{
    public AnomalyDetectionService(IServiceScopeFactory s, ILogger<AnomalyDetectionService> l, IConfiguration cfg) : base(s, l, TimeSpan.FromMinutes(Math.Max(1, cfg.GetValue("Anomaly:IntervalMinutes", 15)))) { }
    protected override async Task RunForTenantAsync(IServiceProvider sp, Guid tenantId, CancellationToken ct)
    {
        var (created, updated) = await sp.GetRequiredService<AnomalyService>().RunAsync(DateTime.UtcNow, ct);
        if (created > 0) Log.LogInformation("Anomaly detection: {Created} new, {Updated} refreshed for tenant {Tenant}", created, updated, tenantId);
    }
}

/// <summary>Retention policies (default every 6 hours, on the lease holder).</summary>
public class RetentionJobService : TenantLoopService
{
    public RetentionJobService(IServiceScopeFactory s, ILogger<RetentionJobService> l, IConfiguration cfg) : base(s, l, TimeSpan.FromMinutes(Math.Max(5, cfg.GetValue("Retention:IntervalMinutes", 360)))) { }
    protected override async Task RunForTenantAsync(IServiceProvider sp, Guid tenantId, CancellationToken ct)
    {
        var deleted = await sp.GetRequiredService<RetentionService>().ApplyAsync(DateTime.UtcNow, ct);
        var total = deleted.Values.Sum();
        if (total > 0) Log.LogInformation("Retention: deleted {Total} rows ({Detail}) for tenant {Tenant}", total, string.Join(", ", deleted.Where(kv => kv.Value > 0).Select(kv => $"{kv.Key}={kv.Value}")), tenantId);
    }
}

/// <summary>Billing accrual from custody events (hourly) and maintenance forecasts (daily by default), on the lease holder.</summary>
public class BillingAccrualService : TenantLoopService
{
    public BillingAccrualService(IServiceScopeFactory s, ILogger<BillingAccrualService> l, IConfiguration cfg) : base(s, l, TimeSpan.FromMinutes(Math.Max(1, cfg.GetValue("Billing:AccrueMinutes", 60)))) { }
    protected override async Task RunForTenantAsync(IServiceProvider sp, Guid tenantId, CancellationToken ct)
    {
        var n = await sp.GetRequiredService<BillingService>().AccrueAsync(DateTime.UtcNow, ct);
        if (n > 0) Log.LogInformation("Billing: {Count} ledger entries for tenant {Tenant}", n, tenantId);
    }
}

public class MaintenanceForecastService : TenantLoopService
{
    public MaintenanceForecastService(IServiceScopeFactory s, ILogger<MaintenanceForecastService> l, IConfiguration cfg) : base(s, l, TimeSpan.FromMinutes(Math.Max(5, cfg.GetValue("Maintenance:IntervalMinutes", 1440)))) { }
    protected override async Task RunForTenantAsync(IServiceProvider sp, Guid tenantId, CancellationToken ct)
    {
        var (forecasts, alerts) = await sp.GetRequiredService<MaintenanceService>().RunAsync(DateTime.UtcNow, ct);
        if (forecasts > 0) Log.LogInformation("Maintenance: {Forecasts} forecasts, {Alerts} alert(s) for tenant {Tenant}", forecasts, alerts, tenantId);
    }
}

public class HttpIntegrationTransport : IIntegrationTransport
{
    private readonly HttpClient _http;
    public HttpIntegrationTransport(HttpClient http) { _http = http; _http.Timeout = TimeSpan.FromSeconds(30); }
    public async Task<(bool ok, string? error)> PostAsync(string url, string body, IDictionary<string, string> headers, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
            foreach (var h in headers) req.Headers.TryAddWithoutValidation(h.Key, h.Value);
            using var res = await _http.SendAsync(req, ct);
            return res.IsSuccessStatusCode ? (true, null) : (false, $"HTTP {(int)res.StatusCode} {res.ReasonPhrase}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested) { return (false, ex.Message); }
    }
}
