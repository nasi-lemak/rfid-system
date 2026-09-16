using Microsoft.EntityFrameworkCore;
using Rfid.Application.Contracts;
using Rfid.Domain;
using Rfid.Domain.Entities;

namespace Rfid.Application.Services;

/// <summary>Opens scheduled stocktakes when due (and auto-reconciles stale ones); executed by a background service per tenant.</summary>
public class StocktakeScheduleService
{
    private readonly IAppDb _db;
    private readonly ICurrentContext _ctx;
    private readonly StocktakeService _stocktakes;
    public StocktakeScheduleService(IAppDb db, ICurrentContext ctx, StocktakeService stocktakes) { _db = db; _ctx = ctx; _stocktakes = stocktakes; }

    public static DateTime ComputeNextRun(DateTime from, int intervalDays, TimeSpan timeOfDay)
    {
        var candidate = from.Date + timeOfDay;
        while (candidate <= from) candidate = candidate.AddDays(Math.Max(1, intervalDays));
        return candidate;
    }

    public async Task<int> RunDueAsync(DateTime now, CancellationToken ct = default)
    {
        var due = await _db.StocktakeSchedules.Where(s => s.Enabled && s.NextRunAt <= now).ToListAsync(ct);
        var created = 0;
        foreach (var s in due)
        {
            var st = await _stocktakes.CreateAsync($"{s.Name} – {now:yyyy-MM-dd}", s.LocationId, s.ItemTypeId, ct);
            s.LastRunAt = now; s.LastStocktakeId = st.Id;
            s.NextRunAt = ComputeNextRun(now, s.IntervalDays, s.TimeOfDay);
            _db.Alerts.Add(new Alert { TenantId = _ctx.TenantId, LocationId = s.LocationId, Severity = Severity.Info, Message = $"Scheduled stocktake '{st.Name}' is open ({st.ExpectedCount} items expected)" });
            created++;
        }
        // Auto-reconcile scheduled stocktakes left open past their window.
        var auto = await _db.StocktakeSchedules.Where(s => s.AutoReconcileHours != null && s.LastStocktakeId != null).ToListAsync(ct);
        foreach (var s in auto)
        {
            var st = await _db.Stocktakes.FirstOrDefaultAsync(x => x.Id == s.LastStocktakeId && x.Status == StocktakeStatus.Open, ct);
            if (st != null && st.StartedAt.AddHours(s.AutoReconcileHours!.Value) <= now) await _stocktakes.ReconcileAsync(st.Id, ct);
        }
        await _db.SaveChangesAsync(ct);
        return created;
    }
}
