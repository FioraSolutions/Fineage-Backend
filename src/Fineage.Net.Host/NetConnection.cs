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
/// Inbound envelopes land in a bounded <see cref="Inbox"/>; outbound envelopes go through a bounded channel of
/// batches so a slow client cannot grow server memory without limit and a burst is one socket write.
/// Fanout callers that must not block use <see cref="TrySend"/> and count a <c>fineage.outbound.dropped</c>
/// instead of blocking the simulation tick. Stripe delivery uses <see cref="SubmitOrderedBatch"/>, which waits
/// for room and does not drop reliable frames.
/// </summary>
public sealed class NetConnection : IAsyncDisposable
{
    static long _nextConnectionId;

    /// <summary>Frames combined into one socket write before the next batch is queued (issue #867 PR3).</summary>
    internal const int MaxFramesPerBatch = 32;

    /// <summary>Bytes combined into one socket write, including frame headers.</summary>
    internal const int MaxBytesPerBatch = 64 * 1024;

    readonly TcpClient _client;
    readonly NetworkStream _stream;
    readonly Channel<PacketEnvelope> _inbox;
    readonly Channel<OutboundBatch> _batches;
    readonly Channel<byte> _pumpWake;
    readonly CommandDeduper _deduper = new();
    readonly string _endpoint;
    readonly Task _pump;
    readonly ConcurrentDictionary<CoalesceReplaceableKey, PacketEnvelope> _coalescedReplaceable = new();
    int _closed;
    int _boundObjectId;
    uint _boundGeneration;

    const int MaxCoalescedReplaceableEntries = 256;

    readonly record struct CoalesceReplaceableKey(ushort Opcode, ulong EntityValue, uint EntityGeneration);

    readonly record struct OutboundBatch(PacketEnvelope[] Frames);

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
        _batches = Channel.CreateBounded<OutboundBatch>(new BoundedChannelOptions(Math.Max(8, outboundCapacity))
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

    /// <summary>Publishes the player object this socket is currently bound to. Called by the presence hub before the handle is visible.</summary>
    internal void BindPresence(int objectId, uint generation)
    {
        Volatile.Write(ref _boundObjectId, objectId);
        Volatile.Write(ref _boundGeneration, generation);
    }

    /// <summary>Clears the binding when it still names <paramref name="objectId"/> at <paramref name="generation"/>.</summary>
    internal void UnbindPresence(int objectId, uint generation)
    {
        if (Volatile.Read(ref _boundObjectId) == objectId && Volatile.Read(ref _boundGeneration) == generation)
        {
            Volatile.Write(ref _boundGeneration, 0);
            Volatile.Write(ref _boundObjectId, 0);
        }
    }

    /// <summary>Send-path generation check. No presence lock: the connection publishes its own binding.</summary>
    internal bool IsBoundTo(ConnectionHandle handle) =>
        Volatile.Read(ref _closed) == 0
        && Volatile.Read(ref _boundObjectId) == handle.ObjectId
        && Volatile.Read(ref _boundGeneration) == handle.EntityGeneration;

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
        catch (Exception ex) when (ex is IOException or SocketException)
        {
            NetDisconnectDiagnostics.Log("read", ConnectionId, _endpoint, ex, _outbound.Reader.Count, StripeBatchDepth());
        }
        finally
        {
            _inbox.Writer.TryComplete();
        }
    }

    int StripeBatchDepth()
    {
        lock (_stripeBatchLock)
            return _stripeBatch.Count;
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

        if (OutboundOrdering.IsBarrier(opcode))
            FlushCoalescedBeforeBarrier(wait: false);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            await _batches.Writer.WriteAsync(new OutboundBatch([envelope]), timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            NetTelemetry.OutboundDropped(_endpoint);
            NetHostLog.Error(
                $"connectionId={ConnectionId} endpoint={_endpoint} dropped reliable opcode={opcode}: outbound batch queue did not accept the frame within 2s");
        }
        catch (ChannelClosedException)
        {
            NetTelemetry.OutboundDropped(_endpoint);
            NetHostLog.Error(
                $"connectionId={ConnectionId} endpoint={_endpoint} dropped reliable opcode={opcode}: outbound batch queue is closed");
        }
    }

    /// <summary>
    /// Non-blocking enqueue. A full batch queue drops the frame, counts it, and logs the opcode.
    /// Replaceable presence frames coalesce instead of dropping. Stripe delivery does not use this path.
    /// </summary>
    public bool TrySend(ushort opcode, byte[] payload, uint requestSequence = 0)
    {
        if (!TryEnvelope(opcode, payload, requestSequence, out PacketEnvelope envelope))
            return false;

        if (OutboundOrdering.IsBarrier(opcode))
            FlushCoalescedBeforeBarrier(wait: false);

        if (TryCoalescedSend(opcode, envelope))
            return true;

        if (_batches.Writer.TryWrite(new OutboundBatch([envelope])))
            return true;

        NetTelemetry.OutboundDropped(_endpoint);
        NetHostLog.Error(
            $"connectionId={ConnectionId} endpoint={_endpoint} dropped reliable opcode={opcode}: outbound batch queue is full (queued={_batches.Reader.Count})");
        return false;
    }

    /// <summary>
    /// Stripe path: queue every frame in order, splitting only on the batch byte/count bound, and wait for room.
    /// Reliable frames are not dropped. A closed connection logs and returns.
    /// </summary>
    internal void SubmitOrderedBatch(List<OutboundFrame> frames)
    {
        if (frames.Count == 0)
            return;

        if (Volatile.Read(ref _closed) != 0)
        {
            NetHostLog.Error(
                $"connectionId={ConnectionId} endpoint={_endpoint} rejected {frames.Count} ordered frames: connection is closed");
            return;
        }

        for (int i = 0; i < frames.Count; i++)
        {
            if (OutboundOrdering.IsBarrier(frames[i].Opcode))
            {
                FlushCoalescedBeforeBarrier(wait: true);
                break;
            }
        }

        var chunk = new List<PacketEnvelope>(Math.Min(frames.Count, MaxFramesPerBatch));
        int bytes = 0;
        for (int i = 0; i < frames.Count; i++)
        {
            if (!TryEnvelope(frames[i].Opcode, frames[i].Payload, 0, out PacketEnvelope envelope))
                continue;

            int size = PacketEnvelope.HeaderSize + envelope.Payload.Length;
            if (chunk.Count > 0 && (chunk.Count >= MaxFramesPerBatch || bytes + size > MaxBytesPerBatch))
            {
                WriteBatchWait(chunk);
                chunk = new List<PacketEnvelope>(Math.Min(frames.Count - i, MaxFramesPerBatch));
                bytes = 0;
            }

            chunk.Add(envelope);
            bytes += size;
        }

        if (chunk.Count > 0)
            WriteBatchWait(chunk);
    }

    void WriteBatchWait(List<PacketEnvelope> chunk)
    {
        var batch = new OutboundBatch(chunk.ToArray());
        try
        {
            _batches.Writer.WriteAsync(batch).AsTask().GetAwaiter().GetResult();
        }
        catch (ChannelClosedException)
        {
            NetHostLog.Error(
                $"connectionId={ConnectionId} endpoint={_endpoint} rejected {batch.Frames.Length} ordered frames: outbound batch queue is closed");
        }
    }

    /// <summary>
    /// Replaceable move fanout: keep the latest MoveState / ChangeMoveType per entity instead of counting a drop.
    /// The pump writes stashed frames with the next batch so a coalesced-only stream still reaches the socket.
    /// </summary>
    bool TryCoalescedSend(ushort opcode, PacketEnvelope envelope)
    {
        if (!OutboundOrdering.IsReplaceable(opcode))
            return false;

        if (!TryCoalesceEntityKey(envelope.Payload, out ulong entityValue, out uint generation))
            return false;

        var key = new CoalesceReplaceableKey(opcode, entityValue, generation);
        _coalescedReplaceable[key] = envelope;
        if (_coalescedReplaceable.Count > MaxCoalescedReplaceableEntries)
            FlushCoalescedBeforeBarrier(wait: false);
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

    void FlushCoalescedBeforeBarrier(bool wait)
    {
        var frames = new List<PacketEnvelope>();
        TakeCoalesced(frames);
        if (frames.Count == 0)
            return;

        var batch = new OutboundBatch(frames.ToArray());
        if (wait)
        {
            try
            {
                _batches.Writer.WriteAsync(batch).AsTask().GetAwaiter().GetResult();
            }
            catch (ChannelClosedException)
            {
                NetHostLog.Error(
                    $"connectionId={ConnectionId} endpoint={_endpoint} lost {frames.Count} coalesced frames before a barrier: outbound batch queue is closed");
            }

            return;
        }

        if (_batches.Writer.TryWrite(batch))
            return;

        RestoreCoalesced(frames);
        NetHostLog.Error(
            $"connectionId={ConnectionId} endpoint={_endpoint} kept {frames.Count} coalesced frames: outbound batch queue is full, so they were not moved ahead of the barrier");
    }

    void RestoreCoalesced(List<PacketEnvelope> frames)
    {
        foreach (PacketEnvelope envelope in frames)
        {
            if (!TryCoalesceEntityKey(envelope.Payload, out ulong entityValue, out uint generation))
                continue;
            var key = new CoalesceReplaceableKey(envelope.Opcode, entityValue, generation);
            _coalescedReplaceable.TryAdd(key, envelope);
        }
    }

    void TakeCoalesced(List<PacketEnvelope> destination)
    {
        if (_coalescedReplaceable.IsEmpty)
            return;

        foreach (KeyValuePair<CoalesceReplaceableKey, PacketEnvelope> entry in _coalescedReplaceable.ToArray())
        {
            if (!_coalescedReplaceable.TryRemove(entry.Key, out PacketEnvelope? envelope))
                continue;
            if (_coalescedReplaceable.TryGetValue(entry.Key, out PacketEnvelope? newer))
            {
                envelope = newer;
                _coalescedReplaceable.TryRemove(entry.Key, out _);
            }

            destination.Add(envelope);
        }
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
            NetHostLog.Error(
                $"connectionId={ConnectionId} endpoint={_endpoint} rejected opcode={opcode}: payload length {body.Length} exceeds {PacketEnvelope.MaxPayloadBytes}");
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
        var combined = new List<PacketEnvelope>(MaxFramesPerBatch);
        try
        {
            while (true)
            {
                if (_batches.Reader.Completion.IsCompleted)
                {
                    combined.Clear();
                    DrainBatches(combined);
                    TakeCoalesced(combined);
                    if (combined.Count > 0)
                        await WriteCombinedAsync(combined).ConfigureAwait(false);
                    break;
                }

                Task batchWait = _batches.Reader.WaitToReadAsync().AsTask();
                Task wakeWait = _pumpWake.Reader.WaitToReadAsync().AsTask();
                await Task.WhenAny(batchWait, wakeWait).ConfigureAwait(false);

                combined.Clear();
                DrainBatches(combined);
                while (_pumpWake.Reader.TryRead(out _))
                {
                }

                TakeCoalesced(combined);
                if (combined.Count > 0)
                    await WriteCombinedAsync(combined).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException or InvalidOperationException)
        {
            if (ex is IOException or SocketException)
                NetDisconnectDiagnostics.Log("pump", ConnectionId, _endpoint, ex, _outbound.Reader.Count, StripeBatchDepth());
        }
        finally
        {
            try
            {
                combined.Clear();
                TakeCoalesced(combined);
                if (combined.Count > 0)
                    await WriteCombinedAsync(combined).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
            {
            }

            _batches.Writer.TryComplete();
        }
    }

    void DrainBatches(List<PacketEnvelope> combined)
    {
        while (_batches.Reader.TryRead(out OutboundBatch batch))
        {
            PacketEnvelope[] frames = batch.Frames;
            for (int i = 0; i < frames.Length; i++)
            {
                if (frames[i] != null)
                    combined.Add(frames[i]);
            }
        }
    }

    async Task WriteCombinedAsync(List<PacketEnvelope> frames)
    {
        if (frames.Count == 0)
            return;

        // The producer already splits at MaxFrames/MaxBytes. A drain can still hold several of those batches;
        // write them as one socket buffer when they fit, otherwise in bound-sized pieces.
        int offset = 0;
        while (offset < frames.Count)
        {
            int count = 0;
            int bytes = 0;
            while (offset + count < frames.Count)
            {
                int size = PacketEnvelope.HeaderSize + frames[offset + count].Payload.Length;
                if (count > 0 && (count >= MaxFramesPerBatch || bytes + size > MaxBytesPerBatch))
                    break;
                bytes += size;
                count++;
            }

            var slice = new PacketEnvelope[count];
            for (int i = 0; i < count; i++)
                slice[i] = frames[offset + i];

            long started = Stopwatch.GetTimestamp();
            await NetFraming.WriteManyAsync(_stream, slice, CancellationToken.None).ConfigureAwait(false);
            RecordSocketWrite(started, bytes, count);
            for (int i = 0; i < count; i++)
                NetTelemetry.FrameOut(_endpoint, slice[i].Payload.Length);
            offset += count;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0)
            return;

        Volatile.Write(ref _boundGeneration, 0);
        Volatile.Write(ref _boundObjectId, 0);
        // Queue any stashed presence before completing the channel so the pump can still write it.
        FlushCoalescedBeforeBarrier(wait: false);
        _pumpWake.Writer.TryWrite(1);
        _batches.Writer.TryComplete();
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
