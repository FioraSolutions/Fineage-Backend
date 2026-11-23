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
/// One ordered mailbox per connection, bounded in bytes. Producers never wait. The pump is the only reader.
/// <para>
/// Two byte limits. Above the <b>soft</b> limit low-priority frames give way: movement / status frames are
/// coalesced to the newest one per (opcode, entity) (a <c>StatusUpdate</c> merges its attributes into the pending
/// one, so no attribute is lost), and visual frames (attack / cast animations, social actions, target markers)
/// are shed oldest first, or the new visual is dropped. Reliable frames (inventory, item drop / pickup, death,
/// chat, system messages, knownlist spawn / despawn, teleports) are never dropped or replaced. When a reliable
/// or newest-per-entity frame would push the mailbox above the <b>hard</b> limit after visuals were shed, the
/// admission is <see cref="MailboxAdmit.Rejected"/> and the connection closes that slow client.
/// </para>
/// <para>
/// Bytes already handed to the pump but not yet written still count, so a blocked socket write does not make
/// the mailbox look empty. One caller batch may exceed both limits when the connection holds nothing at all
/// (an enter-world burst); the pump splits the socket writes.
/// </para>
/// </summary>
sealed class OutboundMailbox
{
    /// <summary>Default hard byte limit per connection (<c>FINEAGE_NET_OUTBOUND_HARD_BYTES</c>).</summary>
    public const int DefaultHardBytes = 1024 * 1024;

    readonly object _gate = new();
    readonly List<Queued> _ordered = new();
    readonly LinkedList<OpenEntry> _openOrder = new();
    readonly Dictionary<CoalesceKey, LinkedListNode<OpenEntry>> _open = new();
    readonly int _softFrames;
    readonly int _softBytes;
    readonly int _hardFrames;
    readonly int _hardBytes;
    readonly string _endpoint;
    readonly ulong _connectionId;
    int _orderedBytes;
    int _openBytes;
    int _visualFrames;
    // Frames Take already handed to the pump but the socket write has not finished.
    int _inflightFrames;
    int _inflightBytes;
    bool _closed;
    bool _loggedClosed;

    readonly record struct CoalesceKey(ushort Opcode, ulong EntityValue, uint EntityGeneration);

    readonly record struct Queued(PacketEnvelope Envelope, int Bytes, bool Visual);

    sealed class OpenEntry(CoalesceKey key, PacketEnvelope envelope)
    {
        public CoalesceKey Key { get; } = key;
        public PacketEnvelope Envelope { get; set; } = envelope;
    }

    public OutboundMailbox(int maxFrames, int maxBytes, ulong connectionId, string endpoint, int? hardBytes = null)
    {
        if (maxFrames < 1)
            throw new ArgumentOutOfRangeException(nameof(maxFrames), maxFrames, "outbound soft frame limit must be positive");
        if (maxBytes < 1)
            throw new ArgumentOutOfRangeException(nameof(maxBytes), maxBytes, "outbound soft byte limit must be positive");
        int hard = hardBytes ?? HardBytesSetting;
        if (hard < maxBytes)
            throw new ArgumentOutOfRangeException(nameof(hardBytes), hard,
                $"outbound hard byte limit ({hard}) must be at least the soft limit ({maxBytes})");

        _softFrames = maxFrames;
        _softBytes = maxBytes;
        _hardBytes = hard;
        // Minimum-sized frames must fill the byte limit before the frame count reports full.
        _hardFrames = Math.Max(maxFrames, hard / PacketEnvelope.HeaderSize);
        _connectionId = connectionId;
        _endpoint = endpoint;
    }

    /// <summary><c>FINEAGE_NET_OUTBOUND_HARD_BYTES</c>, or <see cref="DefaultHardBytes"/>. Invalid text throws.</summary>
    public static int HardBytesSetting
    {
        get
        {
            string? text = Environment.GetEnvironmentVariable("FINEAGE_NET_OUTBOUND_HARD_BYTES");
            if (string.IsNullOrWhiteSpace(text))
                return DefaultHardBytes;
            if (!int.TryParse(text, out int value) || value < 1)
                throw new InvalidOperationException($"FINEAGE_NET_OUTBOUND_HARD_BYTES='{text}' is not a positive byte count");
            return value;
        }
    }

    public int SoftBytes => _softBytes;
    public int HardBytes => _hardBytes;

    public int QueuedFrames
    {
        get
        {
            lock (_gate)
                return _ordered.Count + _open.Count;
        }
    }

    public int QueuedBytes
    {
        get
        {
            lock (_gate)
                return _orderedBytes + _openBytes;
        }
    }

    /// <summary>Bytes queued plus bytes the pump has taken but not finished writing.</summary>
    public int HeldBytes
    {
        get
        {
            lock (_gate)
                return UsedBytes;
        }
    }

    int UsedBytes => _orderedBytes + _openBytes + _inflightBytes;

    int UsedFrames => _ordered.Count + _open.Count + _inflightFrames;

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
    /// Admits <paramref name="envelopes"/> in order without waiting. A dropped visual does not fail the batch.
    /// Returns <see cref="MailboxAdmit.Rejected"/> as soon as a frame that must not be lost does not fit under the
    /// hard limit; the caller closes the connection. Frames admitted before the rejection stay queued, which is
    /// harmless because the connection is closing.
    /// </summary>
    public MailboxAdmit TryAdmitBatch(IReadOnlyList<PacketEnvelope> envelopes, bool forceWholeBatch) =>
        Admit(envelopes, forceWholeBatch, asSnapshot: false);

    /// <summary>
    /// Knownlist / appearance burst. Every frame is kept verbatim and in order, ahead of later movement: a spawn
    /// burst is never replaced or dropped (that would leave the client without those entities). Over the hard
    /// limit it is <see cref="MailboxAdmit.Rejected"/> and the client is disconnected to resync on re-enter.
    /// </summary>
    public MailboxAdmit TryMergeSnapshot(IReadOnlyList<PacketEnvelope> frames) =>
        Admit(frames, forceWholeBatch: false, asSnapshot: true);

    MailboxAdmit Admit(IReadOnlyList<PacketEnvelope> envelopes, bool forceWholeBatch, bool asSnapshot)
    {
        lock (_gate)
        {
            if (_closed)
                return ClosedOnce();
            if (envelopes.Count == 0)
                return MailboxAdmit.Accepted;

            // Nothing queued and nothing in a socket write: this one burst may exceed the limits.
            bool burst = forceWholeBatch || (UsedFrames == 0);
            MailboxAdmit result = MailboxAdmit.Accepted;
            if (asSnapshot)
                SealOpen();
            for (int i = 0; i < envelopes.Count; i++)
            {
                PacketEnvelope envelope = envelopes[i];
                MailboxAdmit admit = asSnapshot ? AdmitReliable(envelope, burst, barrier: false) : AdmitOne(envelope, burst);
                if (admit == MailboxAdmit.Rejected)
                {
                    RecordDepth();
                    return MailboxAdmit.Rejected;
                }

                if (admit == MailboxAdmit.Dropped && envelopes.Count == 1)
                    result = MailboxAdmit.Dropped;
            }

            RecordDepth();
            return result;
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
            int count = _ordered.Count + _open.Count;
            int bytes = _orderedBytes + _openBytes;
            for (int i = 0; i < _ordered.Count; i++)
                destination.Add(_ordered[i].Envelope);
            for (var node = _openOrder.First; node != null; node = node.Next)
                destination.Add(node.Value.Envelope);

            _ordered.Clear();
            _orderedBytes = 0;
            _visualFrames = 0;
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

    MailboxAdmit AdmitOne(PacketEnvelope envelope, bool burst)
    {
        switch (OutboundLanePolicy.Classify(envelope.Opcode))
        {
            case OutboundLane.Movement:
                return Coalesce(envelope, burst);
            case OutboundLane.Visual:
                return AppendVisual(envelope, burst);
            case OutboundLane.Snapshot:
                return AdmitReliable(envelope, burst, barrier: true);
            default:
                return AdmitReliable(envelope, burst, barrier: OutboundOrdering.IsBarrier(envelope.Opcode));
        }
    }

    MailboxAdmit AdmitReliable(PacketEnvelope envelope, bool burst, bool barrier)
    {
        if (barrier)
            SealOpen();

        int size = FrameBytes(envelope);
        if (!FitsSoft(size))
            ShedVisuals(size, toHard: false);
        if (!burst && !FitsHard(size))
        {
            ShedVisuals(size, toHard: true);
            if (!FitsHard(size))
            {
                OutboundLaneMetrics.Full(OutboundLaneOf(envelope.Opcode));
                NetHostLog.Error(
                    $"connectionId={_connectionId} endpoint={_endpoint} rejected reliable opcode={envelope.Opcode} ({size} bytes): " +
                    $"outbound mailbox is above its hard limit after shedding visuals ({Describe()})");
                return MailboxAdmit.Rejected;
            }
        }

        AppendOrdered(envelope, size, visual: false);
        return MailboxAdmit.Accepted;
    }

    MailboxAdmit Coalesce(PacketEnvelope envelope, bool burst)
    {
        if (!TryEntityKey(envelope.Opcode, envelope.Payload, out CoalesceKey key))
            throw new InvalidOperationException(
                $"connectionId={_connectionId} endpoint={_endpoint} movement opcode={envelope.Opcode} payload is {envelope.Payload.Length} bytes; " +
                "a coalesced frame must start with its 12-byte entity key");

        int size;
        if (_open.TryGetValue(key, out LinkedListNode<OpenEntry>? node))
        {
            PacketEnvelope previous = node.Value.Envelope;
            PacketEnvelope latest = envelope.Opcode == NetOpcode.StatusUpdate ? MergeStatus(previous, envelope) : envelope;
            int oldSize = FrameBytes(previous);
            size = FrameBytes(latest);
            _openBytes += size - oldSize;
            node.Value.Envelope = latest;
            // Newest pose goes to the end so it stays after anything coalesced since the superseded one.
            _openOrder.Remove(node);
            _openOrder.AddLast(node);
            OutboundLaneMetrics.Coalesced(OutboundLane.Movement);
            OutboundLaneMetrics.Shed(OutboundLane.Movement, oldSize);
            return MailboxAdmit.Accepted;
        }

        size = FrameBytes(envelope);
        if (!FitsSoft(size))
            ShedVisuals(size, toHard: false);
        if (!burst && !FitsHard(size))
        {
            ShedVisuals(size, toHard: true);
            if (!FitsHard(size))
            {
                OutboundLaneMetrics.Full(OutboundLane.Movement);
                NetHostLog.Error(
                    $"connectionId={_connectionId} endpoint={_endpoint} rejected movement opcode={envelope.Opcode} entity={key.EntityValue}: " +
                    $"newest state for a new entity does not fit under the hard limit ({Describe()})");
                return MailboxAdmit.Rejected;
            }
        }

        var created = _openOrder.AddLast(new OpenEntry(key, envelope));
        _open[key] = created;
        _openBytes += size;
        return MailboxAdmit.Accepted;
    }

    MailboxAdmit AppendVisual(PacketEnvelope envelope, bool burst)
    {
        int size = FrameBytes(envelope);
        if (!burst && !FitsSoft(size))
        {
            ShedVisuals(size, toHard: false);
            if (!FitsSoft(size))
            {
                OutboundLaneMetrics.Full(OutboundLane.Visual);
                OutboundLaneMetrics.Shed(OutboundLane.Visual, size);
                OutboundLaneMetrics.LogLimited(
                    $"connectionId={_connectionId} endpoint={_endpoint} dropped visual opcode={envelope.Opcode}: mailbox is above its soft limit and holds no older visual to shed ({Describe()})");
                return MailboxAdmit.Dropped;
            }
        }

        AppendOrdered(envelope, size, visual: true);
        return MailboxAdmit.Accepted;
    }

    /// <summary>Drops queued visual frames, oldest first, until <paramref name="size"/> more bytes fit the limit.</summary>
    void ShedVisuals(int size, bool toHard)
    {
        if (_visualFrames == 0)
            return;

        int limitBytes = toHard ? _hardBytes : _softBytes;
        int limitFrames = toHard ? _hardFrames : _softFrames;
        int excessBytes = UsedBytes + size - limitBytes;
        int excessFrames = UsedFrames + 1 - limitFrames;
        if (excessBytes <= 0 && excessFrames <= 0)
            return;

        int shedFrames = 0;
        int shedBytes = 0;
        int write = 0;
        for (int read = 0; read < _ordered.Count; read++)
        {
            Queued item = _ordered[read];
            if (item.Visual && (shedBytes < excessBytes || shedFrames < excessFrames))
            {
                shedFrames++;
                shedBytes += item.Bytes;
                continue;
            }

            _ordered[write++] = item;
        }

        if (shedFrames == 0)
            return;
        _ordered.RemoveRange(write, _ordered.Count - write);
        _orderedBytes -= shedBytes;
        _visualFrames -= shedFrames;
        OutboundLaneMetrics.Evicted(OutboundLane.Visual, shedFrames);
        OutboundLaneMetrics.Shed(OutboundLane.Visual, shedBytes);
        OutboundLaneMetrics.LogLimited(
            $"connectionId={_connectionId} endpoint={_endpoint} shed {shedFrames} visual frames / {shedBytes} bytes to stay under the {(toHard ? "hard" : "soft")} limit ({Describe()})");
    }

    /// <summary>
    /// A barrier must be observed after the newest coalesced state queued before it. Moves the open movement
    /// frames into the ordered stream; their bytes are already counted, so this never fails.
    /// </summary>
    void SealOpen()
    {
        if (_openOrder.Count == 0)
            return;

        for (var node = _openOrder.First; node != null; node = node.Next)
        {
            PacketEnvelope envelope = node.Value.Envelope;
            _ordered.Add(new Queued(envelope, FrameBytes(envelope), Visual: false));
        }

        _orderedBytes += _openBytes;
        _open.Clear();
        _openOrder.Clear();
        _openBytes = 0;
    }

    void AppendOrdered(PacketEnvelope envelope, int size, bool visual)
    {
        _ordered.Add(new Queued(envelope, size, visual));
        _orderedBytes += size;
        if (visual)
            _visualFrames++;
    }

    bool FitsSoft(int size) => UsedBytes + size <= _softBytes && UsedFrames + 1 <= _softFrames;

    bool FitsHard(int size) => UsedBytes + size <= _hardBytes && UsedFrames + 1 <= _hardFrames;

    string Describe() =>
        $"held={UsedBytes} bytes / {UsedFrames} frames (in flight {_inflightBytes} bytes), soft={_softBytes} bytes, hard={_hardBytes} bytes";

    static OutboundLane OutboundLaneOf(ushort opcode) => OutboundLanePolicy.Classify(opcode);

    static PacketEnvelope MergeStatus(PacketEnvelope previous, PacketEnvelope latest)
    {
        StatusUpdateNetMsg older = NetCodec.DecodeStatusUpdate(previous.Payload);
        StatusUpdateNetMsg newer = NetCodec.DecodeStatusUpdate(latest.Payload);
        var ids = new List<int>(older.AttributeIds.Length + newer.AttributeIds.Length);
        var values = new List<int>(ids.Capacity);
        for (int i = 0; i < older.AttributeIds.Length; i++)
        {
            if (Array.IndexOf(newer.AttributeIds, older.AttributeIds[i]) >= 0)
                continue;
            ids.Add(older.AttributeIds[i]);
            values.Add(older.Values[i]);
        }

        ids.AddRange(newer.AttributeIds);
        values.AddRange(newer.Values);
        byte[] payload = NetCodec.EncodeStatusUpdate(new StatusUpdateNetMsg
        {
            EntityValue = newer.EntityValue,
            EntityGeneration = newer.EntityGeneration,
            AttributeIds = [.. ids],
            Values = [.. values]
        });
        return new PacketEnvelope
        {
            Major = latest.Major,
            Minor = latest.Minor,
            Opcode = latest.Opcode,
            Flags = latest.Flags,
            RequestSequence = latest.RequestSequence,
            Payload = payload,
            // Queue age is measured from the oldest state this frame carries.
            MailboxQueuedAtTimestamp = previous.MailboxQueuedAtTimestamp > 0 && latest.MailboxQueuedAtTimestamp > 0
                ? Math.Min(previous.MailboxQueuedAtTimestamp, latest.MailboxQueuedAtTimestamp)
                : Math.Max(previous.MailboxQueuedAtTimestamp, latest.MailboxQueuedAtTimestamp)
        };
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
        FineageTelemetry.OutboundQueueFrames.Record(_ordered.Count + _open.Count, endpoint);
        FineageTelemetry.OutboundQueueBytes.Record(_orderedBytes + _openBytes, endpoint);
    }

    static int FrameBytes(PacketEnvelope envelope) => PacketEnvelope.HeaderSize + envelope.Payload.Length;

    /// <summary>
    /// Entity a coalesced frame describes. Codec frames start with the 12-byte entity key (u64 value, u32
    /// generation); <c>AbnormalStatusUpdate</c> starts with an i32 object id and carries the full effect list,
    /// so its newest frame replaces the pending one.
    /// </summary>
    static bool TryEntityKey(ushort opcode, byte[] payload, out CoalesceKey key)
    {
        key = default;
        ReadOnlySpan<byte> span = payload;
        if (opcode == NetOpcode.AbnormalStatusUpdate)
        {
            if (payload.Length < 4)
                return false;
            key = new CoalesceKey(opcode, BinaryPrimitives.ReadUInt32LittleEndian(span), 0);
            return true;
        }

        if (payload.Length < 12)
            return false;
        ulong entity = BinaryPrimitives.ReadUInt64LittleEndian(span);
        uint generation = BinaryPrimitives.ReadUInt32LittleEndian(span[8..]);
        key = new CoalesceKey(opcode, entity, generation);
        return true;
    }
}
