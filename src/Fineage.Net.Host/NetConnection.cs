using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading.Channels;
using Fineage.Net.Contract;
using Fineage.Protocol.Telemetry;

namespace Fineage.Net.Host;

/// <summary>
/// One accepted Net socket.
/// Inbound envelopes land in a bounded <see cref="Inbox"/>; outbound envelopes go through a bounded channel so a
/// slow client cannot grow server memory without limit. Fanout callers use <see cref="TrySend"/> and count a
/// <c>fineage.outbound.dropped</c> instead of blocking the simulation tick, the contract the retired V2
/// <c>PipelineConnection</c> offered.
/// </summary>
public sealed class NetConnection : IAsyncDisposable
{
    static long _nextConnectionId;

    readonly TcpClient _client;
    readonly NetworkStream _stream;
    readonly Channel<PacketEnvelope> _inbox;
    readonly Channel<PacketEnvelope> _outbound;
    readonly Channel<byte> _pumpWake;
    readonly CommandDeduper _deduper = new();
    readonly string _endpoint;
    readonly Task _pump;
    readonly ConcurrentDictionary<CoalesceReplaceableKey, PacketEnvelope> _coalescedReplaceable = new();
    int _closed;

    readonly record struct CoalesceReplaceableKey(ushort Opcode, ulong EntityValue, uint EntityGeneration);

    public ulong ConnectionId { get; } = (ulong)Interlocked.Increment(ref _nextConnectionId);

    public NetConnection(TcpClient client, int inboxCapacity, string endpoint = "net", int outboundCapacity = DefaultOutboundCapacity)
    {
        _client = client;
        _endpoint = endpoint;
        _stream = client.GetStream();
        // Wait (not DropOldest): trade/inventory/auth commands must not silently lose earlier
        // frames while later ones still run. Full inbox applies TCP backpressure via the read loop.
        // Movement coalesce can land later as a separate inbound class.
        _inbox = Channel.CreateBounded<PacketEnvelope>(new BoundedChannelOptions(inboxCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
        _outbound = Channel.CreateBounded<PacketEnvelope>(new BoundedChannelOptions(Math.Max(8, outboundCapacity))
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
        _pumpWake = Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });
        _pump = PumpAsync();
    }

    public const int DefaultOutboundCapacity = 256;

    public ChannelReader<PacketEnvelope> Inbox => _inbox.Reader;
    public bool HelloCompleted { get; set; }
    public uint FeatureFlags { get; set; }

    public async Task RunReadLoopAsync(CancellationToken ct)
    {
        try
        {
            await NetInboundPipe.ReadFramesAsync(_stream, AcceptFrameAsync, ct).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
        finally
        {
            _inbox.Writer.TryComplete();
        }
    }

    ValueTask AcceptFrameAsync(PacketEnvelope envelope, CancellationToken ct)
    {
        if (envelope.Major != PacketEnvelope.ProtocolMajor)
            return ValueTask.CompletedTask;

        if (envelope.Payload.Length > PacketEnvelope.MaxPayloadBytes)
            return ValueTask.CompletedTask;

        if (!_deduper.TryAccept(envelope.RequestSequence))
            return ValueTask.CompletedTask;

        NetTelemetry.FrameIn(_endpoint, envelope.Payload.Length);
        return _inbox.Writer.WriteAsync(envelope, ct);
    }

    /// <summary>
    /// Queues <paramref name="payload"/> and waits for room. Used by request/reply paths that must not lose a frame.
    /// </summary>
    public async Task SendAsync(ushort opcode, byte[] payload, uint requestSequence = 0, CancellationToken ct = default)
    {
        if (!TryEnvelope(opcode, payload, requestSequence, out PacketEnvelope envelope))
            return;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            await _outbound.Writer.WriteAsync(envelope, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            NetTelemetry.OutboundDropped(_endpoint);
        }
        catch (ChannelClosedException)
        {
            NetTelemetry.OutboundDropped(_endpoint);
        }
    }

    /// <summary>
    /// Fanout path: queue without waiting. A full queue drops the frame and counts it, so one stalled client
    /// cannot back-pressure the world tick.
    /// </summary>
    public bool TrySend(ushort opcode, byte[] payload, uint requestSequence = 0) =>
        TrySendFromStripe(opcode, payload, requestSequence);

    /// <summary>Called from <see cref="NetFanoutStripes"/> stripe workers (PR2).</summary>
    internal bool TrySendFromStripe(ushort opcode, byte[] payload, uint requestSequence = 0)
    {
        if (!TryEnvelope(opcode, payload, requestSequence, out PacketEnvelope envelope))
            return false;

        if (TryCoalescedSend(opcode, envelope))
            return true;

        if (_outbound.Writer.TryWrite(envelope))
            return true;

        NetTelemetry.ReliableOverflow(_endpoint);
        return false;
    }

    /// <summary>
    /// Replaceable move fanout: when the outbound channel is full, keep the latest MoveState /
    /// ChangeMoveType per entity instead of counting a drop. The pump writes stashed frames
    /// straight to the socket after each channel frame so concurrent <see cref="TrySend"/> fillers
    /// cannot starve coalesced moves by re-filling the channel (CI flake on loaded hosts).
    /// </summary>
    static bool IsReplaceablePresenceOpcode(ushort opcode) =>
        opcode is NetOpcode.MoveState or NetOpcode.ChangeMoveType
            or NetOpcode.StatusUpdate or NetOpcode.AbnormalStatusUpdate;

    bool TryCoalescedSend(ushort opcode, PacketEnvelope envelope)
    {
        if (!IsReplaceablePresenceOpcode(opcode))
            return false;

        if (!TryCoalesceEntityKey(envelope.Payload, out ulong entityValue, out uint generation))
            return false;

        var key = new CoalesceReplaceableKey(opcode, entityValue, generation);
        _coalescedReplaceable[key] = envelope;
        _pumpWake.Writer.TryWrite(1);
        return true;
    }

    static bool TryCoalesceEntityKey(byte[] payload, out ulong entityValue, out uint generation)
    {
        entityValue = 0;
        generation = 0;
        if (payload.Length < 12)
            return false;

        ReadOnlySpan<byte> span = payload;
        entityValue = BinaryPrimitives.ReadUInt64LittleEndian(span);
        generation = BinaryPrimitives.ReadUInt32LittleEndian(span[8..]);
        return true;
    }

    /// <summary>
    /// Best-effort: push stashed moves into the channel when there is room (Dispose / idle wake).
    /// The pump prefers <see cref="WriteCoalescedToStreamAsync"/> so stash never competes with fillers.
    /// </summary>
    void FlushCoalescedOutbound()
    {
        foreach (KeyValuePair<CoalesceReplaceableKey, PacketEnvelope> entry in _coalescedReplaceable.ToArray())
        {
            if (!_coalescedReplaceable.TryGetValue(entry.Key, out PacketEnvelope? envelope))
                continue;
            if (!_outbound.Writer.TryWrite(envelope))
                break;
            _coalescedReplaceable.TryRemove(entry.Key, out _);
        }
    }

    /// <summary>
    /// Pump-only: write every stashed MoveState / ChangeMoveType directly to the socket.
    /// Bypasses the bounded channel so fanout fillers cannot block the latest position forever.
    /// </summary>
    async Task WriteCoalescedToStreamAsync()
    {
        if (_coalescedReplaceable.IsEmpty)
            return;
        var batch = new List<PacketEnvelope>(_coalescedReplaceable.Count);
        foreach (KeyValuePair<CoalesceReplaceableKey, PacketEnvelope> entry in _coalescedReplaceable.ToArray())
        {
            if (!_coalescedReplaceable.TryRemove(entry.Key, out PacketEnvelope? envelope))
                continue;
            if (_coalescedReplaceable.TryGetValue(entry.Key, out PacketEnvelope? newer))
            {
                envelope = newer;
                _coalescedReplaceable.TryRemove(entry.Key, out _);
            }

            batch.Add(envelope);
        }

        if (batch.Count == 0)
            return;
        long started = Stopwatch.GetTimestamp();
        int frameBytes = batch.Sum(e => PacketEnvelope.HeaderSize + e.Payload.Length);
        await NetFraming.WriteManyAsync(_stream, batch, CancellationToken.None).ConfigureAwait(false);
        RecordSocketWrite(started, frameBytes, batch.Count);
        foreach (PacketEnvelope envelope in batch)
            NetTelemetry.FrameOut(_endpoint, envelope.Payload.Length);
    }

    void RecordSocketWrite(long startedAt, int bytes, int frames)
    {
        double ms = (Stopwatch.GetTimestamp() - startedAt) * 1000.0 / Stopwatch.Frequency;
        var endpoint = new KeyValuePair<string, object?>("endpoint", _endpoint);
        FineageTelemetry.SocketWriteMilliseconds.Record(ms, endpoint);
        FineageTelemetry.SocketWriteBytes.Record(bytes, endpoint);
        FineageTelemetry.SocketWriteFrames.Record(frames, endpoint);
    }

    /// <summary>
    /// An oversized payload cannot be framed, and letting <see cref="NetFraming"/> throw inside the pump would
    /// tear down the whole connection. Refuse it here and count it like any other dropped frame.
    /// </summary>
    bool TryEnvelope(ushort opcode, byte[] payload, uint requestSequence, out PacketEnvelope envelope)
    {
        byte[] body = payload ?? Array.Empty<byte>();
        if (body.Length > PacketEnvelope.MaxPayloadBytes)
        {
            NetTelemetry.OutboundDropped(_endpoint);
            envelope = null!;
            return false;
        }

        envelope = new PacketEnvelope
        {
            Opcode = opcode,
            Flags = (ushort)PacketFlags.Reliable,
            RequestSequence = requestSequence,
            Payload = body
        };
        return true;
    }

    async Task PumpAsync()
    {
        try
        {
            while (Volatile.Read(ref _closed) == 0)
            {
                Task outboundWait = _outbound.Reader.WaitToReadAsync().AsTask();
                Task wakeWait = _pumpWake.Reader.WaitToReadAsync().AsTask();
                await Task.WhenAny(outboundWait, wakeWait).ConfigureAwait(false);

                while (_outbound.Reader.TryRead(out PacketEnvelope? envelope) && envelope != null)
                {
                    long started = Stopwatch.GetTimestamp();
                    await NetFraming.WriteAsync(_stream, envelope, CancellationToken.None).ConfigureAwait(false);
                    RecordSocketWrite(started, PacketEnvelope.HeaderSize + envelope.Payload.Length, 1);
                    NetTelemetry.FrameOut(_endpoint, envelope.Payload.Length);
                    await WriteCoalescedToStreamAsync().ConfigureAwait(false);
                }

                while (_pumpWake.Reader.TryRead(out _))
                {
                }

                if (!_coalescedReplaceable.IsEmpty)
                    await WriteCoalescedToStreamAsync().ConfigureAwait(false);

                if (_outbound.Reader.Completion.IsCompleted)
                    break;
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException or InvalidOperationException)
        {
            /* peer gone, or a frame that cannot be written: the accept loop tears the connection down */
        }
        finally
        {
            // Last chance: drain replaceable MoveState that never fit the channel.
            try
            {
                await WriteCoalescedToStreamAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
            {
            }
            _outbound.Writer.TryComplete();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0)
            return;

        // Push any stashed MoveState before completing the channel so the pump can still send them.
        FlushCoalescedOutbound();
        _outbound.Writer.TryComplete();
        try
        {
            await _pump.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or OperationCanceledException or ObjectDisposedException)
        {
        }

        try { _stream.Dispose(); } catch { /* ignore */ }
        try { _client.Dispose(); } catch { /* ignore */ }
    }
}
