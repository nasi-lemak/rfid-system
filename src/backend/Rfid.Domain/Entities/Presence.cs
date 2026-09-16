namespace Rfid.Domain.Entities;

/// <summary>
/// A continuous period during which an item was observed inside a zone. Opened on the first read at a
/// location, refreshed by each read, closed by the presence sweeper when no read arrives within the
/// zone's dwell timeout (or immediately when the item is seen elsewhere). Powers zone occupancy,
/// muster roll-calls and dwell-time analytics for active RFID / BLE / UWB deployments.
/// </summary>
public class PresenceSession : TenantEntity
{
    public Guid ItemId { get; set; }
    public Guid LocationId { get; set; }
    public Guid? DeviceId { get; set; }
    public DateTime EnteredAt { get; set; }
    public DateTime LastSeenAt { get; set; }
    public DateTime? ExitedAt { get; set; }
    public int ReadCount { get; set; }
    public double? LastRssi { get; set; }
    public double? PeakRssi { get; set; }
}

/// <summary>Recurring stocktake definition executed by the scheduler background service.</summary>
public class StocktakeSchedule : TenantEntity
{
    public string Name { get; set; } = "";
    public Guid LocationId { get; set; }
    public Guid? ItemTypeId { get; set; }
    public int IntervalDays { get; set; } = 7;
    /// <summary>Local wall-clock time (UTC) at which the stocktake should be opened.</summary>
    public TimeSpan TimeOfDay { get; set; } = TimeSpan.FromHours(6);
    public DateTime NextRunAt { get; set; }
    public DateTime? LastRunAt { get; set; }
    public Guid? LastStocktakeId { get; set; }
    public bool Enabled { get; set; } = true;
    /// <summary>When set, an open scheduled stocktake is reconciled automatically after this many hours.</summary>
    public int? AutoReconcileHours { get; set; }
}

/// <summary>
/// Outbound integration (ERP / EAM / BI / webhook). Delivery is cursor based: the dispatcher sends every
/// new item event (and optionally alert) since the stored cursor in signed batches with retry/backoff,
/// so no event is lost even if the endpoint is down for a while.
/// </summary>
public class IntegrationEndpoint : TenantEntity
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    /// <summary>Shared secret for the HMAC-SHA256 signature sent in X-Rfid-Signature.</summary>
    public string? Secret { get; set; }
    public bool Enabled { get; set; } = true;
    /// <summary>Empty = all event types.</summary>
    public List<ItemEventType> EventTypes { get; set; } = new();
    /// <summary>Empty = all item types; otherwise only events/alerts about items of these type codes.</summary>
    public List<string> ItemTypeCodes { get; set; } = new();
    /// <summary>When set, only events at (or about items currently in) this site subtree.</summary>
    public Guid? SiteLocationId { get; set; }
    public bool IncludeAlerts { get; set; } = true;
    /// <summary>The outbox message currently carrying this endpoint's next batch; one in flight at a time keeps ordering.</summary>
    public Guid? InFlightMessageId { get; set; }
    public Dictionary<string, object?> Headers { get; set; } = new();
    public int BatchSize { get; set; } = 100;
    public DateTime EventCursor { get; set; } = DateTime.UtcNow;
    public DateTime AlertCursor { get; set; } = DateTime.UtcNow;
    public DateTime? LastDeliveryAt { get; set; }
    public string? LastError { get; set; }
    public int FailureCount { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    public long DeliveredCount { get; set; }
    /// <summary>Payload shape: generic envelope or a vendor-specific mapping (SAP asset master, Dynamics 365 customer asset, IBM Maximo MXASSET).</summary>
    public IntegrationFormat Format { get; set; } = IntegrationFormat.Generic;
    public IntegrationAuth AuthType { get; set; } = IntegrationAuth.None;
    public string? ApiToken { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? TokenUrl { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? Scope { get; set; }
    /// <summary>Vendor mapping hints, e.g. {"companyCode":"1000","costCenterAttribute":"costCentre","siteId":"BEDFORD"}.</summary>
    public Dictionary<string, object?> Mapping { get; set; } = new();
}
