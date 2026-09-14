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
