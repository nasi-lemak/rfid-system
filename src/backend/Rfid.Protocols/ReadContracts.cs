namespace Rfid.Protocols;

/// <summary>
/// The ingest contract: what every reader path (handheld, vendor webhook, MQTT, LLRP client, edge agent) produces
/// and what <c>POST /api/ingest/reads</c> accepts. Lives in the protocols library so the edge agent shares the
/// exact type with the server.
/// </summary>
public class ReadBatchRequest
{
    public Guid? DeviceId { get; set; }
    public string? SessionId { get; set; }
    /// <summary>Client-generated batch id (edge agents, handheld store-and-forward): a replayed batch is acknowledged, not re-applied.</summary>
    public string? BatchId { get; set; }
    public List<ReadRequest> Reads { get; set; } = new();
}

public class ReadRequest
{
    public string Epc { get; set; } = "";
    public string? Tid { get; set; }
    public int? AntennaPort { get; set; }
    public double? Rssi { get; set; }
    public DateTime? ReadAt { get; set; }
    /// <summary>Handhelds may state where they are instead of relying on antenna mapping.</summary>
    public Guid? LocationId { get; set; }
    /// <summary>Direct range to the antenna/anchor in metres (UWB / ranging beacons); takes precedence over RSSI for positioning.</summary>
    public double? RangeM { get; set; }
}

public class IngestResult
{
    public int Received { get; set; }
    public int Resolved { get; set; }
    public int Unknown { get; set; }
    public int Events { get; set; }
    public int Alerts { get; set; }
    /// <summary>Reads older than what the item had already been seen at (store-and-forward arriving out of order): recorded, but they never move state backwards.</summary>
    public int Late { get; set; }
    /// <summary>True when the batch id was already processed: the original result is returned and nothing is re-applied.</summary>
    public bool Duplicate { get; set; }
}
