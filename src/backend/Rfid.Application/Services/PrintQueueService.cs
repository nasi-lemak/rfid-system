using Microsoft.EntityFrameworkCore;
using Rfid.Application.Contracts;
using Rfid.Domain;
using Rfid.Domain.Entities;

namespace Rfid.Application.Services;

/// <summary>
/// Label print queue: jobs are rendered when enqueued (so the audit trail keeps the exact ZPL), sent by a
/// worker with retries, and recorded as LabelPrinted events on the item. Printer devices track label stock
/// (Config.labelStock / labelStockMin) and raise an alert when running low.
/// </summary>
public class PrintQueueService
{
    private readonly IAppDb _db;
    private readonly ICurrentContext _ctx;
    private readonly IPrinterClient _printer;
    public int MaxAttempts { get; set; } = 5;
    public PrintQueueService(IAppDb db, ICurrentContext ctx, IPrinterClient printer) { _db = db; _ctx = ctx; _printer = printer; }

    public async Task<List<PrintJob>> EnqueueAsync(IEnumerable<Guid> itemIds, Guid printerDeviceId, PrintReason reason = PrintReason.Initial, int copies = 1, string? template = null, string? note = null, string? epcOverride = null, CancellationToken ct = default)
    {
        var printer = await _db.Devices.FindAsync(new object[] { printerDeviceId }, ct);
        if (printer == null || printer.Kind != DeviceKind.Printer) throw new DomainException("Printer device not found");
        var jobs = new List<PrintJob>();
        foreach (var id in itemIds)
        {
            var item = await _db.Items.Include(i => i.ItemType).Include(i => i.CurrentLocation).Include(i => i.Tags).FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundException("Item");
            var epc = epcOverride ?? item.Tags.FirstOrDefault(t => t.Status == TagStatus.Active)?.Epc ?? item.Tags.FirstOrDefault()?.Epc ?? throw new DomainException($"{item.Name} has no tag to encode");
            var printedBefore = await _db.PrintJobs.AnyAsync(j => j.ItemId == id && j.Status == PrintJobStatus.Printed, ct);
            var job = new PrintJob { TenantId = _ctx.TenantId, ItemId = id, PrinterDeviceId = printerDeviceId, Epc = epc, Zpl = LabelService.Render(item, epc, template), Reason = reason == PrintReason.Initial && printedBefore ? PrintReason.Reprint : reason, Copies = Math.Clamp(copies, 1, 50), Note = note, RequestedBy = _ctx.UserId };
            _db.PrintJobs.Add(job); jobs.Add(job);
        }
        await _db.SaveChangesAsync(ct);
        return jobs;
    }

    /// <summary>Sends due jobs; returns the number printed. Called by the worker and by "process now".</summary>
    public async Task<int> ProcessAsync(DateTime now, CancellationToken ct = default)
    {
        var due = await _db.PrintJobs.Where(j => j.Status == PrintJobStatus.Queued && (j.NextAttemptAt == null || j.NextAttemptAt <= now)).OrderBy(j => j.RequestedAt).Take(50).ToListAsync(ct);
        if (due.Count == 0) return 0;
        var printerIds = due.Select(j => j.PrinterDeviceId).Distinct().ToList();
        var printers = await _db.Devices.Where(d => printerIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, ct);
        var printed = 0;
        foreach (var job in due)
        {
            var printer = printers.GetValueOrDefault(job.PrinterDeviceId);
            var host = printer?.Config.GetValueOrDefault("host")?.ToString(); var port = int.TryParse(printer?.Config.GetValueOrDefault("port")?.ToString(), out var p) ? p : 9100;
            job.Attempts++;
            if (printer == null || string.IsNullOrEmpty(host)) { Fail(job, "printer has no host configured", now); continue; }
            try
            {
                job.Status = PrintJobStatus.Printing;
                var zpl = job.Copies > 1 ? job.Zpl.Replace("^XZ", $"^PQ{job.Copies}\n^XZ") : job.Zpl;
                await _printer.SendAsync(host, port, zpl, ct);
                job.Status = PrintJobStatus.Printed; job.PrintedAt = now; job.Error = null; printer.LastSeenAt = now; printed++;
                _db.ItemEvents.Add(new ItemEvent { TenantId = _ctx.TenantId, ItemId = job.ItemId, Type = ItemEventType.LabelPrinted, OccurredAt = now, UserId = job.RequestedBy, DeviceId = printer.Id, Data = new() { ["jobId"] = job.Id, ["reason"] = job.Reason.ToString(), ["printer"] = printer.Name, ["epc"] = job.Epc, ["copies"] = job.Copies } });
                AdjustStock(printer, -job.Copies, now);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { Fail(job, ex.Message, now); }
        }
        await _db.SaveChangesAsync(ct);
        return printed;
    }

    private void Fail(PrintJob job, string error, DateTime now)
    {
        job.Error = error;
        if (job.Attempts >= MaxAttempts) { job.Status = PrintJobStatus.Failed; return; }
        job.Status = PrintJobStatus.Queued; job.NextAttemptAt = now.AddSeconds(15 * Math.Pow(2, job.Attempts - 1));
    }

    /// <summary>Decrements (or restocks) the printer's label stock and raises a low-stock alert when crossing the minimum.</summary>
    public void AdjustStock(Device printer, int delta, DateTime now)
    {
        if (!int.TryParse(printer.Config.GetValueOrDefault("labelStock")?.ToString(), out var stock)) return;
        var min = int.TryParse(printer.Config.GetValueOrDefault("labelStockMin")?.ToString(), out var m) ? m : 50;
        var before = stock; stock = Math.Max(0, stock + delta);
        printer.Config["labelStock"] = stock;
        if (delta < 0 && before > min && stock <= min)
            _db.Alerts.Add(new Alert { TenantId = _ctx.TenantId, Severity = Severity.Warning, Message = $"Label stock low on {printer.Name}: {stock} labels left (minimum {min})", RaisedAt = now });
        if (stock == 0 && before > 0)
            _db.Alerts.Add(new Alert { TenantId = _ctx.TenantId, Severity = Severity.Critical, Message = $"{printer.Name} is out of labels", RaisedAt = now });
    }
}
