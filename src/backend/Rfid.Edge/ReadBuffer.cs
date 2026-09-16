using Rfid.Protocols;

namespace Rfid.Edge;

/// <summary>Accumulates reads per reader and cuts them into batches by time or size. Thread-safe.</summary>
public sealed class ReadBuffer
{
    private readonly Dictionary<Guid, List<ReadRequest>> _pending = new();
    private readonly object _lock = new();
    private readonly int _maxReads;
    public ReadBuffer(int maxReads = 500) => _maxReads = Math.Max(1, maxReads);

    /// <summary>Adds reads; returns a full batch when the per-device buffer reached its size limit.</summary>
    public ReadBatchRequest? Add(Guid deviceId, IEnumerable<ReadRequest> reads, string? sessionId = null)
    {
        lock (_lock)
        {
            if (!_pending.TryGetValue(deviceId, out var list)) _pending[deviceId] = list = new();
            list.AddRange(reads);
            if (list.Count < _maxReads) return null;
            _pending.Remove(deviceId);
            return new ReadBatchRequest { DeviceId = deviceId, SessionId = sessionId ?? "edge", Reads = list };
        }
    }

    /// <summary>Cuts every non-empty buffer into a batch (called on the flush timer).</summary>
    public List<ReadBatchRequest> Flush()
    {
        lock (_lock)
        {
            var batches = _pending.Where(kv => kv.Value.Count > 0).Select(kv => new ReadBatchRequest { DeviceId = kv.Key, SessionId = "edge", Reads = kv.Value }).ToList();
            _pending.Clear();
            return batches;
        }
    }

    public int Pending { get { lock (_lock) return _pending.Sum(kv => kv.Value.Count); } }
}
