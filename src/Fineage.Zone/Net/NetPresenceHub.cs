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
        try
        {
            await conn.SendAsync(opcode, payload, ct: ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
        {
            /* peer gone */
        }
    }

    public async Task BroadcastAsync(IEnumerable<int> objectIds, ushort opcode, byte[] payload, CancellationToken ct)
    {
        foreach (int id in objectIds)
            await SendAsync(id, opcode, payload, ct).ConfigureAwait(false);
    }
}
