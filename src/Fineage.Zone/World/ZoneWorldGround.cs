using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Drops;
using MessagePack;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    public async Task<string?> TryPickupAsync(ZoneSession session, PickupRequestMsg request, CancellationToken ct)
    {
        if (!IsLive(session))
            return "world";

        var outbound = new List<Pending>();
        string? error;
        lock (_gate)
            error = TryPickup(session, request.ObjectId, outbound);
        if (error != null)
            return error;

        await FlushSelfAsync(session, outbound, ct).ConfigureAwait(false);
        return null;
    }

    public async Task<string?> TryDropItemAsync(ZoneSession session, DropItemRequestMsg request, CancellationToken ct)
    {
        if (!IsLive(session))
            return "world";

        var outbound = new List<Pending>();
        string? error;
        lock (_gate)
            error = TryDropItem(session, request, outbound);
        if (error != null)
            return error;

        await FlushSelfAsync(session, outbound, ct).ConfigureAwait(false);
        return null;
    }

    /// <summary>Interlude SystemMessageId 151 CANNOT_DISCARD_DISTANCE_TOO_FAR.</summary>
    public const int SysMsgDiscardTooFar = 151;
    /// <summary>Interlude SystemMessageId 28/29/30: obtained adena / S2 S1 / S1.</summary>
    public const int SysMsgObtainedAdena = 28;
    public const int SysMsgObtainedCount = 29;
    public const int SysMsgObtainedItem = 30;
    /// <summary>Interlude SystemMessageId 145 FAILED_TO_PICKUP_S1.</summary>
    public const int SysMsgFailedToPickup = 145;
    /// <summary>Mobius DropProtection.PROTECTED_MILLIS: killer-only pickup window.</summary>
    public const long DropProtectMs = 15_000;
    /// <summary>Mobius General AutoDestroyDroppedItemAfter = 600 s (mob loot; player discards are kept, DestroyPlayerDroppedItem = false).</summary>
    public const long GroundItemAutoDestroyMs = 600_000;
    /// <summary>Mobius HerbAutoDestroyTime = 60 s.</summary>
    public const long HerbAutoDestroyMs = 60_000;
    /// <summary>Mobius Attackable.dropItem randDropLim: loot lands within ±70 of the corpse.</summary>
    public const int DeathDropScatter = 70;

    readonly HashSet<int> _expiringGround = [];

    static bool IsHerb(int itemId) => itemId is >= 8600 and <= 8614 or >= 8952 and <= 8954;

    bool CanPickupProtected(ZoneEntity player, ZoneEntity ground, long now)
    {
        if (ground.GroundOwnerId == 0 || now >= ground.GroundProtectUntilMs || ground.GroundOwnerId == player.ObjectId)
            return true;
        return _partyByPlayer.TryGetValue(ground.GroundOwnerId, out int ownerParty)
            && _partyByPlayer.TryGetValue(player.ObjectId, out int myParty)
            && ownerParty == myParty;
    }

    /// <summary>Test hook: roll + drop death loot for <paramref name="npcId"/> as if <paramref name="killer"/> killed it here.</summary>
    internal void DropDeathLootForTest(int npcId, int x, int y, int z, ZoneEntity killer)
    {
        var outbound = new List<Pending>();
        lock (_gate)
            SpawnDeathDrops(npcId, x, y, z, killer, [killer], outbound);
    }

    void TickGroundExpiry(long now, List<Pending> outbound)
    {
        if (_expiringGround.Count == 0)
            return;
        foreach (int id in _expiringGround.ToList())
        {
            if (!_byId.TryGetValue(id, out var g) || g.Kind != ZoneEntityKind.GroundItem)
            {
                _expiringGround.Remove(id);
                continue;
            }
            if (g.GroundExpireAtMs > 0 && now >= g.GroundExpireAtMs)
            {
                ZoneLog.Info($"ground expire object={id} item={g.GroundItemId}");
                RemoveGroundItem(g, outbound);
            }
        }
    }

    string? TryPickup(ZoneSession session, int groundObjectId, List<Pending> outbound, bool allowApproach = true)
    {
        if (!_byId.TryGetValue(session.ObjectId, out var player) || player.Inventory == null)
            return "world";
        if (groundObjectId <= 0)
            return "bad_request";
        if (!_byId.TryGetValue(groundObjectId, out var ground) || ground.Kind != ZoneEntityKind.GroundItem)
            return "pickup";
        if (!VisibleTo(player.ObjectId, groundObjectId))
            return "pickup";
        if (player.Dead)
            return InventoryProtocol.Dead;

        long now = _clock().ToUnixTimeMilliseconds();
        if (!CanPickupProtected(player, ground, now))
        {
            // Mobius DropProtection.tryPickUp false -> FAILED_TO_PICKUP_S1, no run-in.
            PickupFailed(session, ground, outbound);
            return "protected";
        }
        AdvanceMove(player, now);
        if (!ZoneCombat.InPickupRange(player.X, player.Y, ground.X, ground.Y))
        {
            if (!allowApproach)
            {
                PickupFailed(session, ground, outbound);
                return "range";
            }

            // Mobius AI_INTENTION_PICK_UP: run to the item (moveToPawn), pick it up on arrival.
            session.AutoAttackTargetId = 0;
            session.PendingCastSkillId = 0;
            long ms = ApproachTarget(player, ground, ZoneCombat.PickupReach, outbound);
            if (ms < 0)
            {
                PickupFailed(session, ground, outbound);
                return "range";
            }
            if (ms > 0)
            {
                session.PendingPickupId = groundObjectId;
                session.PendingPickupAtUnixMs = now + ms;
                return null;
            }
        }

        session.PendingPickupId = 0;
        session.PendingPickupAtUnixMs = 0;
        session.ClearApproach();

        _game.Items.TryGet(ground.GroundItemId, out var template);
        // Stack merge must be a Modify (Mobius InventoryUpdate addModifiedItem): the client ignores an Add
        // for an objectId it already holds, so the picked-up count only showed after relog.
        bool merged = ground.GroundStackable && player.Inventory.FindByItemId(ground.GroundItemId) != null;
        InventoryUpdateMsg invUpdate;
        if (ground.GroundStackable || ground.GroundCount <= 1)
        {
            var added = player.Inventory.Add(ground.GroundItemId, ground.GroundCount);
            invUpdate = InventoryMessages.Update(merged ? InventoryChange.Modify : InventoryChange.Add, added);
        }
        else
        {
            var pieces = player.Inventory.AddPieces(ground.GroundItemId, ground.GroundCount);
            invUpdate = new InventoryUpdateMsg { Changes = pieces.Select(p => InventoryMessages.Update(InventoryChange.Add, p).Changes[0]).ToArray() };
        }
        outbound.Add(Pack(session, MsgType.InventoryUpdate, invUpdate));
        if (NetBridge != null)
            NetBridge.FanoutInventoryUpdate([session.ObjectId], ToNetInventoryUpdate(invUpdate));
        RemoveGroundItem(ground, outbound);
        SaveInventory(session, player.Inventory);

        string name = template?.Name ?? $"item {ground.GroundItemId}";
        if (ground.GroundItemId == 57)
            ShotNotice(session, SysMsgObtainedAdena, $"You have obtained {ground.GroundCount} adena.", outbound);
        else if (ground.GroundCount > 1)
            ShotNotice(session, SysMsgObtainedCount, $"You have obtained {ground.GroundCount} {name}.", outbound);
        else
            ShotNotice(session, SysMsgObtainedItem, $"You have obtained {name}.", outbound);
        ZoneLog.Info($"pickup object={session.ObjectId} ground={groundObjectId} item={ground.GroundItemId} count={ground.GroundCount} merged={merged}");
        return null;
    }

    void PickupFailed(ZoneSession session, ZoneEntity ground, List<Pending> outbound)
    {
        session.PendingPickupId = 0;
        session.PendingPickupAtUnixMs = 0;
        string name = _game.Items.TryGet(ground.GroundItemId, out var t) ? t.Name : $"item {ground.GroundItemId}";
        ShotNotice(session, SysMsgFailedToPickup, $"You have failed to pick up {name}.", outbound);
    }

    void TickPendingPickups(long now, List<Pending> outbound)
    {
        foreach (var player in Players().ToList())
        {
            var session = player.Session;
            if (session == null || session.PendingPickupId <= 0 || now < session.PendingPickupAtUnixMs)
                continue;
            int id = session.PendingPickupId;
            session.PendingPickupId = 0;
            session.PendingPickupAtUnixMs = 0;
            // Item may be gone (someone else took it) — that is a silent no-op like Mobius.
            if (_byId.TryGetValue(id, out var g) && g.Kind == ZoneEntityKind.GroundItem)
                TryPickup(session, id, outbound, allowApproach: false);
        }
    }

    string? TryDropItem(ZoneSession session, DropItemRequestMsg request, List<Pending> outbound)
    {
        if (!_byId.TryGetValue(session.ObjectId, out var player) || player.Inventory == null)
            return "world";
        if (player.Dead)
            return InventoryProtocol.Dead;
        if (request.ObjectId <= 0 || request.Count < 1)
            return "bad_request";

        var item = player.Inventory.Find(request.ObjectId);
        if (item == null || item.IsEquipped)
            return InventoryProtocol.NotOwned;
        if (!item.Template.CanDropTradeStore)
            return "bad_request"; // Mobius: quest items are not dropable
        if (item.Count < request.Count)
            return "bad_request";

        bool useCoords = request.X != 0 || request.Y != 0 || request.Z != 0;
        int dropX = useCoords ? request.X : player.X;
        int dropY = useCoords ? request.Y : player.Y;
        int dropZ = useCoords ? request.Z : player.Z;
        AdvanceMove(player, _clock().ToUnixTimeMilliseconds());
        if (useCoords && !ZoneCombat.InDropRange(player.X, player.Y, player.Z, dropX, dropY, dropZ))
        {
            // Mobius: CANNOT_DISCARD_DISTANCE_TOO_FAR, item stays. Re-send the row so a client that
            // hid the slot on drag puts it back without a relog.
            ShotNotice(session, SysMsgDiscardTooFar, "That is too far from you to discard.", outbound);
            var keep = InventoryMessages.Update(InventoryChange.Modify, item);
            outbound.Add(Pack(session, MsgType.InventoryUpdate, keep));
            NetBridge?.FanoutInventoryUpdate([session.ObjectId], ToNetInventoryUpdate(keep));
            ZoneLog.Info($"drop-bag refused far object={session.ObjectId} item={item.ItemId} player={player.X},{player.Y},{player.Z} at={dropX},{dropY},{dropZ}");
            return "range";
        }

        int itemId = item.ItemId;
        if (!player.Inventory.TryReduce(item, request.Count, out bool removedEntirely))
            return "bad_request";

        InventoryUpdateMsg invUpdate;
        if (removedEntirely)
        {
            invUpdate = new InventoryUpdateMsg { Changes = [InventoryMessages.RemoveRow(item, -1)] };
            outbound.Add(Pack(session, MsgType.InventoryUpdate, invUpdate));
        }
        else
        {
            invUpdate = InventoryMessages.Update(InventoryChange.Modify, item);
            outbound.Add(Pack(session, MsgType.InventoryUpdate, invUpdate));
        }

        if (NetBridge != null)
            NetBridge.FanoutInventoryUpdate([session.ObjectId], ToNetInventoryUpdate(invUpdate));

        SaveInventory(session, player.Inventory);

        var watchers = new List<ZoneEntity> { player };
        foreach (var other in Players())
        {
            if (ReferenceEquals(other, player))
                continue;
            if (other.Knownlist.Contains(player.ObjectId) || VisibleTo(other.ObjectId, player.ObjectId))
                watchers.Add(other);
        }

        SpawnGroundStack(
            itemId,
            request.Count,
            dropX,
            dropY,
            dropZ,
            player.InstanceId,
            watchers,
            outbound,
            dropperObjectId: player.ObjectId);
        ZoneLog.Info($"drop-bag object={session.ObjectId} item={itemId} count={request.Count} @ {dropX},{dropY},{dropZ}");
        return null;
    }

    /// <summary>Death loot onto the corpse tile — SpawnItem / DropItem fan-out for pickup + FX.</summary>
    void SpawnDeathDrops(
        int npcId,
        int x,
        int y,
        int z,
        ZoneEntity? killer,
        IReadOnlyList<ZoneEntity> watchers,
        List<Pending> outbound)
    {
        if (!_dropCatalog.TryGet(npcId, out var definition))
        {
            ZoneLog.Info($"drop miss npc={npcId} (no droplist)");
            return;
        }

        var rolls = DropRoller.RollDeath(definition, _dropRandom, _dropRates);
        if (rolls.Count == 0)
            ZoneLog.Info($"drop empty npc={npcId}");

        int instanceId = killer?.InstanceId
            ?? (watchers.Count > 0 ? watchers[0].InstanceId : 0);
        int dropperId = killer?.ObjectId ?? 0;
        int ownerId = killer?.Session != null ? killer.ObjectId : 0;
        long now = _clock().ToUnixTimeMilliseconds();

        foreach (var stack in rolls)
        {
            int dx = _dropRandom.Next(DeathDropScatter * 2 + 1) - DeathDropScatter;
            int dy = _dropRandom.Next(DeathDropScatter * 2 + 1) - DeathDropScatter;
            SpawnGroundStack(stack.ItemId, stack.Count, x + dx, y + dy, z, instanceId, watchers, outbound, dropperId,
                ownerObjectId: ownerId,
                expireAtMs: now + (IsHerb(stack.ItemId) ? HerbAutoDestroyMs : GroundItemAutoDestroyMs));
        }
    }

    void SpawnGroundStack(
        int itemId,
        int count,
        int x,
        int y,
        int z,
        int instanceId,
        IReadOnlyList<ZoneEntity> watchers,
        List<Pending> outbound,
        int dropperObjectId = 0,
        int ownerObjectId = 0,
        long expireAtMs = 0)
    {
        if (count <= 0)
            return;
        if (!_game.Items.TryGet(itemId, out var template))
        {
            ZoneLog.Info($"drop skip unknown item={itemId}");
            return;
        }

        int objectId = _itemIds.Next();
        var ground = new ZoneEntity
        {
            ObjectId = objectId,
            Kind = ZoneEntityKind.GroundItem,
            InstanceId = instanceId,
            X = x,
            Y = y,
            Z = z,
            GroundItemId = itemId,
            GroundCount = count,
            GroundStackable = template.Stackable,
            GroundOwnerId = ownerObjectId,
            GroundProtectUntilMs = ownerObjectId != 0 ? _clock().ToUnixTimeMilliseconds() + DropProtectMs : 0,
            GroundExpireAtMs = expireAtMs
        };
        _byId[objectId] = ground;
        if (expireAtMs > 0)
            _expiringGround.Add(objectId);
        _aoi.Enter(objectId, x, y);

        // DropItem carries the dropper so the client plays throw/land FX.
        // AOI enter for late observers still uses SpawnItem via AddPresence.
        byte[] body = MessagePackSerializer.Serialize(new DropItemMsg
        {
            CharObjectId = dropperObjectId,
            ObjectId = objectId,
            ItemId = itemId,
            X = x,
            Y = y,
            Z = z,
            Stackable = template.Stackable,
            Count = count
        });
        List<int>? netWatchers = null;
        foreach (var watcher in watchers)
        {
            if (watcher.Session == null)
                continue;
            watcher.Knownlist.Add(objectId);
            outbound.Add(new Pending(watcher.Session, MsgType.DropItem, body));
            netWatchers ??= [];
            netWatchers.Add(watcher.ObjectId);
        }

        if (netWatchers is { Count: > 0 } && NetBridge != null)
        {
            NetBridge.FanoutDropItem(netWatchers, new DropItemNetMsg
            {
                DropperObjectId = dropperObjectId,
                EntityValue = (ulong)(uint)objectId,
                EntityGeneration = 1,
                ItemId = itemId,
                X = x,
                Y = y,
                Z = z,
                Stackable = template.Stackable,
                Count = count
            });
        }

        ZoneLog.Info($"drop object={objectId} item={itemId} count={count} dropper={dropperObjectId} @ {x},{y},{z}");
    }

    void RemoveGroundItem(ZoneEntity ground, List<Pending> outbound)
    {
        int objectId = ground.ObjectId;
        _expiringGround.Remove(objectId);
        _aoi.Leave(objectId);
        _byId.Remove(objectId);

        byte[] deleteBody = MessagePackSerializer.Serialize(new DeleteObjectMsg { ObjectId = objectId });
        List<int>? netWatchers = null;
        foreach (var player in Players())
        {
            if (player.Session == null)
                continue;
            if (player.Knownlist.Remove(objectId))
            {
                outbound.Add(new Pending(player.Session, MsgType.DeleteObject, deleteBody));
                netWatchers ??= [];
                netWatchers.Add(player.ObjectId);
            }
        }

        if (netWatchers is { Count: > 0 } && NetBridge != null)
        {
            NetBridge.FanoutEntityDespawn(netWatchers, new EntityDespawnMsg
            {
                EntityValue = (ulong)(uint)objectId,
                EntityGeneration = 1
            });
        }
    }

    static InventoryUpdateNetMsg ToNetInventoryUpdate(InventoryUpdateMsg msg)
    {
        var src = msg.Changes ?? [];
        var rows = new InventoryUpdateEntryNet[src.Length];
        for (int i = 0; i < src.Length; i++)
        {
            var row = src[i];
            rows[i] = new InventoryUpdateEntryNet
            {
                Change = row.Change,
                ObjectId = row.ObjectId,
                ItemId = row.ItemId,
                Count = row.Count,
                EnchantLevel = row.EnchantLevel,
                EquippedSlot = row.EquippedSlot
            };
        }

        return new InventoryUpdateNetMsg { Changes = rows };
    }
}
