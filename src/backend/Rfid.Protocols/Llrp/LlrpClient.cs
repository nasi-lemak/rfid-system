using System.Net.Sockets;

namespace Rfid.Protocols.Llrp;

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
    public event Action<int, bool>? GpiChanged;
    public event Action<string>? Log;
    public bool Connected => _tcp?.Connected == true;
    public TimeSpan ResponseTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public ReaderCapabilities? Capabilities { get; private set; }
    public LlrpReaderOptions Options { get; private set; } = new();
    public DateTime? ConnectedAt { get; private set; }
    public long TagsReceived { get; private set; }

    public LlrpClient(string host, int port = 5084, uint roSpecId = 1) { _host = host; _port = port; _roSpecId = roSpecId; }

    /// <summary>Connects, waits for the reader's connection event, resets config and starts the inventory.</summary>
    public Task StartAsync(CancellationToken ct = default) => StartAsync(new LlrpReaderOptions(), ct);

    /// <summary>Connect, read capabilities, apply power/session/antenna configuration and start a continuous or GPI-triggered inventory.</summary>
    public async Task StartAsync(LlrpReaderOptions options, CancellationToken ct = default)
    {
        Options = options;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _tcp = new TcpClient();
        await _tcp.ConnectAsync(_host, _port, ct);
        _stream = _tcp.GetStream();
        var connectEvent = new TaskCompletionSource<LlrpMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_pending) _pending[LlrpMsg.ReaderEventNotification] = connectEvent;
        _ = Task.Run(() => ReadLoopAsync(_cts.Token), _cts.Token);
        await connectEvent.Task.WaitAsync(ResponseTimeout, ct);
        ConnectedAt = DateTime.UtcNow;
        Log?.Invoke($"connected to {_host}:{_port}");
        try
        {
            var capsTcs = new TaskCompletionSource<LlrpMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_pending) _pending[LlrpParamEx.MsgGetReaderCapabilitiesResponse] = capsTcs;
            await SendAsync(LlrpConfigCodec.GetReaderCapabilities(NextId()), ct);
            var caps = await capsTcs.Task.WaitAsync(ResponseTimeout, ct);
            Capabilities = LlrpConfigCodec.ParseCapabilities(caps.Body);
            Log?.Invoke($"{Capabilities.Manufacturer} model {Capabilities.ModelId} fw {Capabilities.Firmware}: {Capabilities.MaxAntennas} antennas, {Capabilities.Gpis} GPI / {Capabilities.Gpos} GPO, {Capabilities.PowerTable.Count} power levels");
        }
        catch (TimeoutException) { Log?.Invoke("reader did not answer GET_READER_CAPABILITIES; continuing without power table"); }
        await ExpectOkAsync(LlrpCodec.SetReaderConfig(NextId(), options.KeepaliveMs), LlrpMsg.SetReaderConfigResponse, ct);
        if (options.TransmitPowerDbm.HasValue || options.Session != 1 || options.TagPopulation != 32 || options.AntennaIds is { Length: > 0 })
            await ExpectOkAsync(LlrpConfigCodec.SetAntennaConfig(NextId(), options, Capabilities), LlrpMsg.SetReaderConfigResponse, ct);
        await ExpectOkAsync(LlrpCodec.DeleteRoSpec(NextId(), 0), LlrpMsg.DeleteRoSpecResponse, ct);
        var addRoSpec = options.GpiStartPort is int gpi
            ? LlrpConfigCodec.AddRoSpecGpiTriggered(NextId(), _roSpecId, gpi, (ushort)options.ReportEveryNTags, options.AntennaIds)
            : LlrpCodec.AddRoSpec(NextId(), _roSpecId, (ushort)options.ReportEveryNTags, options.AntennaIds);
        await ExpectOkAsync(addRoSpec, LlrpMsg.AddRoSpecResponse, ct);
        await ExpectOkAsync(LlrpCodec.EnableRoSpec(NextId(), _roSpecId), LlrpMsg.EnableRoSpecResponse, ct);
        if (options.GpiStartPort == null) await ExpectOkAsync(LlrpCodec.StartRoSpec(NextId(), _roSpecId), LlrpMsg.StartRoSpecResponse, ct);
        Log?.Invoke(options.GpiStartPort is int g ? $"inventory armed on GPI {g}" : "inventory started");
    }

    /// <summary>Writes a general-purpose output (stack light, buzzer, gate).</summary>
    public Task SetGpoAsync(int port, bool state, CancellationToken ct = default) => ExpectOkAsync(LlrpConfigCodec.SetGpo(NextId(), port, state), LlrpMsg.SetReaderConfigResponse, ct);

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
                    case LlrpMsg.RoAccessReport: { var tags = LlrpCodec.ParseRoAccessReport(body); if (tags.Count > 0) { TagsReceived += tags.Count; TagsReported?.Invoke(tags); } break; }
                    case LlrpMsg.Keepalive: await SendAsync(LlrpCodec.KeepaliveAck(id), ct); break;
                    case LlrpMsg.ReaderEventNotification:
                    {
                        var gpis = LlrpConfigCodec.ParseGpiEvents(body);
                        foreach (var (port, high) in gpis) GpiChanged?.Invoke(port, high);
                        TaskCompletionSource<LlrpMessage>? evTcs;
                        lock (_pending) { if (_pending.TryGetValue(type, out evTcs)) _pending.Remove(type); }
                        evTcs?.TrySetResult(msg);
                        break;
                    }
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
