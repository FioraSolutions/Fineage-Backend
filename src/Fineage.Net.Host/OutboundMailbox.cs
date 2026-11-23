using System.Buffers.Binary;
using Fineage.Net.Contract;
using Fineage.Protocol.Telemetry;

namespace Fineage.Net.Host;

enum MailboxAdmit
{
    Accepted,
    Dropped,
    Rejected,
    Closed
}

readonly record struct MailboxTaken(int Frames, int Bytes);

/// <summary>
/// One ordered mailbox per connection. Producers never wait: movement coalesces, a knownlist snapshot
/// replaces the pending one when the budget cannot hold both, visuals drop when stale, and a reliable
/// frame that does not fit rejects so the connection can resync. The pump is the only reader.
/// The budget is bytes and frames together. The frame cap is at least enough minimum-sized frames to
/// fill the byte budget, so a burst of small frames cannot report "full" while the byte budget is idle.
/// </summary>
sealed class OutboundMailbox
{
    readonly object _gate = new();
    readonly List<Segment> _ordered = new();
    readonly List<CoalesceKey> _openOrder = new();
    readonly Dictionary<CoalesceKey, PacketEnvelope> _open = new();
    readonly int _maxFrames;
    readonly int _maxBytes;
    readonly string _endpoint;
    readonly ulong _connectionId;
    Segment? _snapshot;
    int _orderedBytes;
    int _orderedFrames;
    int _openBytes;
    // Frames Take already handed to the pump but the socket write has not finished.
    // They still occupy the budget: the mailbox looks empty while a slow write is in progress,
    // and the next reliable batch must not be treated as the one empty-queue burst.
    int _inflightFrames;
    int _inflightBytes;
    bool _closed;
    bool _loggedClosed;

    const int MaxOpenEntries = 256;

    readonly record struct CoalesceKey(ushort Opcode, ulong EntityValue, uint EntityGeneration);

    sealed class Segment
    {
        public bool Snapshot { get; init; }
        public List<PacketEnvelope> Frames { get; } = new();
        public int Bytes { get; set; }
    }

    public OutboundMailbox(int maxFrames, int maxBytes, ulong connectionId, string endpoint)
    {
        _maxFrames = maxFrames;
        _maxBytes = maxBytes;
        _connectionId = connectionId;
        _endpoint = endpoint;
    }

    public int QueuedFrames
    {
        get
        {
            lock (_gate)
                return _orderedFrames + _open.Count;
        }
    }

    public void Close()
    {
        lock (_gate)
        {
            _closed = true;
            _inflightFrames = 0;
            _inflightBytes = 0;
        }
    }

    public MailboxAdmit TryAppend(PacketEnvelope envelope) => TryAdmitBatch([envelope], forceWholeBatch: false);

    /// <summary>
    /// Admits <paramref name="envelopes"/> without waiting. One burst may exceed the budget when this
    /// connection is holding nothing (the pump splits the socket write), or when
    /// <paramref name="forceWholeBatch"/> is set. Bytes already handed to the pump still count, so a
    /// slow socket write does not make the mailbox look empty. A later batch that does not fit rejects
    /// on a reliable frame.
    /// </summary>
    public MailboxAdmit TryAdmitBatch(IReadOnlyList<PacketEnvelope> envelopes, bool forceWholeBatch)
    {
        lock (_gate)
        {
            if (_closed)
                return ClosedOnce();

            bool force = forceWholeBatch || (_orderedFrames == 0 && _inflightFrames == 0);
            foreach (PacketEnvelope envelope in envelopes)
            {
                MailboxAdmit admit = AdmitOne(envelope, force);
                if (admit == MailboxAdmit.Rejected)
                {
                    RecordDepth();
                    return MailboxAdmit.Rejected;
                }
            }

            RecordDepth();
            return MailboxAdmit.Accepted;
        }
    }

    /// <summary>
    /// Knownlist / appearance burst. Appends to the pending snapshot while the budget allows.
    /// When it does not, the pending snapshot is replaced if the new burst fits without it.
    /// </summary>
    public MailboxAdmit TryMergeSnapshot(IReadOnlyList<PacketEnvelope> frames)
    {
        lock (_gate)
        {
            if (_closed)
                return ClosedOnce();
            if (frames.Count == 0)
                return MailboxAdmit.Accepted;

            if (_snapshot != null)
                FoldOpenIntoSnapshot();
            else if (!SealOpen())
            {
                OutboundLaneMetrics.Full(OutboundLane.Snapshot);
                NetHostLog.Error(
                    $"connectionId={_connectionId} endpoint={_endpoint} dropped knownlist snapshot of {frames.Count} frames: coalesced movement could not be sealed ahead of it (frames={_orderedFrames}/{_maxFrames} bytes={_orderedBytes}/{_maxBytes})");
                RecordDepth();
                return MailboxAdmit.Dropped;
            }

            int addBytes = 0;
            for (int i = 0; i < frames.Count; i++)
                addBytes += FrameBytes(frames[i]);
            int addFrames = frames.Count;

            if (_snapshot != null)
            {
                if (RoomFor(addFrames, addBytes))
                {
                    AppendSnapshot(_snapshot, frames, addBytes);
                    RecordDepth();
                    return MailboxAdmit.Accepted;
                }

                int otherFrames = _orderedFrames - _snapshot.Frames.Count;
                int otherBytes = _orderedBytes - _snapshot.Bytes;
                bool fitsWithoutPending = otherFrames == 0
                    || (otherFrames + addFrames <= _maxFrames && otherBytes + addBytes <= _maxBytes);
                if (!fitsWithoutPending)
                {
                    OutboundLaneMetrics.Full(OutboundLane.Snapshot, addFrames);
                    NetHostLog.Error(
                        $"connectionId={_connectionId} endpoint={_endpoint} dropped knownlist snapshot of {addFrames} frames / {addBytes} bytes: pending snapshot is {_snapshot.Frames.Count} frames / {_snapshot.Bytes} bytes and the new burst does not fit beside other queued data (frames={_orderedFrames}/{_maxFrames} bytes={_orderedBytes}/{_maxBytes})");
                    RecordDepth();
                    return MailboxAdmit.Dropped;
                }

                int oldFrames = _snapshot.Frames.Count;
                int oldBytes = _snapshot.Bytes;
                _orderedFrames -= oldFrames;
                _orderedBytes -= oldBytes;
                _snapshot.Frames.Clear();
                _snapshot.Bytes = 0;
                AppendSnapshot(_snapshot, frames, addBytes);
                OutboundLaneMetrics.Evicted(OutboundLane.Snapshot, oldFrames);
                NetHostLog.Error(
                    $"connectionId={_connectionId} endpoint={_endpoint} replaced pending knownlist snapshot ({oldFrames} frames / {oldBytes} bytes) with {addFrames} frames / {addBytes} bytes: outbound budget is {_maxFrames} frames / {_maxBytes} bytes");
                RecordDepth();
                return MailboxAdmit.Accepted;
            }

            if (!RoomFor(addFrames, addBytes))
            {
                OutboundLaneMetrics.Full(OutboundLane.Snapshot, addFrames);
                NetHostLog.Error(
                    $"connectionId={_connectionId} endpoint={_endpoint} dropped knownlist snapshot of {addFrames} frames / {addBytes} bytes: outbound mailbox is full (frames={_orderedFrames}/{_maxFrames} bytes={_orderedBytes}/{_maxBytes})");
                RecordDepth();
                return MailboxAdmit.Dropped;
            }

            var created = new Segment { Snapshot = true };
            AppendSnapshot(created, frames, addBytes);
            _ordered.Add(created);
            _snapshot = created;
            RecordDepth();
            return MailboxAdmit.Accepted;
        }
    }

    /// <summary>
    /// Moves queued frames to <paramref name="destination"/> and counts them as in flight until
    /// <see cref="ReleaseInflight"/> (the socket write finished).
    /// </summary>
    public MailboxTaken Take(List<PacketEnvelope> destination)
    {
        lock (_gate)
        {
            int count = 0;
            int bytes = _orderedBytes + _openBytes;
            for (int s = 0; s < _ordered.Count; s++)
            {
                List<PacketEnvelope> frames = _ordered[s].Frames;
                for (int i = 0; i < frames.Count; i++)
                {
                    destination.Add(frames[i]);
                    count++;
                }
            }

            _ordered.Clear();
            _orderedBytes = 0;
            _orderedFrames = 0;
            _snapshot = null;

            for (int i = 0; i < _openOrder.Count; i++)
                destination.Add(_open[_openOrder[i]]);
            count += _open.Count;

            _open.Clear();
            _openOrder.Clear();
            _openBytes = 0;
            _inflightFrames += count;
            _inflightBytes += bytes;
            RecordDepth();
            return new MailboxTaken(count, bytes);
        }
    }

    /// <summary>The pump finished writing frames previously returned by <see cref="Take"/>.</summary>
    public void ReleaseInflight(int frames, int bytes)
    {
        lock (_gate)
        {
            _inflightFrames = Math.Max(0, _inflightFrames - frames);
            _inflightBytes = Math.Max(0, _inflightBytes - bytes);
        }
    }

    MailboxAdmit AdmitOne(PacketEnvelope envelope, bool force)
    {
        switch (OutboundLanePolicy.Classify(envelope.Opcode))
        {
            case OutboundLane.Movement:
                Coalesce(envelope);
                return MailboxAdmit.Accepted;
            case OutboundLane.Visual:
                return AppendVisual(envelope, force);
            case OutboundLane.Snapshot:
                return MergeOne(envelope);
            default:
                if (OutboundOrdering.IsBarrier(envelope.Opcode) && !SealOpen())
                {
                    OutboundLaneMetrics.Full(OutboundLane.Reliable);
                    NetHostLog.Error(
                        $"connectionId={_connectionId} endpoint={_endpoint} rejected reliable opcode={envelope.Opcode}: coalesced movement could not be sealed ahead of it (frames={_orderedFrames}/{_maxFrames} bytes={_orderedBytes}/{_maxBytes})");
                    return MailboxAdmit.Rejected;
                }

                if (!force && !RoomFor(1, FrameBytes(envelope)))
                {
                    OutboundLaneMetrics.Full(OutboundLane.Reliable);
                    NetHostLog.Error(
                        $"connectionId={_connectionId} endpoint={_endpoint} rejected reliable opcode={envelope.Opcode}: outbound mailbox is full (frames={_orderedFrames}/{_maxFrames} bytes={_orderedBytes}/{_maxBytes})");
                    return MailboxAdmit.Rejected;
                }

                AppendOrdered(envelope);
                return MailboxAdmit.Accepted;
        }
    }

    MailboxAdmit MergeOne(PacketEnvelope envelope)
    {
        // Called with the gate held. Re-enter the snapshot rules for a single appearance frame.
        if (_snapshot != null)
            FoldOpenIntoSnapshot();
        else if (!SealOpen())
        {
            OutboundLaneMetrics.Full(OutboundLane.Snapshot);
            NetHostLog.Error(
                $"connectionId={_connectionId} endpoint={_endpoint} dropped snapshot opcode={envelope.Opcode}: coalesced movement could not be sealed ahead of it (frames={_orderedFrames}/{_maxFrames} bytes={_orderedBytes}/{_maxBytes})");
            return MailboxAdmit.Dropped;
        }

        int size = FrameBytes(envelope);
        if (_snapshot != null)
        {
            if (RoomFor(1, size))
            {
                _snapshot.Frames.Add(envelope);
                _snapshot.Bytes += size;
                _orderedFrames++;
                _orderedBytes += size;
                return MailboxAdmit.Accepted;
            }

            int otherFrames = _orderedFrames - _snapshot.Frames.Count;
            int otherBytes = _orderedBytes - _snapshot.Bytes;
            bool fitsWithoutPending = otherFrames == 0
                || (otherFrames + 1 <= _maxFrames && otherBytes + size <= _maxBytes);
            if (!fitsWithoutPending)
            {
                OutboundLaneMetrics.Full(OutboundLane.Snapshot);
                NetHostLog.Error(
                    $"connectionId={_connectionId} endpoint={_endpoint} dropped snapshot opcode={envelope.Opcode} ({size} bytes): pending snapshot is {_snapshot.Frames.Count} frames / {_snapshot.Bytes} bytes (frames={_orderedFrames}/{_maxFrames} bytes={_orderedBytes}/{_maxBytes})");
                return MailboxAdmit.Dropped;
            }

            int oldFrames = _snapshot.Frames.Count;
            int oldBytes = _snapshot.Bytes;
            _orderedFrames -= oldFrames;
            _orderedBytes -= oldBytes;
            _snapshot.Frames.Clear();
            _snapshot.Bytes = 0;
            _snapshot.Frames.Add(envelope);
            _snapshot.Bytes = size;
            _orderedFrames++;
            _orderedBytes += size;
            OutboundLaneMetrics.Evicted(OutboundLane.Snapshot, oldFrames);
            NetHostLog.Error(
                $"connectionId={_connectionId} endpoint={_endpoint} replaced pending knownlist snapshot ({oldFrames} frames / {oldBytes} bytes) with opcode={envelope.Opcode} ({size} bytes): outbound budget is {_maxFrames} frames / {_maxBytes} bytes");
            return MailboxAdmit.Accepted;
        }

        if (!RoomFor(1, size))
        {
            OutboundLaneMetrics.Full(OutboundLane.Snapshot);
            NetHostLog.Error(
                $"connectionId={_connectionId} endpoint={_endpoint} dropped snapshot opcode={envelope.Opcode}: outbound mailbox is full (frames={_orderedFrames}/{_maxFrames} bytes={_orderedBytes}/{_maxBytes})");
            return MailboxAdmit.Dropped;
        }

        var created = new Segment { Snapshot = true };
        created.Frames.Add(envelope);
        created.Bytes = size;
        _ordered.Add(created);
        _snapshot = created;
        _orderedFrames++;
        _orderedBytes += size;
        return MailboxAdmit.Accepted;
    }

    void Coalesce(PacketEnvelope envelope)
    {
        if (!TryEntityKey(envelope.Opcode, envelope.Payload, out CoalesceKey key))
        {
            if (!RoomFor(1, FrameBytes(envelope)))
            {
                OutboundLaneMetrics.Full(OutboundLane.Movement);
                NetHostLog.Error(
                    $"connectionId={_connectionId} endpoint={_endpoint} dropped movement opcode={envelope.Opcode}: payload has no entity key and the mailbox is full (frames={_orderedFrames}/{_maxFrames} bytes={_orderedBytes}/{_maxBytes})");
                return;
            }

            AppendOrdered(envelope);
            return;
        }

        int size = FrameBytes(envelope);
        if (_open.TryGetValue(key, out PacketEnvelope? previous))
        {
            _openBytes -= FrameBytes(previous);
            _open[key] = envelope;
            _openBytes += size;
            OutboundLaneMetrics.Coalesced(OutboundLane.Movement);
            return;
        }

        while (_open.Count > 0 && !OpenHasRoom(size))
        {
            CoalesceKey oldest = _openOrder[0];
            PacketEnvelope evicted = _open[oldest];
            _openBytes -= FrameBytes(evicted);
            _open.Remove(oldest);
            _openOrder.RemoveAt(0);
            OutboundLaneMetrics.Evicted(OutboundLane.Movement);
            OutboundLaneMetrics.LogLimited(
                $"connectionId={_connectionId} endpoint={_endpoint} evicted replaceable opcode={oldest.Opcode} entity={oldest.EntityValue}: movement lane is {MaxOpenEntries} entries / {_maxBytes} bytes");
        }

        _open[key] = envelope;
        _openOrder.Add(key);
        _openBytes += size;
    }

    MailboxAdmit AppendVisual(PacketEnvelope envelope, bool force)
    {
        int size = FrameBytes(envelope);
        if (!force && !RoomFor(1, size))
        {
            if (DropOldestVisual() && RoomFor(1, size))
            {
                OutboundLaneMetrics.Evicted(OutboundLane.Visual);
            }
            else
            {
                OutboundLaneMetrics.Full(OutboundLane.Visual);
                OutboundLaneMetrics.Evicted(OutboundLane.Visual);
                OutboundLaneMetrics.LogLimited(
                    $"connectionId={_connectionId} endpoint={_endpoint} dropped stale visual opcode={envelope.Opcode}: outbound mailbox is full (frames={_orderedFrames}/{_maxFrames} bytes={_orderedBytes}/{_maxBytes})");
                return MailboxAdmit.Dropped;
            }
        }

        AppendOrdered(envelope);
        return MailboxAdmit.Accepted;
    }

    bool DropOldestVisual()
    {
        for (int s = 0; s < _ordered.Count; s++)
        {
            Segment segment = _ordered[s];
            if (segment.Snapshot || segment.Frames.Count != 1)
                continue;
            if (OutboundLanePolicy.Classify(segment.Frames[0].Opcode) != OutboundLane.Visual)
                continue;
            _orderedFrames -= segment.Frames.Count;
            _orderedBytes -= segment.Bytes;
            _ordered.RemoveAt(s);
            return true;
        }

        return false;
    }

    void FoldOpenIntoSnapshot()
    {
        if (_snapshot == null || _openOrder.Count == 0)
            return;

        for (int i = 0; i < _openOrder.Count; i++)
        {
            PacketEnvelope envelope = _open[_openOrder[i]];
            int size = FrameBytes(envelope);
            _snapshot.Frames.Add(envelope);
            _snapshot.Bytes += size;
            _orderedFrames++;
            _orderedBytes += size;
        }

        _open.Clear();
        _openOrder.Clear();
        _openBytes = 0;
    }

    bool SealOpen()
    {
        if (_openOrder.Count == 0)
            return true;

        int bytes = 0;
        var frames = new List<PacketEnvelope>(_openOrder.Count);
        for (int i = 0; i < _openOrder.Count; i++)
        {
            PacketEnvelope envelope = _open[_openOrder[i]];
            frames.Add(envelope);
            bytes += FrameBytes(envelope);
        }

        if (!RoomFor(frames.Count, bytes))
            return false;

        var segment = new Segment();
        segment.Frames.AddRange(frames);
        segment.Bytes = bytes;
        _ordered.Add(segment);
        _orderedFrames += frames.Count;
        _orderedBytes += bytes;
        _open.Clear();
        _openOrder.Clear();
        _openBytes = 0;
        return true;
    }

    void AppendOrdered(PacketEnvelope envelope)
    {
        int size = FrameBytes(envelope);
        var segment = new Segment();
        segment.Frames.Add(envelope);
        segment.Bytes = size;
        _ordered.Add(segment);
        _orderedFrames++;
        _orderedBytes += size;
    }

    void AppendSnapshot(Segment snapshot, IReadOnlyList<PacketEnvelope> frames, int addBytes)
    {
        for (int i = 0; i < frames.Count; i++)
            snapshot.Frames.Add(frames[i]);
        snapshot.Bytes += addBytes;
        _orderedFrames += frames.Count;
        _orderedBytes += addBytes;
    }

    bool RoomFor(int frames, int bytes)
    {
        if (frames <= 0)
            return true;
        // Nothing queued and nothing already in a socket write: one burst may exceed the cap.
        // The pump splits it. In-flight bytes still count, or a blocked write looks like an empty mailbox
        // and every later batch is admitted as that one burst.
        if (_orderedFrames == 0 && _inflightFrames == 0)
            return true;
        return _orderedFrames + _inflightFrames + frames <= _maxFrames
            && _orderedBytes + _inflightBytes + bytes <= _maxBytes;
    }

    bool OpenHasRoom(int size)
    {
        if (_open.Count == 0)
            return true;
        if (_open.Count >= MaxOpenEntries)
            return false;
        return _openBytes + size <= _maxBytes;
    }

    MailboxAdmit ClosedOnce()
    {
        if (!_loggedClosed)
        {
            _loggedClosed = true;
            NetHostLog.Error(
                $"connectionId={_connectionId} endpoint={_endpoint} rejected outbound frame: mailbox is closed");
        }

        return MailboxAdmit.Closed;
    }

    void RecordDepth()
    {
        var endpoint = new KeyValuePair<string, object?>("endpoint", _endpoint);
        FineageTelemetry.OutboundQueueFrames.Record(_orderedFrames + _open.Count, endpoint);
        FineageTelemetry.OutboundQueueBytes.Record(_orderedBytes + _openBytes, endpoint);
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
