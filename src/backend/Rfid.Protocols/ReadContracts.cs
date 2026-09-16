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

/// <summary>
/// Position ingest: x/y (metres) inside a floor-plan location, produced by an external RTLS engine (UWB TDoA,
/// BLE AoA, vision, …) or an edge agent. The platform stores fixes and updates the item's position exactly as
/// its own trilateration does; solvers stay with the vendor.
/// </summary>
public class PositionBatchRequest
{
    public Guid? DeviceId { get; set; }
    /// <summary>Client batch id for store-and-forward idempotency (same semantics as reads).</summary>
    public string? BatchId { get; set; }
    public List<PositionFixRequest> Fixes { get; set; } = new();
}

public class PositionFixRequest
{
    public string? Epc { get; set; }
    public Guid? ItemId { get; set; }
    /// <summary>The floor-plan location the coordinates are relative to.</summary>
    public Guid LocationId { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double? Z { get; set; }
    public double? AccuracyM { get; set; }
    public DateTime? At { get; set; }
}

public class PositionIngestResult
{
    public int Received { get; set; }
    public int Applied { get; set; }
    public int Unknown { get; set; }
    public int Rejected { get; set; }
    public bool Duplicate { get; set; }
}

/// <summary>MQTT topic-filter matching shared by the server subscriber and the edge bridge.</summary>
public static class TopicMatcher
{
    /// <summary><c>+</c> matches one level, <c>#</c> the rest.</summary>
    public static bool Matches(string pattern, string topic)
    {
        var p = pattern.Split('/'); var t = topic.Split('/');
        for (var i = 0; i < p.Length; i++)
        {
            if (p[i] == "#") return true;
            if (i >= t.Length) return false;
            if (p[i] != "+" && p[i] != t[i]) return false;
        }
        return p.Length == t.Length;
    }
}
