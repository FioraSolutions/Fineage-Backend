using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Threading.Channels;
using Fineage.Net.Contract;

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
    readonly TcpClient _client;
    readonly NetworkStream _stream;
    readonly Channel<PacketEnvelope> _inbox;
    readonly Channel<PacketEnvelope> _outbound;
    readonly CommandDeduper _deduper = new();
    readonly string _endpoint;
    readonly Task _pump;
    readonly ConcurrentDictionary<CoalesceEntityKey, PacketEnvelope> _coalescedMove = new();
    int _closed;

    readonly record struct CoalesceEntityKey(ulong EntityValue, uint EntityGeneration);

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
            while (!ct.IsCancellationRequested)
            {
                PacketEnvelope? envelope;
                try
                {
                    envelope = await NetFraming.ReadAsync(_stream, ct).ConfigureAwait(false);
                }
                catch (InvalidOperationException)
                {
                    break;
                }

                if (envelope == null)
                    break;

                if (envelope.Major != PacketEnvelope.ProtocolMajor)
                    continue;

                if (envelope.Payload.Length > PacketEnvelope.MaxPayloadBytes)
                    continue;

                if (!_deduper.TryAccept(envelope.RequestSequence))
                    continue;

                NetTelemetry.FrameIn(_endpoint, envelope.Payload.Length);

                await _inbox.Writer.WriteAsync(envelope, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            _inbox.Writer.TryComplete();
        }
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
    public bool TrySend(ushort opcode, byte[] payload, uint requestSequence = 0)
    {
        if (!TryEnvelope(opcode, payload, requestSequence, out PacketEnvelope envelope))
            return false;

        if (TryCoalescedSend(opcode, envelope))
            return true;

        if (_outbound.Writer.TryWrite(envelope))
            return true;

        NetTelemetry.OutboundDropped(_endpoint);
        return false;
    }

    /// <summary>
    /// Replaceable move fanout: when the outbound channel is full, keep the latest MoveState /
    /// ChangeMoveType per entity instead of counting a drop. The pump writes stashed frames
    /// straight to the socket after each channel frame so concurrent <see cref="TrySend"/> fillers
    /// cannot starve coalesced moves by re-filling the channel (CI flake on loaded hosts).
    /// </summary>
    bool TryCoalescedSend(ushort opcode, PacketEnvelope envelope)
    {
        if (opcode != NetOpcode.MoveState && opcode != NetOpcode.ChangeMoveType)
            return false;

        if (!TryCoalesceEntityKey(envelope.Payload, out ulong entityValue, out uint generation))
            return false;

        var key = new CoalesceEntityKey(entityValue, generation);

        if (_outbound.Writer.TryWrite(envelope))
        {
            _coalescedMove.TryRemove(key, out _);
            return true;
        }

        _coalescedMove[key] = envelope;
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
        foreach (KeyValuePair<CoalesceEntityKey, PacketEnvelope> entry in _coalescedMove.ToArray())
        {
            if (!_coalescedMove.TryGetValue(entry.Key, out PacketEnvelope? envelope))
                continue;
            if (!_outbound.Writer.TryWrite(envelope))
                break;
            _coalescedMove.TryRemove(entry.Key, out _);
        }
    }

    /// <summary>
    /// Pump-only: write every stashed MoveState / ChangeMoveType directly to the socket.
    /// Bypasses the bounded channel so fanout fillers cannot block the latest position forever.
    /// </summary>
    async Task WriteCoalescedToStreamAsync()
    {
        foreach (KeyValuePair<CoalesceEntityKey, PacketEnvelope> entry in _coalescedMove.ToArray())
        {
            if (!_coalescedMove.TryRemove(entry.Key, out PacketEnvelope? envelope))
                continue;
            // A newer frame may have landed for the same entity after ToArray; prefer the latest.
            if (_coalescedMove.TryGetValue(entry.Key, out PacketEnvelope? newer))
            {
                envelope = newer;
                _coalescedMove.TryRemove(entry.Key, out _);
            }
            await NetFraming.WriteAsync(_stream, envelope, CancellationToken.None).ConfigureAwait(false);
            NetTelemetry.FrameOut(_endpoint, envelope.Payload.Length);
        }
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
            await foreach (PacketEnvelope envelope in _outbound.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                await NetFraming.WriteAsync(_stream, envelope, CancellationToken.None).ConfigureAwait(false);
                NetTelemetry.FrameOut(_endpoint, envelope.Payload.Length);
                await WriteCoalescedToStreamAsync().ConfigureAwait(false);
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
