using System.Buffers.Binary;
using Fineage.Net.Contract;
using Fineage.Protocol.Telemetry;

namespace Fineage.Net.Host;

/// <summary>
/// One ordered mailbox per connection (issue #867 PR4).
/// Replaceable presence frames for the same entity collapse in place until an ordering barrier
/// (spawn, despawn, death, inventory, drop, teleport). The pump is the only reader: it takes
/// one snapshot and writes it. Producers share a single wake. A full queue signals that wake
/// before waiting, because the pump is the only reader.
/// The ordered queue is bounded by frame count and bytes. A full queue waits on the stripe
/// path and fails the non-blocking path. Replaceable frames sit in their own bound so a full
/// reliable queue does not drop the latest move.
/// </summary>
sealed class OutboundMailbox
{
    readonly object _gate = new();
    readonly Queue<PacketEnvelope> _ordered = new();
    readonly List<CoalesceKey> _openOrder = new();
    readonly Dictionary<CoalesceKey, PacketEnvelope> _open = new();
    readonly int _maxFrames;
    readonly int _maxBytes;
    readonly string _endpoint;
    readonly ulong _connectionId;
    readonly Action _wake;
    int _orderedBytes;
    int _openBytes;
    bool _closed;

    const int MaxOpenEntries = 256;

    readonly record struct CoalesceKey(ushort Opcode, ulong EntityValue, uint EntityGeneration);

    public OutboundMailbox(int maxFrames, int maxBytes, ulong connectionId, string endpoint, Action wake)
    {
        _maxFrames = maxFrames;
        _maxBytes = maxBytes;
        _connectionId = connectionId;
        _endpoint = endpoint;
        _wake = wake;
    }

    /// <summary>
    /// Queue <paramref name="envelope"/>. <paramref name="waitMilliseconds"/> &lt; 0 waits until the
    /// pump makes room or the mailbox closes. Zero does not wait.
    /// </summary>
    public bool TryEnqueue(PacketEnvelope envelope, int waitMilliseconds)
    {
        lock (_gate)
        {
            if (_closed)
            {
                NetHostLog.Error(
                    $"connectionId={_connectionId} endpoint={_endpoint} rejected opcode={envelope.Opcode}: outbound mailbox is closed");
                return false;
            }

            if (OutboundOrdering.IsReplaceable(envelope.Opcode)
                && TryEntityKey(envelope.Opcode, envelope.Payload, out CoalesceKey key))
            {
                EnqueueReplaceable(key, envelope);
                RecordDepth();
                return true;
            }

            if (OutboundOrdering.IsBarrier(envelope.Opcode) && !SealOpen(waitMilliseconds))
            {
                NetHostLog.Error(
                    $"connectionId={_connectionId} endpoint={_endpoint} kept {_open.Count} coalesced frames: outbound mailbox is full (frames={_ordered.Count}/{_maxFrames} bytes={_orderedBytes}/{_maxBytes}), so barrier opcode={envelope.Opcode} was not queued ahead of them");
                return false;
            }

            if (AppendOrdered(envelope, waitMilliseconds))
            {
                RecordDepth();
                return true;
            }

            NetHostLog.Error(
                $"connectionId={_connectionId} endpoint={_endpoint} dropped reliable opcode={envelope.Opcode}: outbound mailbox is full (frames={_ordered.Count}/{_maxFrames} bytes={_orderedBytes}/{_maxBytes})");
            return false;
        }
    }

    /// <summary>Moves the current snapshot to <paramref name="destination"/> and wakes any producer waiting for room.</summary>
    public void Take(List<PacketEnvelope> destination)
    {
        lock (_gate)
        {
            while (_ordered.Count > 0)
                destination.Add(_ordered.Dequeue());
            _orderedBytes = 0;

            for (int i = 0; i < _openOrder.Count; i++)
                destination.Add(_open[_openOrder[i]]);
            _open.Clear();
            _openOrder.Clear();
            _openBytes = 0;
            RecordDepth();
            Monitor.PulseAll(_gate);
        }
    }

    /// <summary>Ordered frames plus open replaceable frames. The disconnect log reads this from the read loop and the pump.</summary>
    public int QueuedFrames
    {
        get
        {
            lock (_gate)
                return _ordered.Count + _open.Count;
        }
    }

    public void Close()
    {
        lock (_gate)
        {
            _closed = true;
            Monitor.PulseAll(_gate);
        }
    }

    void RecordDepth()
    {
        var endpoint = new KeyValuePair<string, object?>("endpoint", _endpoint);
        FineageTelemetry.OutboundQueueFrames.Record(_ordered.Count + _open.Count, endpoint);
        FineageTelemetry.OutboundQueueBytes.Record(_orderedBytes + _openBytes, endpoint);
    }

    void EnqueueReplaceable(CoalesceKey key, PacketEnvelope envelope)
    {
        int size = FrameBytes(envelope);
        if (_open.TryGetValue(key, out PacketEnvelope? previous))
        {
            _openBytes -= FrameBytes(previous);
            _open[key] = envelope;
            _openBytes += size;
            return;
        }

        while (_open.Count > 0 && !OpenHasRoom(size))
        {
            CoalesceKey oldest = _openOrder[0];
            PacketEnvelope evicted = _open[oldest];
            _openBytes -= FrameBytes(evicted);
            _open.Remove(oldest);
            _openOrder.RemoveAt(0);
            NetHostLog.Error(
                $"connectionId={_connectionId} endpoint={_endpoint} superseded replaceable opcode={oldest.Opcode} entity={oldest.EntityValue}: open mailbox bound is {MaxOpenEntries} frames / {_maxBytes} bytes");
        }

        _open[key] = envelope;
        _openOrder.Add(key);
        _openBytes += size;
    }

    /// <summary>Called with <see cref="_gate"/> held. Moves every open frame onto the ordered queue, ahead of the barrier.</summary>
    bool SealOpen(int waitMilliseconds)
    {
        while (_openOrder.Count > 0)
        {
            CoalesceKey key = _openOrder[0];
            PacketEnvelope envelope = _open[key];
            int size = FrameBytes(envelope);
            if (!HasRoom(size))
            {
                if (_closed || !Wait(waitMilliseconds))
                    return false;
                continue;
            }

            _open.Remove(key);
            _openOrder.RemoveAt(0);
            _openBytes -= size;
            _ordered.Enqueue(envelope);
            _orderedBytes += size;
        }

        return true;
    }

    bool AppendOrdered(PacketEnvelope envelope, int waitMilliseconds)
    {
        int size = FrameBytes(envelope);
        while (!HasRoom(size))
        {
            if (_closed || !Wait(waitMilliseconds))
                return false;
        }

        _ordered.Enqueue(envelope);
        _orderedBytes += size;
        return true;
    }

    bool HasRoom(int size)
    {
        if (_ordered.Count == 0)
            return true;
        return _ordered.Count < _maxFrames && _orderedBytes + size <= _maxBytes;
    }

    bool OpenHasRoom(int size)
    {
        if (_open.Count == 0)
            return true;
        if (_open.Count >= MaxOpenEntries)
            return false;
        return _openBytes + size <= _maxBytes;
    }

    bool Wait(int waitMilliseconds)
    {
        if (waitMilliseconds == 0)
            return false;

        // The pump is the only reader. Wake it before releasing the lock so a full queue cannot stall.
        _wake();
        if (waitMilliseconds < 0)
            return Monitor.Wait(_gate);
        return Monitor.Wait(_gate, waitMilliseconds);
    }

    static int FrameBytes(PacketEnvelope envelope) => PacketEnvelope.HeaderSize + envelope.Payload.Length;

    static bool TryEntityKey(ushort opcode, byte[] payload, out CoalesceKey key)
    {
        key = default;
        if (payload.Length < 12)
            return false;

        ReadOnlySpan<byte> span = payload;
        ulong entity = BinaryPrimitives.ReadUInt64LittleEndian(span);
        uint generation = BinaryPrimitives.ReadUInt32LittleEndian(span[8..]);
        key = new CoalesceKey(opcode, entity, generation);
        return true;
    }
}
