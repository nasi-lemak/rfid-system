using Microsoft.EntityFrameworkCore;
using Rfid.Api.Auth;
using Rfid.Application.Services;
using Rfid.Infrastructure.Persistence;

namespace Rfid.Api.Background;

/// <summary>Base for periodic jobs that run once per tenant inside an ambient tenant scope.</summary>
public abstract class TenantLoopService : BackgroundService
{
    protected readonly IServiceScopeFactory Scopes;
    protected readonly ILogger Log;
    private readonly TimeSpan _interval;
    protected TenantLoopService(IServiceScopeFactory scopes, ILogger log, TimeSpan interval) { Scopes = scopes; Log = log; _interval = interval; }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(5), ct); // let migrations/seed finish
        while (!ct.IsCancellationRequested)
        {
            try
            {
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
