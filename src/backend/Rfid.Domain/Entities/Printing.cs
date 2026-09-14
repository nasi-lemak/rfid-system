namespace Rfid.Domain.Entities;

/// <summary>A label print request. Jobs are processed by the print-queue worker with retries and form the reprint audit trail.</summary>
public class PrintJob : TenantEntity
{
    public Guid ItemId { get; set; }
    public Guid PrinterDeviceId { get; set; }
    public string Epc { get; set; } = "";
    public string Zpl { get; set; } = "";
    public PrintJobStatus Status { get; set; } = PrintJobStatus.Queued;
    public PrintReason Reason { get; set; } = PrintReason.Initial;
    public string? Note { get; set; }
    public int Copies { get; set; } = 1;
    public int Attempts { get; set; }
    public string? Error { get; set; }
    public Guid? RequestedBy { get; set; }
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    public DateTime? NextAttemptAt { get; set; }
    public DateTime? PrintedAt { get; set; }
}
