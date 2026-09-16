namespace Rfid.Domain.Entities;

// ───────────────────────────── Anomaly detection ─────────────────────────────

/// <summary>A statistically unusual read pattern found by the detector (dedup'd per kind + subject while open).</summary>
public class Anomaly : TenantEntity
{
    public AnomalyKind Kind { get; set; }
    public Guid? DeviceId { get; set; }
    public Guid? ItemId { get; set; }
    public Guid? LocationId { get; set; }
    /// <summary>Standard deviations from the baseline (or a ratio for kinds without variance).</summary>
    public double Score { get; set; }
    public double Observed { get; set; }
    public double Expected { get; set; }
    public DateTime WindowStart { get; set; }
    public DateTime WindowEnd { get; set; }
    public string Message { get; set; } = "";
    public AnomalyStatus Status { get; set; } = AnomalyStatus.Open;
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
    public int Occurrences { get; set; } = 1;
    public Guid? AlertId { get; set; }
    public Dictionary<string, object?> Details { get; set; } = new();
}

// ───────────────────────────── GS1 encoding at scale ─────────────────────────────

/// <summary>A serial-number pool for one EPC scheme + company prefix + reference: hands out contiguous, never-reused serials.</summary>
public class SerialPool : TenantEntity
{
    public string Name { get; set; } = "";
    public EpcScheme Scheme { get; set; } = EpcScheme.Sgtin96;
    public string CompanyPrefix { get; set; } = "";
    /// <summary>SGTIN item reference / GRAI asset type / SSCC extension digit; empty for GIAI.</summary>
    public string Reference { get; set; } = "";
    public int Filter { get; set; } = 1;
    public ulong NextSerial { get; set; } = 1;
    public ulong? MaxSerial { get; set; }
    public Guid? ItemTypeId { get; set; }
    public string? Notes { get; set; }
    /// <summary>Optimistic concurrency so two encoders never receive the same serials.</summary>
    public int Version { get; set; }
}

/// <summary>One encoding run: which serials were turned into which EPCs, and what happened to them.</summary>
public class EncodingBatch : TenantEntity
{
    public string Name { get; set; } = "";
    public EpcScheme Scheme { get; set; }
    public string CompanyPrefix { get; set; } = "";
    public string Reference { get; set; } = "";
    public int Filter { get; set; }
    public ulong FirstSerial { get; set; }
    public ulong LastSerial { get; set; }
    public int Count { get; set; }
    public Guid? PoolId { get; set; }
    public Guid? ItemTypeId { get; set; }
    /// <summary>"tags" = unassigned tags created; "bind" = bound to items lacking tags; "items" = items created per serial.</summary>
    public string Mode { get; set; } = "tags";
    public int TagsCreated { get; set; }
    public int ItemsBound { get; set; }
    public int PrintJobs { get; set; }
    public Guid? UserId { get; set; }
    public string? FirstEpc { get; set; }
    public string? LastEpc { get; set; }
}

// ───────────────────────────── Audit & retention ─────────────────────────────

/// <summary>Who changed what: every mutating API call (except high-volume ingest) is recorded with a redacted request body.</summary>
public class AuditEntry : TenantEntity
{
    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    public Guid? DeviceId { get; set; }
    public string Method { get; set; } = "";
    public string Path { get; set; } = "";
    /// <summary>Controller/action name, e.g. "Items.Update".</summary>
    public string Action { get; set; } = "";
    public string? EntityType { get; set; }
    public Guid? EntityId { get; set; }
    public int StatusCode { get; set; }
    public string? Body { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
    public int DurationMs { get; set; }
}

/// <summary>How long each high-volume dataset is kept; null = forever. Applied by the nightly retention job.</summary>
public class RetentionPolicy : TenantEntity
{
    public string Dataset { get; set; } = "";
    public int? RetainDays { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTime? LastRunAt { get; set; }
    public int LastDeleted { get; set; }
    public long TotalDeleted { get; set; }
}
