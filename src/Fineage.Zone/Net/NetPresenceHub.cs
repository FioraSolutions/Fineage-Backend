using Fineage.Net.Contract;
using Fineage.Net.Host;

namespace Fineage.Zone.Net;

/// <summary>
/// Maps live Zone object ids to Net connections for presence fanout
/// (MoveState / EntitySpawn / EntityDespawn). V2 MessagePack flushes stay on ZoneSession streams.
/// </summary>
public sealed class NetPresenceHub
{
    readonly object _gate = new();
    readonly Dictionary<int, Entry> _byObjectId = [];

    readonly record struct Entry(NetConnection Conn, EntityId EntityId);

    public void Register(int objectId, NetConnection conn, EntityId entityId)
    {
        ArgumentNullException.ThrowIfNull(conn);
        if (objectId <= 0)
            throw new ArgumentOutOfRangeException(nameof(objectId));
        lock (_gate)
            _byObjectId[objectId] = new Entry(conn, entityId);
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
        lock (_gate)
        {
            if (!_byObjectId.TryGetValue(objectId, out var entry) || !ReferenceEquals(entry.Conn, owner))
                return false;
            _byObjectId.Remove(objectId);
            return true;
        }
    }

    /// <summary>Force-removes the entry without an ownership check (world reset / tests). The server uses the owner overload.</summary>
    public void Unregister(int objectId)
    {
        lock (_gate)
            _byObjectId.Remove(objectId);
    }

    public bool TryGet(int objectId, out NetConnection conn, out EntityId entityId)
    {
        lock (_gate)
        {
            if (_byObjectId.TryGetValue(objectId, out var entry))
            {
                conn = entry.Conn;
                entityId = entry.EntityId;
                return true;
            }
        }

        conn = null!;
        entityId = default;
        return false;
    }

    public async Task SendAsync(int objectId, ushort opcode, byte[] payload, CancellationToken ct)
    {
        if (!TryGet(objectId, out var conn, out _))
            return;
        await conn.SendAsync(opcode, payload, ct: ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Fanout path: never waits. A full outbound queue drops the frame and counts
    /// <c>fineage.outbound.dropped</c> instead of stalling the tick.
    /// </summary>
    public void TrySend(int objectId, ushort opcode, byte[] payload)
    {
        if (!TryGet(objectId, out var conn, out _))
            return;
        conn.TrySend(opcode, payload);
    }

    public void TryBroadcast(IEnumerable<int> objectIds, ushort opcode, byte[] payload)
    {
        foreach (int id in objectIds)
            TrySend(id, opcode, payload);
    }
}
