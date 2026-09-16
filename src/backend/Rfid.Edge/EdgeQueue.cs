using System.Text.Json;
using Rfid.Protocols;

namespace Rfid.Edge;

/// <summary>
/// Durable, ordered, file-backed queue of read batches. Each batch is one JSON file named by a monotonically
/// increasing sequence number; the batch id <c>{agentId}:{seq}</c> is what makes replays idempotent on the server.
/// Writes are atomic (temp file + rename), so a crash never leaves a half-written batch.
/// </summary>
public sealed class EdgeQueue
{
    private readonly string _dir; private readonly string _poison; private readonly string _agentId; private readonly int _maxBatches;
    private long _next; private readonly object _lock = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public EdgeQueue(string directory, string agentId, int maxBatches = 20_000)
    {
        _dir = Path.GetFullPath(directory); _poison = Path.Combine(_dir, "poison"); _agentId = agentId; _maxBatches = Math.Max(1, maxBatches);
        Directory.CreateDirectory(_dir); Directory.CreateDirectory(_poison);
        // Resume the sequence after a restart: batches already acknowledged are gone, pending ones are replayed in order.
        var all = Sequences(_dir).Concat(Sequences(_poison)).ToList();
        _next = all.Count == 0 ? 1 : all.Max() + 1;
    }

    public string AgentId => _agentId;
    public int Count => Sequences(_dir).Count();
    public long NextSequence { get { lock (_lock) return _next; } }
    public static string BatchId(string agentId, long seq) => $"{agentId}:{seq:D10}";

    /// <summary>Assigns the next batch id, persists the batch and returns it. Drops the oldest pending batch when over capacity.</summary>
    public ReadBatchRequest Enqueue(ReadBatchRequest batch)
    {
        lock (_lock)
        {
            var seq = _next++;
            batch.BatchId = BatchId(_agentId, seq);
            var final = FileFor(_dir, seq); var tmp = final + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(batch, Json));
            File.Move(tmp, final, overwrite: true);
            var pending = Sequences(_dir).OrderBy(x => x).ToList();
            foreach (var old in pending.Take(Math.Max(0, pending.Count - _maxBatches))) Poison(old, "queue over capacity");
            return batch;
        }
    }

    /// <summary>Oldest pending batch, or null.</summary>
    public (long seq, ReadBatchRequest batch)? PeekOldest()
    {
        lock (_lock)
        {
            foreach (var seq in Sequences(_dir).OrderBy(x => x))
            {
                try
                {
                    var b = JsonSerializer.Deserialize<ReadBatchRequest>(File.ReadAllText(FileFor(_dir, seq)), Json);
                    if (b != null) return (seq, b);
                    Poison(seq, "unreadable");
                }
                catch (JsonException) { Poison(seq, "corrupt json"); }
            }
            return null;
        }
    }

    public void Acknowledge(long seq) { lock (_lock) { var f = FileFor(_dir, seq); if (File.Exists(f)) File.Delete(f); } }

    /// <summary>Moves a batch the server permanently rejected (4xx) aside so it never blocks the queue, keeping it for inspection.</summary>
    public void Poison(long seq, string reason)
    {
        lock (_lock)
        {
            var f = FileFor(_dir, seq); if (!File.Exists(f)) return;
            File.Move(f, FileFor(_poison, seq), overwrite: true);
            File.WriteAllText(FileFor(_poison, seq) + ".reason", $"{DateTime.UtcNow:O} {reason}");
        }
    }

    public int PoisonCount => Sequences(_poison).Count();

    private static string FileFor(string dir, long seq) => Path.Combine(dir, $"{seq:D10}.json");
    private static IEnumerable<long> Sequences(string dir) => Directory.Exists(dir)
        ? Directory.EnumerateFiles(dir, "*.json").Select(f => long.TryParse(Path.GetFileNameWithoutExtension(f), out var n) ? n : -1).Where(n => n > 0)
        : Enumerable.Empty<long>();
}
