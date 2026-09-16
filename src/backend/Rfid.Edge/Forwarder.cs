using Microsoft.Extensions.Logging;

namespace Rfid.Edge;

/// <summary>
/// Drains the queue oldest-first. A batch is removed only after the server acknowledged it; transport failures
/// back off exponentially without reordering; permanent rejections are moved aside so one bad batch never
/// blocks the site. Because batch ids are monotonic per agent, redelivery after a crash is harmless.
/// </summary>
public sealed class Forwarder
{
    private readonly EdgeQueue _queue; private readonly IServerClient _server; private readonly ILogger _log; private readonly int _maxBackoffSeconds;
    public int Failures { get; private set; }
    public long Delivered { get; private set; }
    public long Duplicates { get; private set; }
    public string? LastError { get; private set; }
    public DateTime? LastDeliveryAt { get; private set; }

    public Forwarder(EdgeQueue queue, IServerClient server, ILogger log, int maxBackoffSeconds = 300) { _queue = queue; _server = server; _log = log; _maxBackoffSeconds = Math.Max(1, maxBackoffSeconds); }

    public TimeSpan Backoff => TimeSpan.FromSeconds(Math.Min(_maxBackoffSeconds, 5 * Math.Pow(2, Math.Min(Failures, 12))));

    /// <summary>Forwards pending batches until the queue is empty or a transport failure says wait. Returns how many were acknowledged.</summary>
    public async Task<int> DrainAsync(CancellationToken ct)
    {
        var sent = 0;
        while (!ct.IsCancellationRequested && _queue.PeekOldest() is { } next)
        {
            var (seq, batch) = next;
            var (outcome, result, error) = await _server.PostBatchAsync(batch, ct);
            switch (outcome)
            {
                case PostOutcome.Delivered:
                    _queue.Acknowledge(seq); sent++; Delivered++; Failures = 0; LastError = null; LastDeliveryAt = DateTime.UtcNow;
                    if (result?.Duplicate == true) Duplicates++;
                    break;
                case PostOutcome.Reject:
                    _log.LogWarning("Batch {BatchId} rejected by server: {Error}; moved to poison", batch.BatchId, error);
                    _queue.Poison(seq, error ?? "rejected"); LastError = error;
                    break;
                default:
                    Failures++; LastError = error;
                    _log.LogWarning("Batch {BatchId} not delivered ({Error}); {Pending} pending, retry in {Backoff}s", batch.BatchId, error, _queue.Count, (int)Backoff.TotalSeconds);
                    return sent;
            }
        }
        return sent;
    }
}
