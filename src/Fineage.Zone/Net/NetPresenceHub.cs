using Fineage.Net.Contract;
using Fineage.Net.Host;
using Fineage.Protocol.Telemetry;
using Fineage.Zone.World;

namespace Fineage.Zone.Net;

/// <summary>
/// Maps live Zone object ids to Net connections for presence fanout
/// (MoveState / EntitySpawn / EntityDespawn). The hub is the only fan-out path: ZoneSession carries no stream.
/// Writers publish an immutable table; the send path reads that table and never takes <see cref="_writeLock"/>.
/// </summary>
public sealed class NetPresenceHub : IAsyncDisposable
{
    readonly object _writeLock = new();
    readonly NetFanoutStripes _stripes = new();
    readonly bool _ownsStripes = true;
    PresenceTable _published = PresenceTable.Empty;

    readonly record struct PresenceEntry(ConnectionHandle Handle, EntityId EntityId);

    /// <summary>Tests and custom wiring may inject stripe routing.</summary>
    public NetPresenceHub(NetFanoutStripes? stripes = null)
    {
        if (stripes != null)
        {
            _stripes = stripes;
            _ownsStripes = false;
        }
    }

    public void Register(int objectId, NetConnection conn, EntityId entityId)
    {
        ArgumentNullException.ThrowIfNull(conn);
        if (objectId <= 0)
            throw new ArgumentOutOfRangeException(nameof(objectId));

        // Bind before the handle is published so a send that observes the table sees a live generation.
        conn.BindPresence(objectId, entityId.Generation);
        lock (_writeLock)
        {
            var next = _published.Clone();
            if (next.TryGetValue(objectId, out PresenceEntry previous) &&
                !ReferenceEquals(previous.Handle.Connection, conn))
                previous.Handle.Connection.UnbindPresence(objectId, previous.Handle.EntityGeneration);

            next[objectId] = new PresenceEntry(new ConnectionHandle(objectId, entityId.Generation, conn), entityId);
            Volatile.Write(ref _published, new PresenceTable(next));
        }
    }

    /// <summary>
    /// Removes the entry only when it still belongs to <paramref name="owner"/>. World object ids are reused as
    /// soon as a player leaves, so a connection that is still tearing down must never unregister the entry a
    /// newer connection has just claimed — that silently dropped the newcomer's presence fanout (MoveState
    /// "NOT REGISTERED in presence hub") until its client gave up.
    /// </summary>
    public bool Unregister(int objectId, NetConnection owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        lock (_writeLock)
        {
            if (!_published.TryGet(objectId, out PresenceEntry entry) ||
                !ReferenceEquals(entry.Handle.Connection, owner))
                return false;

            owner.UnbindPresence(objectId, entry.Handle.EntityGeneration);
            var next = _published.Clone();
            next.Remove(objectId);
            Volatile.Write(ref _published, new PresenceTable(next));
            return true;
        }
    }

    /// <summary>Force-removes the entry without an ownership check (world reset / tests). The server uses the owner overload.</summary>
    public void Unregister(int objectId)
    {
        lock (_writeLock)
        {
            if (!_published.TryGet(objectId, out PresenceEntry entry))
                return;
            entry.Handle.Connection.UnbindPresence(objectId, entry.Handle.EntityGeneration);
            var next = _published.Clone();
            next.Remove(objectId);
            Volatile.Write(ref _published, new PresenceTable(next));
        }
    }

    public bool TryGet(int objectId, out NetConnection conn, out EntityId entityId)
    {
        if (Volatile.Read(ref _published).TryGet(objectId, out PresenceEntry entry))
        {
            conn = entry.Handle.Connection;
            entityId = entry.EntityId;
            return true;
        }

        conn = null!;
        entityId = default;
        return false;
    }

    /// <summary>
    /// Resolves <paramref name="objectIds"/> against the published handle table once.
    /// Missing object ids are omitted and logged; the returned handles are what the send path stores.
    /// </summary>
    public AudienceSnapshot CaptureAudience(IReadOnlyList<int> objectIds, ushort? opcode = null)
    {
        if (objectIds == null || objectIds.Count == 0)
            return AudienceSnapshot.Empty;

        PresenceTable table = Volatile.Read(ref _published);
        var handles = new ConnectionHandle[objectIds.Count];
        int count = 0;
        int missing = 0;
        for (int i = 0; i < objectIds.Count; i++)
        {
            int objectId = objectIds[i];
            if (!table.TryGet(objectId, out PresenceEntry entry))
            {
                missing++;
                ZoneLog.Error(opcode is { } code
                    ? $"presence audience is missing a connection handle for objectId={objectId} opcode={code}"
                    : $"presence audience is missing a connection handle for objectId={objectId}");
                continue;
            }

            handles[count++] = entry.Handle;
        }

        if (count == 0)
            return new AudienceSnapshot([], missing);
        if (count != handles.Length)
            Array.Resize(ref handles, count);
        return new AudienceSnapshot(handles, missing);
    }

    /// <summary>Enqueues <paramref name="payload"/> to every handle in <paramref name="audience"/>.</summary>
    public void Deliver(in AudienceSnapshot audience, ushort opcode, byte[] payload)
    {
        ReadOnlySpan<ConnectionHandle> handles = audience.Handles;
        if (handles.Length == 0)
            return;

        for (int i = 0; i < handles.Length; i++)
            Dispatch(handles[i], opcode, payload);
    }

    public async Task SendAsync(int objectId, ushort opcode, byte[] payload, CancellationToken ct)
    {
        if (!TryGet(objectId, out var conn, out _))
        {
            ZoneLog.Error($"presence send is missing a connection handle for objectId={objectId} opcode={opcode}");
            return;
        }

        await conn.SendAsync(opcode, payload, ct: ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Fanout path: never waits on the caller. Delivery is a connection handle from the published table,
    /// then one stripe batch per connection (see <see cref="FanoutDeliveryScope"/>).
    /// </summary>
    public void TrySend(int objectId, ushort opcode, byte[] payload)
    {
        if (!TryResolve(objectId, opcode, out ConnectionHandle handle))
            return;
        Dispatch(handle, opcode, payload);
    }

    /// <summary>
    /// Critical presence (spawn/despawn/inventory). Same handle path as <see cref="TrySend"/>; the stripe
    /// waits for mailbox room instead of dropping the frame.
    /// </summary>
    public void TrySendCritical(int objectId, ushort opcode, byte[] payload)
    {
        if (!TryResolve(objectId, opcode, out ConnectionHandle handle))
            return;
        Dispatch(handle, opcode, payload);
    }

    bool TryResolve(int objectId, ushort opcode, out ConnectionHandle handle)
    {
        if (Volatile.Read(ref _published).TryGet(objectId, out PresenceEntry entry))
        {
            handle = entry.Handle;
            return true;
        }

        ZoneLog.Error($"presence send is missing a connection handle for objectId={objectId} opcode={opcode}");
        handle = default;
        return false;
    }

    void Dispatch(ConnectionHandle handle, ushort opcode, byte[] payload)
    {
        long stamp = CommandFanoutTimingContext.MailboxStamp();
        CommandFanoutTimingContext.NoteRecipient(payload.Length);
        if (!FanoutDeliveryScope.TryBuffer(_stripes, handle, opcode, payload, stamp))
        {
            _stripes.EnqueueTrySend(handle, opcode, payload, stamp);
            RecordCommandToMailboxes();
        }
    }

    static void RecordCommandToMailboxes()
    {
        if (CommandFanoutTimingContext.TryRecordMailbox(out double commitToMailboxMs))
        {
            FineageTelemetry.CommandToMailboxesMilliseconds.Record(commitToMailboxMs);
            FineageTelemetry.CommandTimelineMilliseconds.Record(commitToMailboxMs,
                new KeyValuePair<string, object?>("phase", "fanout_enqueue_to_mailbox"));
        }
    }

    public ValueTask DisposeAsync() => _ownsStripes ? _stripes.DisposeAsync() : ValueTask.CompletedTask;

    public void TryBroadcast(IEnumerable<int> objectIds, ushort opcode, byte[] payload)
    {
        if (objectIds is IReadOnlyList<int> list)
        {
            Deliver(CaptureAudience(list, opcode), opcode, payload);
            return;
        }

        Deliver(CaptureAudience(objectIds as int[] ?? objectIds.ToArray(), opcode), opcode, payload);
    }

    public void TryBroadcastCritical(IEnumerable<int> objectIds, ushort opcode, byte[] payload) =>
        TryBroadcast(objectIds, opcode, payload);

    sealed class PresenceTable
    {
        public static readonly PresenceTable Empty = new(new Dictionary<int, PresenceEntry>());

        readonly Dictionary<int, PresenceEntry> _byObjectId;

        public PresenceTable(Dictionary<int, PresenceEntry> byObjectId) => _byObjectId = byObjectId;

        public bool TryGet(int objectId, out PresenceEntry entry) => _byObjectId.TryGetValue(objectId, out entry);

        public Dictionary<int, PresenceEntry> Clone() => new(_byObjectId);
    }
}
