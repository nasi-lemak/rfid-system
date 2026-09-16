using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Rfid.Api.Background;
using Rfid.Domain.Entities;

namespace Rfid.Api.Auth;

/// <summary>After a successful SaveChanges that wrote outbox rows, wakes the in-process dispatcher (no effect on failed saves – nothing was committed).</summary>
public class OutboxNudgeInterceptor : SaveChangesInterceptor
{
    private readonly OutboxSignal _signal;
    public OutboxNudgeInterceptor(OutboxSignal signal) => _signal = signal;
    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result) { Nudge(eventData.Context); return base.SavedChanges(eventData, result); }
    public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default) { Nudge(eventData.Context); return base.SavedChangesAsync(eventData, result, cancellationToken); }
    private void Nudge(DbContext? ctx) { if (ctx != null && ctx.ChangeTracker.Entries<OutboxMessage>().Any()) _signal.Nudge(); }
}
