namespace Rfid.Protocols;

/// <summary>Desired reader configuration an edge agent pulls from the platform (<c>GET /api/edge/config</c>).</summary>
public class EdgeConfig
{
    /// <summary>Content hash; the agent re-applies only when it changes.</summary>
    public string Revision { get; set; } = "";
    public Guid GatewayDeviceId { get; set; }
    public List<EdgeReader> Readers { get; set; } = new();
}

public class EdgeReader
{
    public Guid DeviceId { get; set; }
    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 5084;
    public double? PowerDbm { get; set; }
    public int Session { get; set; } = 1;
    public int TagPopulation { get; set; } = 32;
    public ushort[]? Antennas { get; set; }
    public int? GpiStartPort { get; set; }
    public bool Enabled { get; set; } = true;
}
