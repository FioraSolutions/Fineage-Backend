using System.Buffers;
using Fineage.Net.Contract;
using Fineage.Net.Host;
using Fineage.Zone.World;

namespace Fineage.Zone.Net;

/// <summary>
/// AOI knownlist spawn/despawn fanout for enter-move / revive / bypass (#353 B2b).
/// Extracted from <see cref="NetZoneServer"/> so handlers share one implementer.
/// </summary>
sealed class NetKnownlistFanout
{
    /// <summary>Spawn or drop, its follow-ups, the reciprocal player spawn, and that spawn's follow-ups.</summary>
    const int FramesPerEnter = 2 + 2 * NetEnterWorldProjector.MaxPresenceFollowUps;

    /// <summary>Despawn on the mover, and the mover's despawn on a player who left the knownlist.</summary>
    const int FramesPerLeave = 2;

    static readonly List<NetFrame> NoDiff = new(0);

    readonly ZoneWorld _world;
    readonly EntityIdMap _ids;
    readonly NetPresenceHub _presence;
    readonly Func<ZoneEntity, int> _playerSpawnPAtkSpd;

    public NetKnownlistFanout(
        ZoneWorld world,
        EntityIdMap ids,
        NetPresenceHub presence,
        Func<ZoneEntity, int> playerSpawnPAtkSpd)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _ids = ids ?? throw new ArgumentNullException(nameof(ids));
        _presence = presence ?? throw new ArgumentNullException(nameof(presence));
        _playerSpawnPAtkSpd = playerSpawnPAtkSpd ?? throw new ArgumentNullException(nameof(playerSpawnPAtkSpd));
    }

    /// <summary>
    /// On the simulation thread: the spawn and despawn frames for a knownlist change from <paramref name="before"/> to
    /// <paramref name="after"/>, for the mover and the peers that start or stop seeing it.
    /// Ids are copied into pooled buffers, sorted, and merged. Enters are emitted in ascending id order, then leaves,
    /// which is the same order as sorting each set. Frames are staged in a pooled buffer and copied into one result
    /// list. An empty diff returns a shared empty list; callers must not mutate it.
    /// </summary>
    public List<NetFrame> PlanDiff(ZoneSession zoneSession, EntityId self, HashSet<int> before, HashSet<int> after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        int beforeCount = before.Count;
        int afterCount = after.Count;
        int[] beforeBuf = ArrayPool<int>.Shared.Rent(beforeCount);
        int[] afterBuf = ArrayPool<int>.Shared.Rent(afterCount);
        NetFrame[]? frameBuf = null;
        int written = 0;
        int frameLimit = 0;
        try
        {
            Fill(before, beforeBuf.AsSpan(0, beforeCount));
            Fill(after, afterBuf.AsSpan(0, afterCount));
            if (beforeCount > 1)
                beforeBuf.AsSpan(0, beforeCount).Sort();
            if (afterCount > 1)
                afterBuf.AsSpan(0, afterCount).Sort();

            ReadOnlySpan<int> beforeIds = beforeBuf.AsSpan(0, beforeCount);
            ReadOnlySpan<int> afterIds = afterBuf.AsSpan(0, afterCount);
            CountDiff(beforeIds, afterIds, out int entered, out int left);
            if (entered == 0 && left == 0)
                return NoDiff;

            frameLimit = entered * FramesPerEnter + left * FramesPerLeave;
            frameBuf = ArrayPool<NetFrame>.Shared.Rent(frameLimit);
            var staged = frameBuf.AsSpan(0, frameLimit);
            written = EmitEnters(staged, 0, zoneSession, self, beforeIds, afterIds);
            written = EmitLeaves(staged, written, self, beforeIds, afterIds);
            if (written == 0)
                return NoDiff;

            var frames = new List<NetFrame>(written);
            for (int i = 0; i < written; i++)
                frames.Add(staged[i]);
            return frames;
        }
        finally
        {
            if (frameBuf != null)
            {
                frameBuf.AsSpan(0, Math.Min(frameLimit, frameBuf.Length)).Clear();
                ArrayPool<NetFrame>.Shared.Return(frameBuf);
            }

            ArrayPool<int>.Shared.Return(beforeBuf);
            ArrayPool<int>.Shared.Return(afterBuf);
        }
    }

    public Task SendAsync(NetConnection conn, IEnumerable<NetFrame> frames, CancellationToken ct) =>
        NetFrame.SendAsync(conn, _presence, frames, ct);

    static void Fill(HashSet<int> source, Span<int> dest)
    {
        int i = 0;
        foreach (int id in source)
        {
            if ((uint)i >= (uint)dest.Length)
                throw new InvalidOperationException(
                    $"knownlist snapshot grew past {dest.Length} ids while planning the diff");
            dest[i++] = id;
        }

        if (i != dest.Length)
            throw new InvalidOperationException(
                $"knownlist snapshot contained {i} ids, expected {dest.Length}");
    }

    static void CountDiff(ReadOnlySpan<int> before, ReadOnlySpan<int> after, out int entered, out int left)
    {
        entered = 0;
        left = 0;
        int i = 0;
        int j = 0;
        while (i < before.Length && j < after.Length)
        {
            int b = before[i];
            int a = after[j];
            if (b == a)
            {
                i++;
                j++;
            }
            else if (b < a)
            {
                left++;
                i++;
            }
            else
            {
                entered++;
                j++;
            }
        }

        left += before.Length - i;
        entered += after.Length - j;
    }

    static void Add(Span<NetFrame> dest, ref int count, NetFrame frame)
    {
        if ((uint)count >= (uint)dest.Length)
            throw new InvalidOperationException(
                $"knownlist diff emitted more than {dest.Length} frames");
        dest[count++] = frame;
    }

    int EmitEnters(
        Span<NetFrame> dest,
        int count,
        ZoneSession zoneSession,
        EntityId self,
        ReadOnlySpan<int> before,
        ReadOnlySpan<int> after)
    {
        int i = 0;
        for (int j = 0; j < after.Length; j++)
        {
            int id = after[j];
            while (i < before.Length && before[i] < id)
                i++;
            if (i < before.Length && before[i] == id)
                continue;
            count = EmitEnter(dest, count, zoneSession, self, id);
        }

        return count;
    }

    int EmitLeaves(Span<NetFrame> dest, int count, EntityId self, ReadOnlySpan<int> before, ReadOnlySpan<int> after)
    {
        byte[]? despawnSelf = null;
        int j = 0;
        for (int i = 0; i < before.Length; i++)
        {
            int id = before[i];
            while (j < after.Length && after[j] < id)
                j++;
            if (j < after.Length && after[j] == id)
                continue;

            var forgottenId = _ids.AllocateOrGet(id);
            Add(dest, ref count, NetFrame.ToSelf(
                NetOpcode.EntityDespawn,
                NetCodec.EncodeEntityDespawn(new EntityDespawnMsg
                {
                    EntityValue = forgottenId.Value,
                    EntityGeneration = forgottenId.Generation
                })));
            if (!_world.TryGetLivePlayerById(id, out _))
                continue;

            despawnSelf ??= NetCodec.EncodeEntityDespawn(new EntityDespawnMsg
            {
                EntityValue = self.Value,
                EntityGeneration = self.Generation
            });
            Add(dest, ref count, new NetFrame(id, NetOpcode.EntityDespawn, despawnSelf));
        }

        return count;
    }

    int EmitEnter(Span<NetFrame> dest, int count, ZoneSession zoneSession, EntityId self, int id)
    {
        if (!_world.TryGetEntityById(id, out var newlySeen))
        {
            ZoneLog.Error(
                $"knownlist enter missing entity objectId={id} session={zoneSession.WireSessionId} self={self.Value}/{self.Generation}");
            return count;
        }

        var otherId = _ids.AllocateOrGet(id);
        if (newlySeen.Kind == ZoneEntityKind.GroundItem)
        {
            Add(dest, ref count, NetFrame.ToSelf(
                NetOpcode.DropItem,
                NetCodec.EncodeDropItem(new DropItemNetMsg
                {
                    DropperObjectId = 0,
                    EntityValue = otherId.Value,
                    EntityGeneration = otherId.Generation,
                    ItemId = newlySeen.GroundItemId,
                    X = newlySeen.X,
                    Y = newlySeen.Y,
                    Z = newlySeen.Z,
                    Stackable = newlySeen.GroundStackable,
                    Count = newlySeen.GroundCount
                })));
            return count;
        }

        var spawn = NetCodec.EncodeEntitySpawn(
            NetEnterWorldProjector.BuildPresenceSpawn(newlySeen, otherId, _playerSpawnPAtkSpd(newlySeen)));
        Add(dest, ref count, NetFrame.ToSelf(NetOpcode.EntitySpawn, spawn));
        count = AppendFollowUps(dest, count, peerObjectId: 0, newlySeen, otherId);
        if (newlySeen.Kind == ZoneEntityKind.Player &&
            _world.TryGetLivePlayer(zoneSession, out var selfEntity))
        {
            var toPeer = NetCodec.EncodeEntitySpawn(
                NetEnterWorldProjector.BuildRemoteSpawn(selfEntity, self, _playerSpawnPAtkSpd(selfEntity)));
            Add(dest, ref count, new NetFrame(id, NetOpcode.EntitySpawn, toPeer));
            count = AppendFollowUps(dest, count, id, selfEntity, self);
        }

        return count;
    }

    int AppendFollowUps(Span<NetFrame> dest, int count, int peerObjectId, ZoneEntity entity, EntityId id)
    {
        var rented = ArrayPool<PresenceWireSlot>.Shared.Rent(NetEnterWorldProjector.MaxPresenceFollowUps);
        try
        {
            int followUps = NetEnterWorldProjector.WritePresenceFollowUps(
                rented.AsSpan(0, NetEnterWorldProjector.MaxPresenceFollowUps), entity, id, _world);
            for (int i = 0; i < followUps; i++)
            {
                byte[] payload = EnterWire.EncodePresence(rented[i]);
                Add(dest, ref count, peerObjectId == 0
                    ? NetFrame.ToSelf(rented[i].Opcode, payload)
                    : new NetFrame(peerObjectId, rented[i].Opcode, payload));
            }

            return count;
        }
        finally
        {
            rented.AsSpan(0, NetEnterWorldProjector.MaxPresenceFollowUps).Clear();
            ArrayPool<PresenceWireSlot>.Shared.Return(rented);
        }
    }
}
