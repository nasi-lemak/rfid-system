using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Rfid.Domain;
using Rfid.Edge;
using Rfid.Protocols;
using Xunit;

namespace Rfid.Tests;

/// <summary>Store-and-forward invariants of the edge agent and the server's handling of late batches.</summary>
public class EdgeAgentTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rfid-edge-tests", Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private sealed class FakeServer : IServerClient
    {
        public List<string> Posted { get; } = new();
        public Queue<PostOutcome> Script { get; } = new();
        public Task<(PostOutcome outcome, IngestResult? result, string? error)> PostBatchAsync(ReadBatchRequest batch, CancellationToken ct)
        {
            var outcome = Script.Count > 0 ? Script.Dequeue() : PostOutcome.Delivered;
            if (outcome == PostOutcome.Delivered) Posted.Add(batch.BatchId!);
            return Task.FromResult((outcome, outcome == PostOutcome.Delivered ? new IngestResult { Received = batch.Reads.Count } : null, outcome == PostOutcome.Delivered ? null : "boom"));
        }
        public Task HeartbeatAsync(Guid? deviceId, object metrics, CancellationToken ct) => Task.CompletedTask;
        public Task<EdgeConfig?> GetConfigAsync(CancellationToken ct) => Task.FromResult<EdgeConfig?>(null);
    }

    private static ReadBatchRequest Batch(string epc) => new() { DeviceId = Guid.NewGuid(), Reads = { new ReadRequest { Epc = epc, ReadAt = DateTime.UtcNow } } };

    [Fact]
    public void Batch_ids_are_monotonic_survive_restart_and_writes_are_durable()
    {
        var q = new EdgeQueue(_dir, "edge-7");
        var a = q.Enqueue(Batch("A")); var b = q.Enqueue(Batch("B"));
        Assert.Equal("edge-7:0000000001", a.BatchId); Assert.Equal("edge-7:0000000002", b.BatchId);
        Assert.Equal(2, q.Count);
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));

        var restarted = new EdgeQueue(_dir, "edge-7");            // process restart: pending batches still there, sequence continues
        Assert.Equal(2, restarted.Count); Assert.Equal(3, restarted.NextSequence);
        Assert.Equal("edge-7:0000000003", restarted.Enqueue(Batch("C")).BatchId);
        Assert.Equal("A", restarted.PeekOldest()!.Value.batch.Reads[0].Epc);
    }

    [Fact]
    public async Task Forwarder_preserves_order_across_failures_and_isolates_rejected_batches()
    {
        var q = new EdgeQueue(_dir, "edge-1");
        foreach (var e in new[] { "A", "B", "C" }) q.Enqueue(Batch(e));
        var server = new FakeServer();
        var fwd = new Forwarder(q, server, NullLogger.Instance);

        server.Script.Enqueue(PostOutcome.Delivered); server.Script.Enqueue(PostOutcome.Retry);
        Assert.Equal(1, await fwd.DrainAsync(default));                    // A delivered, B failed → stop, keep order
        Assert.Equal(1, fwd.Failures); Assert.True(fwd.Backoff >= TimeSpan.FromSeconds(5));
        Assert.Equal(2, q.Count); Assert.Equal("B", q.PeekOldest()!.Value.batch.Reads[0].Epc);

        server.Script.Enqueue(PostOutcome.Reject);                          // B permanently rejected → poison, C continues
        Assert.Equal(1, await fwd.DrainAsync(default));
        Assert.Equal(new[] { "edge-1:0000000001", "edge-1:0000000003" }, server.Posted);
        Assert.Equal(0, q.Count); Assert.Equal(1, q.PoisonCount); Assert.Equal(0, fwd.Failures);
    }

    [Fact]
    public void Queue_over_capacity_drops_the_oldest_into_poison_never_the_newest()
    {
        var q = new EdgeQueue(_dir, "edge-2", maxBatches: 2);
        q.Enqueue(Batch("A")); q.Enqueue(Batch("B")); q.Enqueue(Batch("C"));
        Assert.Equal(2, q.Count); Assert.Equal(1, q.PoisonCount);
        Assert.Equal("B", q.PeekOldest()!.Value.batch.Reads[0].Epc);
    }

    [Fact]
    public void Read_buffer_cuts_by_size_and_by_flush()
    {
        var buf = new ReadBuffer(maxReads: 3);
        var d1 = Guid.NewGuid(); var d2 = Guid.NewGuid();
        Assert.Null(buf.Add(d1, new[] { new ReadRequest { Epc = "1" }, new ReadRequest { Epc = "2" } }));
        var full = buf.Add(d1, new[] { new ReadRequest { Epc = "3" } });
        Assert.NotNull(full); Assert.Equal(3, full!.Reads.Count); Assert.Equal(d1, full.DeviceId);
        buf.Add(d2, new[] { new ReadRequest { Epc = "x" } });
        var flushed = buf.Flush();
        Assert.Single(flushed); Assert.Equal(d2, flushed[0].DeviceId); Assert.Equal(0, buf.Pending);
    }

    [Fact]
    public async Task Late_batches_are_recorded_but_never_move_state_backwards()
    {
        var h = new TestHost();
        var tool = await h.InstallTypeAsync("tool-tracking", "TOOL");
        var dock = h.Loc(LocationKind.Zone, "Dock"); var hangar = h.Loc(LocationKind.Zone, "Hangar");
        var (item, tag) = h.Item(tool, "TL-1", null);
        await h.SaveAsync();
        var t0 = DateTime.UtcNow.AddMinutes(-10);

        // The newer batch arrives first (the agent replays an older one after reconnecting).
        var newer = await h.Ingest.IngestAsync(new ReadBatchRequest { BatchId = "edge:2", Reads = { new() { Epc = tag.Epc, LocationId = hangar.Id, ReadAt = t0.AddMinutes(5) } } }, ReadSource.Fixed);
        var older = await h.Ingest.IngestAsync(new ReadBatchRequest { BatchId = "edge:1", Reads = { new() { Epc = tag.Epc, LocationId = dock.Id, ReadAt = t0 } } }, ReadSource.Fixed);

        Assert.Equal(0, newer.Late); Assert.Equal(1, older.Late); Assert.Equal(0, older.Events);
        var fresh = await h.Db.Items.FirstAsync(i => i.Id == item.Id);
        Assert.Equal(hangar.Id, fresh.CurrentLocationId); Assert.Equal(t0.AddMinutes(5), fresh.LastSeenAt);
        Assert.Equal(2, await h.Db.TagReads.CountAsync());                          // raw history is complete
        Assert.DoesNotContain(await h.Db.ItemEvents.Where(e => e.ItemId == item.Id).ToListAsync(), e => e.ToLocationId == dock.Id);
    }
}
