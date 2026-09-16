using Microsoft.EntityFrameworkCore;
using Rfid.Application.Contracts;
using Rfid.Domain;
using Rfid.Domain.Entities;

namespace Rfid.Application.Services;

/// <summary>
/// Returnable-asset billing: custody events (Issue/Dispatch → deposit + cycle fee; Return → deposit refund, daily rental beyond
/// the free days, late fee; Dispose/Missing while in custody → loss fee) become ledger entries by rate card; unbilled entries roll
/// up into per-party invoices for a period.
/// </summary>
public class BillingService
{
    private readonly IAppDb _db; private readonly ICurrentContext _ctx;
    /// <summary>How far back the first accrual reaches when rate cards appear.</summary>
    public TimeSpan InitialWindow { get; set; } = TimeSpan.FromDays(90);
    public BillingService(IAppDb db, ICurrentContext ctx) { _db = db; _ctx = ctx; }

    /// <summary>Most specific enabled rate card: party > party kind > item type > catch-all (then by priority).</summary>
    public static RateCard? Pick(IEnumerable<RateCard> cards, Item item, Party party)
        => cards.Where(c => c.Enabled && (c.ItemTypeId == null || c.ItemTypeId == item.ItemTypeId) && (c.PartyId == null || c.PartyId == party.Id) && (c.PartyKind == null || c.PartyKind == party.Kind))
            .OrderByDescending(c => (c.PartyId != null ? 8 : 0) + (c.PartyKind != null ? 4 : 0) + (c.ItemTypeId != null ? 2 : 0)).ThenByDescending(c => c.Priority).FirstOrDefault();

    /// <summary>Charges custody events since the last accrual up to <paramref name="now"/>. Returns ledger entries created.</summary>
    public async Task<int> AccrueAsync(DateTime now, CancellationToken ct = default)
    {
        var cards = await _db.RateCards.Where(c => c.Enabled).ToListAsync(ct);
        if (cards.Count == 0) return 0; // nothing to charge yet – leave the watermark alone so history is billed once cards exist
        var cursor = await _db.BillingCursors.FirstOrDefaultAsync(ct);
        if (cursor == null) { cursor = new BillingCursor { TenantId = _ctx.TenantId, AccruedTo = now - InitialWindow }; _db.BillingCursors.Add(cursor); }
        var events = await _db.ItemEvents.Where(e => e.OccurredAt > cursor.AccruedTo && e.OccurredAt <= now && (e.Type == ItemEventType.CustodyChanged || e.Type == ItemEventType.Moved || e.Type == ItemEventType.Disposed)).OrderBy(e => e.OccurredAt).ToListAsync(ct);
        var itemIds = events.Select(e => e.ItemId).Distinct().ToList();
        var items = await _db.Items.Include(i => i.ItemType).Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        var parties = await _db.Parties.ToDictionaryAsync(p => p.Id, ct);
        var ops = await _db.Operations.Where(o => events.Select(e => e.OperationId).Contains(o.Id)).ToDictionaryAsync(o => o.Id, ct);
        var created = 0;
        foreach (var e in events)
        {
            if (!items.TryGetValue(e.ItemId, out var item)) continue;
            var op = e.OperationId.HasValue ? ops.GetValueOrDefault(e.OperationId.Value) : null;
            // Issue / dispatch-to-party: custody starts.
            if ((e.Type == ItemEventType.CustodyChanged || (e.Type == ItemEventType.Moved && e.Data.ContainsKey("dispatch"))) && e.ToPartyId is { } toParty && parties.TryGetValue(toParty, out var party) && e.FromPartyId == null)
            {
                var card = Pick(cards, item, party); if (card == null) continue;
                if (card.DepositAmount > 0) Add(new LedgerEntry { PartyId = party.Id, ItemId = item.Id, ItemEventId = e.Id, OperationId = e.OperationId, RateCardId = card.Id, Kind = LedgerKind.Deposit, Amount = card.DepositAmount, Currency = card.Currency, OccurredAt = e.OccurredAt, Description = $"Deposit · {item.Name} ({item.Identifier})" }); 
                if (card.CycleFee > 0) Add(new LedgerEntry { PartyId = party.Id, ItemId = item.Id, ItemEventId = e.Id, OperationId = e.OperationId, RateCardId = card.Id, Kind = LedgerKind.CycleFee, Amount = card.CycleFee, Currency = card.Currency, OccurredAt = e.OccurredAt, Description = $"Cycle fee · {item.Name} ({item.Identifier})" });
                continue;
            }
            // Return: custody ends.
            if (e.Type == ItemEventType.CustodyChanged && e.FromPartyId is { } fromParty && e.ToPartyId == null && parties.TryGetValue(fromParty, out var owner))
            {
                var card = Pick(cards, item, owner); if (card == null) continue;
                var issued = await _db.ItemEvents.Where(x => x.ItemId == item.Id && x.ToPartyId == fromParty && x.OccurredAt < e.OccurredAt).OrderByDescending(x => x.OccurredAt).FirstOrDefaultAsync(ct);
                var days = issued == null ? 0 : (int)Math.Ceiling((e.OccurredAt - issued.OccurredAt).TotalDays);
                if (card.DepositAmount > 0) Add(new LedgerEntry { PartyId = owner.Id, ItemId = item.Id, ItemEventId = e.Id, OperationId = e.OperationId, RateCardId = card.Id, Kind = LedgerKind.DepositRefund, Amount = -card.DepositAmount, Currency = card.Currency, OccurredAt = e.OccurredAt, Description = $"Deposit refund · {item.Name} ({item.Identifier})" });
                var billableDays = Math.Max(0, days - card.FreeDays);
                if (card.DailyFee > 0 && billableDays > 0) Add(new LedgerEntry { PartyId = owner.Id, ItemId = item.Id, ItemEventId = e.Id, OperationId = e.OperationId, RateCardId = card.Id, Kind = LedgerKind.DailyFee, Amount = card.DailyFee * billableDays, Quantity = billableDays, Currency = card.Currency, OccurredAt = e.OccurredAt, Description = $"Rental {billableDays} day(s) beyond {card.FreeDays} free · {item.Name}" });
                DateTime? dueBack = null;
                if (issued?.OperationId is { } issOpId) { if (!ops.TryGetValue(issOpId, out var issOp)) { issOp = await _db.Operations.FirstOrDefaultAsync(o => o.Id == issOpId, ct); if (issOp != null) ops[issOpId] = issOp; } dueBack = issOp?.DueBackAt; }
                if (dueBack == null && issued != null && issued.Data.TryGetValue("dueBackAt", out var d) && DateTime.TryParse(d?.ToString(), out var dd)) dueBack = dd;
                if (card.LateFeePerDay > 0 && dueBack.HasValue && e.OccurredAt > dueBack.Value) { var late = (int)Math.Ceiling((e.OccurredAt - dueBack.Value).TotalDays); Add(new LedgerEntry { PartyId = owner.Id, ItemId = item.Id, ItemEventId = e.Id, OperationId = e.OperationId, RateCardId = card.Id, Kind = LedgerKind.LateFee, Amount = card.LateFeePerDay * late, Quantity = late, Currency = card.Currency, OccurredAt = e.OccurredAt, Description = $"Late return {late} day(s) · {item.Name}" }); }
                continue;
            }
            // Lost / disposed while in custody: loss fee (deposit is kept – no refund entry is generated).
            if (e.Type == ItemEventType.Disposed)
            {
                // The disposal clears the custodian, so find who held the item from the last custody change before it.
                var lastCustody = await _db.ItemEvents.Where(x => x.ItemId == item.Id && x.Type == ItemEventType.CustodyChanged && x.OccurredAt <= e.OccurredAt).OrderByDescending(x => x.OccurredAt).FirstOrDefaultAsync(ct);
                var cust = lastCustody?.ToPartyId ?? item.CustodianPartyId;
                if (cust == null || !parties.TryGetValue(cust.Value, out var holder)) continue;
                var card = Pick(cards, item, holder); if (card?.LossFee > 0) Add(new LedgerEntry { PartyId = holder.Id, ItemId = item.Id, ItemEventId = e.Id, OperationId = e.OperationId, RateCardId = card.Id, Kind = LedgerKind.LossFee, Amount = card.LossFee, Currency = card.Currency, OccurredAt = e.OccurredAt, Description = $"Loss fee · {item.Name} ({item.Identifier})" });
            }
        }
        cursor.AccruedTo = now;
        await _db.SaveChangesAsync(ct);
        return created;
        void Add(LedgerEntry le) { le.TenantId = _ctx.TenantId; _db.LedgerEntries.Add(le); created++; }
    }

    public record PartyBalance(Guid PartyId, string Party, string Kind, decimal Unbilled, decimal Outstanding, decimal DepositsHeld, int OpenInvoices, DateTime? LastActivity);

    public async Task<List<PartyBalance>> BalancesAsync(CancellationToken ct = default)
    {
        var entries = await _db.LedgerEntries.Select(l => new { l.PartyId, l.Kind, l.Amount, l.InvoiceId, l.OccurredAt }).ToListAsync(ct);
        var invoices = await _db.Invoices.Where(i => i.Status == InvoiceStatus.Issued).Select(i => new { i.PartyId, i.Total }).ToListAsync(ct);
        var parties = await _db.Parties.ToDictionaryAsync(p => p.Id, ct);
        return entries.GroupBy(e => e.PartyId).Select(g =>
        {
            var p = parties.GetValueOrDefault(g.Key);
            return new PartyBalance(g.Key, p?.Name ?? "?", p?.Kind.ToString() ?? "", g.Where(e => e.InvoiceId == null).Sum(e => e.Amount), invoices.Where(i => i.PartyId == g.Key).Sum(i => i.Total),
                g.Where(e => e.Kind is LedgerKind.Deposit or LedgerKind.DepositRefund).Sum(e => e.Amount), invoices.Count(i => i.PartyId == g.Key), g.Max(e => (DateTime?)e.OccurredAt));
        }).OrderByDescending(b => b.Outstanding + b.Unbilled).ToList();
    }

    /// <summary>Creates one draft invoice per party for the unbilled entries dated within the period.</summary>
    public async Task<List<Invoice>> GenerateInvoicesAsync(DateTime from, DateTime to, Guid? partyId = null, int dueDays = 30, CancellationToken ct = default)
    {
        var q = _db.LedgerEntries.Where(l => l.InvoiceId == null && l.OccurredAt >= from && l.OccurredAt < to);
        if (partyId.HasValue) q = q.Where(l => l.PartyId == partyId);
        var entries = await q.ToListAsync(ct);
        var created = new List<Invoice>();
        var seq = await _db.Invoices.CountAsync(i => i.CreatedAt.Year == to.Year && i.CreatedAt.Month == to.Month, ct);
        foreach (var g in entries.GroupBy(e => new { e.PartyId, e.Currency }))
        {
            var charges = g.Where(e => e.Amount > 0).Sum(e => e.Amount); var credits = g.Where(e => e.Amount < 0).Sum(e => e.Amount);
            if (charges == 0 && credits == 0) continue;
            var inv = new Invoice
            {
                TenantId = _ctx.TenantId, Number = $"INV-{to:yyyyMM}-{++seq:0000}", PartyId = g.Key.PartyId, Currency = g.Key.Currency, PeriodFrom = from, PeriodTo = to,
                Charges = charges, Credits = credits, Total = charges + credits, DueAt = to.AddDays(dueDays),
                Lines = g.GroupBy(e => e.Kind).Select(k => new InvoiceLine { Kind = k.Key.ToString(), Description = Describe(k.Key), Quantity = k.Count(), Amount = k.Sum(e => e.Amount) }).OrderBy(l => l.Kind).ToList(),
            };
            _db.Invoices.Add(inv);
            foreach (var e in g) e.InvoiceId = inv.Id;
            created.Add(inv);
        }
        await _db.SaveChangesAsync(ct);
        return created;
    }

    private static string Describe(LedgerKind k) => k switch
    {
        LedgerKind.Deposit => "Deposits on items issued", LedgerKind.DepositRefund => "Deposits refunded on return", LedgerKind.CycleFee => "Per-cycle usage fees", LedgerKind.DailyFee => "Daily rental beyond free days",
        LedgerKind.LateFee => "Late-return fees", LedgerKind.LossFee => "Lost / disposed items", LedgerKind.Adjustment => "Adjustments", LedgerKind.Payment => "Payments received", _ => k.ToString(),
    };

    public async Task<Invoice> SetStatusAsync(Guid id, InvoiceStatus status, DateTime now, CancellationToken ct = default)
    {
        var inv = await _db.Invoices.FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundException("Invoice");
        if (inv.Status == InvoiceStatus.Paid && status != InvoiceStatus.Paid) throw new DomainException("Paid invoices cannot change status");
        inv.Status = status;
        if (status == InvoiceStatus.Issued) inv.IssuedAt ??= now;
        if (status == InvoiceStatus.Paid) { inv.PaidAt = now; _db.LedgerEntries.Add(new LedgerEntry { TenantId = inv.TenantId, PartyId = inv.PartyId, Kind = LedgerKind.Payment, Amount = -inv.Total, Currency = inv.Currency, OccurredAt = now, InvoiceId = inv.Id, Description = $"Payment · {inv.Number}" }); }
        if (status == InvoiceStatus.Void) foreach (var e in await _db.LedgerEntries.Where(l => l.InvoiceId == id && l.Kind != LedgerKind.Payment).ToListAsync(ct)) e.InvoiceId = null; // entries return to the unbilled pool
        await _db.SaveChangesAsync(ct);
        return inv;
    }
}
