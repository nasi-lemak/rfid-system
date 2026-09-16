namespace Rfid.Domain.Entities;

// ───────────────────────────── Reader health & firmware ─────────────────────────────

/// <summary>Periodic health sample from a reader/gateway/handheld (posted by the device, or recorded by the LLRP supervisor).</summary>
public class DeviceHeartbeat : TenantEntity
{
    public Guid DeviceId { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
    public string? FirmwareVersion { get; set; }
    public double? CpuPercent { get; set; }
    public double? MemoryPercent { get; set; }
    public double? TemperatureC { get; set; }
    public int? ReadsPerMinute { get; set; }
    public double? BatteryPercent { get; set; }
    public string? IpAddress { get; set; }
    public Dictionary<string, object?> Metrics { get; set; } = new();
}

/// <summary>A firmware image published for a vendor/model; rolled out to devices via FirmwareRollout.</summary>
public class FirmwareRelease : TenantEntity
{
    public string Vendor { get; set; } = "";
    public string Model { get; set; } = "";
    public string Version { get; set; } = "";
    public string? Url { get; set; }
    public string? Checksum { get; set; }
    public string? Notes { get; set; }
    public DateTime ReleasedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
}

/// <summary>One device's journey through a firmware update: the device polls for a pending rollout, downloads, installs and reports back.</summary>
public class FirmwareRollout : TenantEntity
{
    public Guid ReleaseId { get; set; }
    public Guid DeviceId { get; set; }
    public FirmwareRolloutStatus Status { get; set; } = FirmwareRolloutStatus.Pending;
    public DateTime ScheduledAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? Error { get; set; }
    public int Attempts { get; set; }
    public string? FromVersion { get; set; }
}

// ───────────────────────────── Geofencing ─────────────────────────────

public record GeoPoint(double Lat, double Lng);

/// <summary>A circle or polygon on the map. Items with GPS positions are tested against enabled fences on every fix.</summary>
public class GeoFence : TenantEntity
{
    public string Name { get; set; } = "";
    public GeoFenceKind Kind { get; set; } = GeoFenceKind.Circle;
    public double? CenterLat { get; set; }
    public double? CenterLng { get; set; }
    public double? RadiusM { get; set; }
    public List<GeoPoint> Points { get; set; } = new();
    /// <summary>When set, an item inside the fence is moved to this location (e.g. a Yard or Customer site).</summary>
    public Guid? LocationId { get; set; }
    public GeoFenceTrigger Trigger { get; set; } = GeoFenceTrigger.Both;
    public Guid? ItemTypeId { get; set; }
    public Severity Severity { get; set; } = Severity.Warning;
    public bool Enabled { get; set; } = true;
    public string? Color { get; set; }
    /// <summary>Optional: alert when an item stays inside longer than this (minutes), e.g. a returnable left at a customer.</summary>
    public int? MaxDwellMinutes { get; set; }
}

/// <summary>GPS position sample for an item (vehicle, container, trailer, tool crate…).</summary>
public class GpsFix : TenantEntity
{
    public Guid ItemId { get; set; }
    public double Lat { get; set; }
    public double Lng { get; set; }
    public double? SpeedKph { get; set; }
    public double? HeadingDeg { get; set; }
    public double? AccuracyM { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
    public Guid? DeviceId { get; set; }
}

/// <summary>Whether an item is currently inside a fence (drives enter/exit transitions and dwell alerts).</summary>
public class GeoFenceState : TenantEntity
{
    public Guid FenceId { get; set; }
    public Guid ItemId { get; set; }
    public bool Inside { get; set; }
    public DateTime Since { get; set; } = DateTime.UtcNow;
    public bool DwellAlerted { get; set; }
}

// ───────────────────────────── Dashboards ─────────────────────────────

public class Dashboard : TenantEntity
{
    public string Name { get; set; } = "";
    public bool IsDefault { get; set; }
    /// <summary>Null = shared with the whole tenant; otherwise private to that user.</summary>
    public Guid? OwnerUserId { get; set; }
    public List<DashboardWidget> Widgets { get; set; } = new();
}

/// <summary>
/// type: stat | alerts | breakdown | trend | presence | devices | stocktakes | list | fences | text.
/// config keys depend on the type (e.g. stat: filter/itemTypeId/locationId/state/status; breakdown: by=type|state|location|status;
/// trend: metric=reads|operations|events|alerts, days; list: source=alerts|events, take).
/// </summary>
public class DashboardWidget
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Type { get; set; } = "stat";
    public string Title { get; set; } = "";
    public int W { get; set; } = 1;
    public int H { get; set; } = 1;
    public Dictionary<string, object?> Config { get; set; } = new();
}

// ───────────────────────────── Notifications & escalation ─────────────────────────────

/// <summary>Where alerts go: e-mail (SMTP), SMS (Twilio-compatible REST), Teams / Slack incoming webhooks, or a generic webhook.</summary>
public class NotificationChannel : TenantEntity
{
    public string Name { get; set; } = "";
    public NotificationKind Kind { get; set; } = NotificationKind.Email;
    /// <summary>Email: host, port, useTls, username, password, from, to (comma list). Sms: accountSid, authToken, from, to, apiBase. Teams/Slack/Webhook: url.</summary>
    public Dictionary<string, object?> Config { get; set; } = new();
    public bool Enabled { get; set; } = true;
    /// <summary>Receive every alert at or above this severity even when no rule/policy names the channel.</summary>
    public bool CatchAll { get; set; }
    public Severity MinSeverity { get; set; } = Severity.Critical;
}

public class EscalationStep
{
    /// <summary>Minutes after the alert is raised (or after the previous step) before this step fires if still unacknowledged.</summary>
    public int AfterMinutes { get; set; }
    public List<Guid> ChannelIds { get; set; } = new();
    public string? Message { get; set; }
}

/// <summary>Ordered notification steps for alerts nobody acknowledges: step 0 immediately, later steps while the alert stays open.</summary>
public class EscalationPolicy : TenantEntity
{
    public string Name { get; set; } = "";
    public List<EscalationStep> Steps { get; set; } = new();
    /// <summary>Keep repeating the last step at its interval until acknowledged.</summary>
    public bool RepeatLastStep { get; set; }
    /// <summary>Applies to alerts raised without an explicit policy (geofence, device offline, …) at or above MinSeverity.</summary>
    public bool IsDefault { get; set; }
    public Severity MinSeverity { get; set; } = Severity.Warning;
}

public class NotificationLog : TenantEntity
{
    public Guid ChannelId { get; set; }
    public Guid? AlertId { get; set; }
    public string Recipient { get; set; } = "";
    public string Subject { get; set; } = "";
    public NotificationStatus Status { get; set; }
    public string? Error { get; set; }
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
    public int EscalationLevel { get; set; }
}

// ───────────────────────────── Warehouse export ─────────────────────────────

/// <summary>One export of a dataset window to the data-warehouse landing zone (Parquet or CSV, partitioned by day).</summary>
public class WarehouseExportRun : TenantEntity
{
    public string Dataset { get; set; } = "";
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public int Rows { get; set; }
    public string Path { get; set; } = "";
    public string Format { get; set; } = "parquet";
    public long Bytes { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public string? Error { get; set; }
    public bool Manual { get; set; }
}
