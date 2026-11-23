using System.Diagnostics;
using System.Net.Sockets;
using System.Threading.Channels;
using Fineage.Net.Contract;
using Fineage.Protocol.Telemetry;

namespace Fineage.Net.Host;

/// <summary>
/// One accepted Net socket.
/// Inbound envelopes land in a bounded <see cref="Inbox"/>. Outbound envelopes share one mailbox:
/// a single wake, replaceable frames collapsed until an ordering barrier, and a queue bounded by
/// frame count and bytes. The pump takes one snapshot and writes it with one <c>WriteManyAsync</c>.
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
    readonly Channel<byte> _pumpWake;
    readonly OutboundMailbox _mailbox;
    readonly CommandDeduper _deduper = new();
    readonly string _endpoint;
    readonly Task _pump;
    int _closed;
    int _boundObjectId;
    uint _boundGeneration;

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
        _pumpWake = Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });
        _mailbox = new OutboundMailbox(
            Math.Max(8, outboundCapacity),
            MaxBytesPerBatch,
            ConnectionId,
            endpoint,
            WakePump);
        _pump = PumpAsync();
    }

    void WakePump() => _pumpWake.Writer.TryWrite(1);

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
    /// Queues <paramref name="payload"/> and waits up to 2s for room. Used by request/reply paths that must not lose a frame.
    /// </summary>
    public Task SendAsync(ushort opcode, byte[] payload, uint requestSequence = 0, CancellationToken ct = default)
    {
        if (ct.IsCancellationRequested)
            return Task.FromCanceled(ct);

        if (!TryEnvelope(opcode, payload, requestSequence, out PacketEnvelope envelope))
            return Task.CompletedTask;

        if (!_mailbox.TryEnqueue(envelope, waitMilliseconds: 2000))
        {
            NetTelemetry.OutboundDropped(_endpoint);
            return Task.CompletedTask;
        }

        WakePump();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Non-blocking enqueue. A full mailbox drops a reliable frame, counts it, and logs the opcode.
    /// Replaceable presence frames collapse until an ordering barrier. Stripe delivery does not use this path.
    /// </summary>
    public bool TrySend(ushort opcode, byte[] payload, uint requestSequence = 0)
    {
        if (!TryEnvelope(opcode, payload, requestSequence, out PacketEnvelope envelope))
            return false;

        if (!_mailbox.TryEnqueue(envelope, waitMilliseconds: 0))
        {
            NetTelemetry.OutboundDropped(_endpoint);
            return false;
        }

        WakePump();
        return true;
    }

    /// <summary>
    /// Stripe path: queue every frame in order and wait for room. Reliable frames are not dropped.
    /// One wake covers the whole batch. A closed connection logs and returns.
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
            if (!TryEnvelope(frames[i].Opcode, frames[i].Payload, 0, out PacketEnvelope envelope))
                continue;

            if (!_mailbox.TryEnqueue(envelope, waitMilliseconds: Timeout.Infinite))
            {
                NetHostLog.Error(
                    $"connectionId={ConnectionId} endpoint={_endpoint} rejected ordered opcode={frames[i].Opcode} at frame {i} of {frames.Count}: outbound mailbox did not accept it");
                return;
            }
        }

        WakePump();
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
                if (Volatile.Read(ref _closed) != 0)
                {
                    combined.Clear();
                    _mailbox.Take(combined);
                    if (combined.Count > 0)
                        await WriteCombinedAsync(combined).ConfigureAwait(false);
                    break;
                }

                await _pumpWake.Reader.WaitToReadAsync().ConfigureAwait(false);
                while (_pumpWake.Reader.TryRead(out _))
                {
                }

                combined.Clear();
                _mailbox.Take(combined);
                if (combined.Count > 0)
                    await WriteCombinedAsync(combined).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException or InvalidOperationException)
        {
            /* peer gone, or a frame that cannot be written: the accept loop tears the connection down */
        }
        finally
        {
            try
            {
                combined.Clear();
                _mailbox.Take(combined);
                if (combined.Count > 0)
                    await WriteCombinedAsync(combined).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
            {
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
        // Stop producers, then wake the pump so it writes whatever is still in the mailbox.
        _mailbox.Close();
        WakePump();
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
