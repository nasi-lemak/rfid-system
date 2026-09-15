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
    private readonly TimeSpan _retention; private DateTime _lastPrune;
    public PresenceSweeperService(IServiceScopeFactory s, ILogger<PresenceSweeperService> l, IConfiguration cfg) : base(s, l, TimeSpan.FromSeconds(cfg.GetValue("Presence:SweepSeconds", 30)))
        => _retention = TimeSpan.FromDays(cfg.GetValue("Positions:RetentionDays", 30));
    protected override async Task RunForTenantAsync(IServiceProvider sp, Guid tenantId, CancellationToken ct)
    {
        var closed = await sp.GetRequiredService<PresenceService>().SweepAsync(DateTime.UtcNow, ct);
        if (closed > 0) Log.LogInformation("Presence sweep closed {Count} sessions for tenant {Tenant}", closed, tenantId);
        if (DateTime.UtcNow - _lastPrune > TimeSpan.FromHours(1))
        {
            _lastPrune = DateTime.UtcNow;
            var pruned = await sp.GetRequiredService<PositionService>().PruneAsync(_retention, ct);
            if (pruned > 0) Log.LogInformation("Pruned {Count} position fixes older than {Days}d for tenant {Tenant}", pruned, _retention.TotalDays, tenantId);
        }
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
