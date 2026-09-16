namespace Rfid.Domain.Entities;

// ───────────────────────────── Returnable-asset billing ─────────────────────────────

/// <summary>Pricing for custody of an item type (or all types): deposit, per-cycle fee, daily rental after free days, late and loss fees.</summary>
public class RateCard : TenantEntity
{
    public string Name { get; set; } = "";
    public Guid? ItemTypeId { get; set; }
    public PartyKind? PartyKind { get; set; }
    public Guid? PartyId { get; set; }
    public string Currency { get; set; } = "USD";
    public decimal DepositAmount { get; set; }
    public decimal CycleFee { get; set; }
    public decimal DailyFee { get; set; }
    public int FreeDays { get; set; }
    public decimal LateFeePerDay { get; set; }
    public decimal LossFee { get; set; }
    public bool Enabled { get; set; } = true;
    public int Priority { get; set; }
}

/// <summary>One charge or credit on a party's account, created from custody events by the billing accrual.</summary>
public class LedgerEntry : TenantEntity
{
    public Guid PartyId { get; set; }
    public Guid? ItemId { get; set; }
    public Guid? ItemEventId { get; set; }
    public Guid? OperationId { get; set; }
    public Guid? RateCardId { get; set; }
    public LedgerKind Kind { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public string Description { get; set; } = "";
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public Guid? InvoiceId { get; set; }
    public int Quantity { get; set; } = 1;
}

public class InvoiceLine
{
    public string Kind { get; set; } = "";
    public string Description { get; set; } = "";
    public int Quantity { get; set; }
    public decimal Amount { get; set; }
}

public class Invoice : TenantEntity
{
    public string Number { get; set; } = "";
    public Guid PartyId { get; set; }
    public DateTime PeriodFrom { get; set; }
    public DateTime PeriodTo { get; set; }
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;
    public string Currency { get; set; } = "USD";
    public decimal Charges { get; set; }
    public decimal Credits { get; set; }
    public decimal Total { get; set; }
    public DateTime? IssuedAt { get; set; }
    public DateTime? DueAt { get; set; }
    public DateTime? PaidAt { get; set; }
    public string? Notes { get; set; }
    public List<InvoiceLine> Lines { get; set; } = new();
}

/// <summary>Watermark for the billing accrual so custody events are charged exactly once.</summary>
public class BillingCursor : TenantEntity
{
    public DateTime AccruedTo { get; set; }
}

// ───────────────────────────── Predictive maintenance ─────────────────────────────

/// <summary>Daily snapshot of an item's maintenance risk (0–100), the factors behind it and the predicted service date.</summary>
public class MaintenanceForecast : TenantEntity
{
    public Guid ItemId { get; set; }
    public double Risk { get; set; }
    public string Level { get; set; } = "Low";
    public DateTime? PredictedServiceAt { get; set; }
    public string? PredictedBy { get; set; }
    public Dictionary<string, object?> Factors { get; set; } = new();
    public DateTime ComputedAt { get; set; } = DateTime.UtcNow;
    public Guid? AlertId { get; set; }
}

// ───────────────────────────── EPCIS 2.0 ─────────────────────────────

/// <summary>An inbound EPCIS capture job (EPCISDocument) and what became of it.</summary>
public class EpcisCapture : TenantEntity
{
    public int Events { get; set; }
    public int Applied { get; set; }
    public int Rejected { get; set; }
    public string Status { get; set; } = "Completed";
    public string? Errors { get; set; }
    public Guid? UserId { get; set; }
    public Guid? DeviceId { get; set; }
}
