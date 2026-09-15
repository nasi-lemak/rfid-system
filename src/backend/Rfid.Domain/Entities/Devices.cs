namespace Rfid.Domain.Entities;

public class Device : TenantEntity
{
    public string Name { get; set; } = "";
    public DeviceKind Kind { get; set; }
    public string? SerialNumber { get; set; }
    public string? Model { get; set; }
    public Guid? SiteLocationId { get; set; }
    public string? TokenHash { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public Dictionary<string, object?> Config { get; set; } = new();
    public List<Antenna> Antennas { get; set; } = new();
    /// <summary>Health SLA: expected heartbeat/read interval in minutes (null → tenant default). Offline when exceeded ×2, degraded when exceeded.</summary>
    public int? HeartbeatSlaMinutes { get; set; }
    public DateTime? LastHeartbeatAt { get; set; }
    public string? FirmwareVersion { get; set; }
    public DeviceHealth Health { get; set; } = DeviceHealth.Unknown;
    public DateTime? HealthChangedAt { get; set; }
    /// <summary>For telematics units: the item (vehicle, trailer, container) whose GPS position this device reports.</summary>
    public Guid? TrackedItemId { get; set; }
}

public class Antenna : TenantEntity
{
    public Guid DeviceId { get; set; }
    public Device? Device { get; set; }
    public int Port { get; set; }
    public Guid? LocationId { get; set; }
    public Location? Location { get; set; }
    public AntennaDirection Direction { get; set; } = AntennaDirection.None;
    public double? PowerDbm { get; set; }
    /// <summary>Anchor coordinates (metres) inside the zone, for RSSI trilateration of BLE/UWB/active tags.</summary>
    public double? X { get; set; }
    public double? Y { get; set; }
    /// <summary>Path-loss model: RSSI measured at 1 m (default -45 dBm) and exponent (default 2.2).</summary>
    public double? RssiAt1m { get; set; }
    public double? PathLossExponent { get; set; }
}

public class TagRead : TenantEntity
{
    public string Epc { get; set; } = "";
    public string? Tid { get; set; }
    public Guid? ItemId { get; set; }
    public Guid? DeviceId { get; set; }
    public int? AntennaPort { get; set; }
    public double? Rssi { get; set; }
    public DateTime ReadAt { get; set; } = DateTime.UtcNow;
    public Guid? LocationId { get; set; }
    public ReadSource Source { get; set; } = ReadSource.Fixed;
    public string? SessionId { get; set; }
}
