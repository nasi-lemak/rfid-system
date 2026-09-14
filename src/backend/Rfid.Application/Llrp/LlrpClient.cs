using System.Net.Sockets;

namespace Rfid.Application.Llrp;

/// <summary>
/// Minimal LLRP client: connects to a reader (default port 5084), configures a continuous Gen2 inventory
/// ROSpec on all antennas and streams tag reports. Works with Impinj Speedway/R700 (LLRP mode), Zebra
/// FX-series, Alien and other LLRP 1.0.1 readers.
/// </summary>
public sealed class LlrpClient : IAsyncDisposable
{
    private readonly string _host; private readonly int _port; private readonly uint _roSpecId;
    private TcpClient? _tcp; private NetworkStream? _stream; private uint _msgId = 1;
    private readonly Dictionary<ushort, TaskCompletionSource<LlrpMessage>> _pending = new();
    private CancellationTokenSource? _cts;

    public event Action<IReadOnlyList<LlrpTag>>? TagsReported;
    public event Action<string>? Log;
    public bool Connected => _tcp?.Connected == true;
    public TimeSpan ResponseTimeout { get; set; } = TimeSpan.FromSeconds(10);

    public LlrpClient(string host, int port = 5084, uint roSpecId = 1) { _host = host; _port = port; _roSpecId = roSpecId; }

    /// <summary>Connects, waits for the reader's connection event, resets config and starts the inventory.</summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _tcp = new TcpClient();
        await _tcp.ConnectAsync(_host, _port, ct);
        _stream = _tcp.GetStream();
        var connectEvent = new TaskCompletionSource<LlrpMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_pending) _pending[LlrpMsg.ReaderEventNotification] = connectEvent;
        _ = Task.Run(() => ReadLoopAsync(_cts.Token), _cts.Token);
        await connectEvent.Task.WaitAsync(ResponseTimeout, ct);
        Log?.Invoke($"connected to {_host}:{_port}");
        await ExpectOkAsync(LlrpCodec.SetReaderConfig(NextId()), LlrpMsg.SetReaderConfigResponse, ct);
        await ExpectOkAsync(LlrpCodec.DeleteRoSpec(NextId(), 0), LlrpMsg.DeleteRoSpecResponse, ct);
        await ExpectOkAsync(LlrpCodec.AddRoSpec(NextId(), _roSpecId), LlrpMsg.AddRoSpecResponse, ct);
        await ExpectOkAsync(LlrpCodec.EnableRoSpec(NextId(), _roSpecId), LlrpMsg.EnableRoSpecResponse, ct);
        await ExpectOkAsync(LlrpCodec.StartRoSpec(NextId(), _roSpecId), LlrpMsg.StartRoSpecResponse, ct);
        Log?.Invoke("inventory started");
    }

    private uint NextId() => Interlocked.Increment(ref _msgId);

    private async Task ExpectOkAsync(byte[] request, ushort responseType, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<LlrpMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_pending) _pending[responseType] = tcs;
        await SendAsync(request, ct);
        var resp = await tcs.Task.WaitAsync(ResponseTimeout, ct);
        var (code, desc) = LlrpCodec.ParseStatus(resp.Body);
        if (code != 0) throw new InvalidOperationException($"LLRP message {responseType} failed: {code} {desc}");
    }

    private async Task SendAsync(byte[] data, CancellationToken ct)
    {
        if (_stream == null) throw new InvalidOperationException("not connected");
        await _stream.WriteAsync(data, ct);
        await _stream.FlushAsync(ct);
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var header = new byte[LlrpCodec.HeaderLength];
        try
        {
            while (!ct.IsCancellationRequested && _stream != null)
            {
                await ReadExactlyAsync(header, ct);
                var (type, length, id) = LlrpCodec.DecodeHeader(header);
                var body = new byte[Math.Max(0, length - LlrpCodec.HeaderLength)];
                await ReadExactlyAsync(body, ct);
                var msg = new LlrpMessage(type, id, body);
                switch (type)
                {
                    case LlrpMsg.RoAccessReport: { var tags = LlrpCodec.ParseRoAccessReport(body); if (tags.Count > 0) TagsReported?.Invoke(tags); break; }
                    case LlrpMsg.Keepalive: await SendAsync(LlrpCodec.KeepaliveAck(id), ct); break;
                    default:
                    {
                        TaskCompletionSource<LlrpMessage>? tcs;
                        lock (_pending) { if (_pending.TryGetValue(type, out tcs)) _pending.Remove(type); }
                        if (tcs != null) tcs.TrySetResult(msg); else Log?.Invoke($"unsolicited message type {type}");
                        break;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException || ex is IOException || ex is ObjectDisposedException || ex is SocketException) { Log?.Invoke($"connection closed: {ex.Message}"); }
        lock (_pending) { foreach (var p in _pending.Values) p.TrySetException(new IOException("LLRP connection closed")); _pending.Clear(); }
    }

    private async Task ReadExactlyAsync(byte[] buffer, CancellationToken ct)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await _stream!.ReadAsync(buffer.AsMemory(read), ct);
            if (n == 0) throw new IOException("remote closed");
            read += n;
        }
    }

    public async Task StopAsync()
    {
        try { if (Connected) { await SendAsync(LlrpCodec.StopRoSpec(NextId(), _roSpecId), CancellationToken.None); await SendAsync(LlrpCodec.CloseConnection(NextId()), CancellationToken.None); } } catch { /* best effort */ }
        _cts?.Cancel(); _stream?.Dispose(); _tcp?.Dispose(); _tcp = null; _stream = null;
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
