using Fineage.Game.Services;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Drops;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    public Task<string?> TryPickupAsync(ZoneSession session, int groundObjectId, CancellationToken ct)
    {
        if (!IsLive(session))
            return Task.FromResult<string?>("world");

        string? error;
        lock (_gate)
            error = TryPickup(session, groundObjectId);
        if (error != null)
            return Task.FromResult<string?>(error);

        return Task.FromResult<string?>(null);
    }

    /// <summary>
    /// Mobius <c>RequestDestroyItem</c>: the bag slot is deleted outright, no ground spawn. Except for the
    /// adena/quest guards the client could bypass, this is the trash-basket action.
    /// </summary>
    public Task<string?> TryDestroyItemAsync(ZoneSession session, int objectId, int count, CancellationToken ct)
    {
        if (!IsLive(session))
            return Task.FromResult<string?>("world");

        string? error;
        lock (_gate)
            error = TryDestroyItem(session, objectId, count);
        if (error != null)
            return Task.FromResult<string?>(error);

        return Task.FromResult<string?>(null);
    }

    public Task<string?> TryDropItemAsync(
        ZoneSession session, int objectId, int count, int x, int y, int z, CancellationToken ct)
    {
        if (!IsLive(session))
            return Task.FromResult<string?>("world");

        string? error;
        lock (_gate)
            error = TryDropItem(session, objectId, count, x, y, z);
        if (error != null)
            return Task.FromResult<string?>(error);

        return Task.FromResult<string?>(null);
    }

    /// <summary>Interlude SystemMessageId 151 CANNOT_DISCARD_DISTANCE_TOO_FAR.</summary>
    public const int SysMsgDiscardTooFar = 151;
    /// <summary>Interlude SystemMessageId 28/29/30: obtained adena / S2 S1 / S1.</summary>
    public const int SysMsgObtainedAdena = 28;
    public const int SysMsgObtainedCount = 29;
    public const int SysMsgObtainedItem = 30;
    /// <summary>Interlude SystemMessageId 145 FAILED_TO_PICKUP_S1.</summary>
    public const int SysMsgFailedToPickup = 145;
    /// <summary>Mobius SystemMessageId 129 YOUR_INVENTORY_IS_FULL.</summary>
    public const int SysMsgInventoryFull = 129;
    /// <summary>Mobius SystemMessageId 422 YOU_HAVE_EXCEEDED_THE_WEIGHT_LIMIT.</summary>
    public const int SysMsgWeightLimit = 422;
    /// <summary>Mobius DropProtection.PROTECTED_MILLIS: killer-only pickup window.</summary>
    public const long DropProtectMs = 15_000;
    /// <summary>Mobius General AutoDestroyDroppedItemAfter = 600 s (mob loot; player discards are kept, DestroyPlayerDroppedItem = false).</summary>
    public const long GroundItemAutoDestroyMs = 600_000;
    /// <summary>Mobius HerbAutoDestroyTime = 60 s.</summary>
    public const long HerbAutoDestroyMs = 60_000;
    /// <summary>Mobius Attackable.dropItem randDropLim: loot lands within ±70 of the corpse.</summary>
    public const int DeathDropScatter = 70;

    readonly HashSet<int> _expiringGround = [];


    bool CanPickupProtected(ZoneEntity player, ZoneEntity ground, long now)
    {
        bool sameParty = _partyByPlayer.TryGetValue(ground.GroundOwnerId, out int ownerParty)
            && _partyByPlayer.TryGetValue(player.ObjectId, out int myParty)
            && ownerParty == myParty;
        return GroundRules.CanPickupProtected(
            ground.GroundOwnerId,
            ground.GroundProtectUntilMs,
            now,
            player.ObjectId,
            sameParty);
    }

    /// <summary>
    /// Mobius <c>PlayerInventory.validateCapacity(Item)</c> (<c>PlayerInventory.java:834-842</c>): a stackable item that
    /// is already carried takes no new slot, everything else needs one. Herbs are excluded because Mobius consumes them
    /// on the spot (<c>hasExImmediateEffect</c>) and never stores them.
    /// </summary>
    bool HasRoomForGroundItem(ZoneEntity player, ZoneEntity ground)
    {
        if (player.Inventory == null)
            return true;
        bool alreadyCarried = player.Inventory.FindByItemId(ground.GroundItemId) != null;
        return GroundRules.HasRoomForGroundItem(
            IsHerb(ground.GroundItemId),
            player.Inventory.Items.Count,
            player.Race,
            ground.GroundStackable,
            alreadyCarried,
            ground.GroundCount);
    }

    /// <summary>Test hook: roll + drop death loot for <paramref name="npcId"/> as if <paramref name="killer"/> killed it here.</summary>
    internal void DropDeathLootForTest(int npcId, int x, int y, int z, ZoneEntity killer)
    {
        lock (_gate)
            SpawnDeathDrops(npcId, x, y, z, killer, [killer]);
    }

    void TickGroundExpiry(long now)
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
                RemoveGroundItem(g);
            }
        }
    }

    string? TryPickup(ZoneSession session, int groundObjectId, bool allowApproach = true)
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
            PickupFailed(session, ground);
            return "protected";
        }
        // Mobius Player.doPickupItem: after drop protection and before the run-in, RequestPickupItem calls
        // Inventory.validateCapacity(target) and answers YOUR_INVENTORY_IS_FULL. Mobius deliberately does *not*
        // weight-check a pickup (validateWeight is only on buy / withdraw / trade), so an overloaded character may
        // still loot and simply carries the weight penalty — matching the reference means slots-only here.
        // Party RANDOM/BY_TURN modes check the eventual looter instead of the picker (Party.distributeItem).
        ZoneParty? pickupParty = null;
        if (_partyByPlayer.TryGetValue(player.ObjectId, out int pickupPartyId))
            _parties.TryGetValue(pickupPartyId, out pickupParty);
        bool partyDistributes = pickupParty != null
                                && pickupParty.ItemDistribution != (int)PartyDistributionType.FindersKeepers
                                && !IsHerb(ground.GroundItemId);
        if (!partyDistributes && !HasRoomForGroundItem(player, ground))
        {
            session.PendingPickupId = 0;
            session.PendingPickupAtUnixMs = 0;
            ShotNotice(session, SysMsgInventoryFull, "Your inventory is full.");
            return "slots";
        }
        AdvanceMove(player, now);
        if (!ZoneCombat.InPickupRange(player.X, player.Y, ground.X, ground.Y))
        {
            if (!allowApproach)
            {
                PickupFailed(session, ground);
                return "range";
            }

            // Mobius AI_INTENTION_PICK_UP: run to the item (moveToPawn), pick it up on arrival.
            session.AutoAttackTargetId = 0;
            session.PendingCastSkillId = 0;
            long ms = ApproachTarget(player, ground, ZoneCombat.PickupReach);
            if (ms < 0)
            {
                PickupFailed(session, ground);
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

        if (IsHerb(ground.GroundItemId))
        {
            UseHerbOnPickup(session, player, ground);
            return null;
        }

        // Mobius Party.distributeItem(spoil=false) / distributeAdena when the picker is in a party.
        if (pickupParty != null)
        {
            if (ground.GroundItemId == WarehouseRules.AdenaItemId)
            {
                DistributePickupAdena(pickupParty, player, ground);
                return null;
            }

            int last = pickupParty.ItemLastLoot;
            int looterId = PartyLootRules.ActualLooter(
                player.ObjectId,
                (PartyDistributionType)pickupParty.ItemDistribution,
                spoil: false,
                pickupParty.MemberIds,
                id => CanReceiveGround(id, ground),
                ref last,
                _combatRandom);
            pickupParty.ItemLastLoot = last;
            if (!_byId.TryGetValue(looterId, out var looter)
                || looter.Kind != ZoneEntityKind.Player
                || looter.Inventory == null
                || looter.Session == null
                || !CanReceiveGround(looterId, ground))
            {
                session.PendingPickupId = 0;
                session.PendingPickupAtUnixMs = 0;
                ShotNotice(session, SysMsgInventoryFull, "Your inventory is full.");
                return "slots";
            }

            GrantGroundPickup(looter, ground);
            ZoneLog.Info(
                $"pickup object={session.ObjectId} looter={looter.ObjectId} ground={groundObjectId} item={ground.GroundItemId} count={ground.GroundCount} party=1");
            return null;
        }

        GrantGroundPickup(player, ground);
        ZoneLog.Info($"pickup object={session.ObjectId} ground={groundObjectId} item={ground.GroundItemId} count={ground.GroundCount} party=0");
        return null;
    }

    /// <summary>Mobius <c>Party.distributeAdena</c> on ground pickup: equal share to in-range living members.</summary>
    void DistributePickupAdena(ZoneParty party, ZoneEntity picker, ZoneEntity ground)
    {
        var inRange = new List<ZoneEntity>();
        foreach (int id in party.MemberIds)
        {
            if (!_byId.TryGetValue(id, out var m) || m.Kind != ZoneEntityKind.Player || m.Inventory == null || m.Dead)
                continue;
            if (!ZoneCombat.Within(ground.X, ground.Y, m.X, m.Y, PartyLootRules.PartyRange))
                continue;
            inRange.Add(m);
        }

        int adena = ground.GroundCount;
        RemoveGroundItem(ground);
        if (inRange.Count == 0)
        {
            if (picker.Inventory != null && picker.Session != null)
                GrantInventoryStack(picker, WarehouseRules.AdenaItemId, adena, notifyObtained: true);
            return;
        }

        int share = PartyLootRules.AdenaShare(adena, inRange.Count);
        if (share <= 0)
            return;
        foreach (var m in inRange)
            GrantInventoryStack(m, WarehouseRules.AdenaItemId, share, notifyObtained: true);
    }

    bool CanReceiveGround(int objectId, ZoneEntity ground)
    {
        if (!_byId.TryGetValue(objectId, out var m) || m.Kind != ZoneEntityKind.Player || m.Inventory == null || m.Dead)
            return false;
        if (!ZoneCombat.Within(ground.X, ground.Y, m.X, m.Y, PartyLootRules.PartyRange))
            return false;
        return HasRoomForGroundItem(m, ground);
    }

    /// <summary>Move one ground stack into <paramref name="looter"/>'s bag and despawn the world item.</summary>
    void GrantGroundPickup(ZoneEntity looter, ZoneEntity ground)
    {
        if (looter.Inventory == null || looter.Session == null)
            return;
        int itemId = ground.GroundItemId;
        int count = ground.GroundCount;
        bool stackable = ground.GroundStackable;
        // Stack merge must be a Modify (Mobius InventoryUpdate addModifiedItem): the client ignores an Add
        // for an objectId it already holds, so the picked-up count only showed after relog.
        bool merged = stackable && looter.Inventory.FindByItemId(itemId) != null;
        InventoryUpdateMsg invUpdate;
        if (stackable || count <= 1)
        {
            var added = looter.Inventory.Add(itemId, count);
            invUpdate = InventoryMessages.Update(merged ? InventoryChange.Modify : InventoryChange.Add, added);
        }
        else
        {
            var pieces = looter.Inventory.AddPieces(itemId, count);
            invUpdate = new InventoryUpdateMsg
            {
                Changes = pieces.Select(p => InventoryMessages.Update(InventoryChange.Add, p).Changes[0]).ToArray()
            };
        }

        NetBridge?.FanoutInventoryUpdate([looter.Session.ObjectId], invUpdate);
        RemoveGroundItem(ground);
        SaveInventory(looter.Session, looter.Inventory);
        NotifyObtained(looter.Session, itemId, count);
    }

    void GrantInventoryStack(ZoneEntity looter, int itemId, int count, bool notifyObtained)
    {
        if (looter.Inventory == null || looter.Session == null || count <= 0)
            return;
        bool merged = looter.Inventory.FindByItemId(itemId) != null;
        var added = looter.Inventory.Add(itemId, count);
        var invUpdate = InventoryMessages.Update(merged ? InventoryChange.Modify : InventoryChange.Add, added);
        NetBridge?.FanoutInventoryUpdate([looter.Session.ObjectId], invUpdate);
        SaveInventory(looter.Session, looter.Inventory);
        if (notifyObtained)
            NotifyObtained(looter.Session, itemId, count);
    }

    void NotifyObtained(ZoneSession session, int itemId, int count)
    {
        _game.Items.TryGet(itemId, out var template);
        string name = template?.Name ?? $"item {itemId}";
        if (itemId == WarehouseRules.AdenaItemId)
            ShotNotice(session, SysMsgObtainedAdena, $"You have obtained {count} adena.");
        else if (count > 1)
            ShotNotice(session, SysMsgObtainedCount, $"You have obtained {count} {name}.");
        else
            ShotNotice(session, SysMsgObtainedItem, $"You have obtained {name}.");
    }

    void PickupFailed(ZoneSession session, ZoneEntity ground)
    {
        session.PendingPickupId = 0;
        session.PendingPickupAtUnixMs = 0;
        string name = _game.Items.TryGet(ground.GroundItemId, out var t) ? t.Name : $"item {ground.GroundItemId}";
        ShotNotice(session, SysMsgFailedToPickup, $"You have failed to pick up {name}.");
    }

    void TickPendingPickups(long now)
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
                TryPickup(session, id, allowApproach: false);
        }
    }

    /// <summary>
    /// Mobius <c>RequestDestroyItem</c> body. It shares the drop path's guards (owner, not equipped, not a
    /// quest item, count available) but never spawns a ground stack and never checks distance.
    /// </summary>
    string? TryDestroyItem(ZoneSession session, int objectId, int count)
    {
        if (!_byId.TryGetValue(session.ObjectId, out var player) || player.Inventory == null)
            return "world";
        if (player.Dead)
            return InventoryProtocol.Dead;
        if (objectId <= 0 || count < 1)
            return "bad_request";

        var item = player.Inventory.Find(objectId);
        if (item == null || item.IsEquipped)
            return InventoryProtocol.NotOwned;
        if (!item.Template.CanDropTradeStore)
            return "bad_request"; // Mobius: quest items cannot be destroyed either
        if (item.Count < count)
            return "bad_request";

        if (!player.Inventory.TryReduce(item, count, out bool removedEntirely))
            return "bad_request";

        var invUpdate = removedEntirely
            ? new InventoryUpdateMsg { Changes = [InventoryMessages.RemoveRow(item, -1)] }
            : InventoryMessages.Update(InventoryChange.Modify, item);
        NetBridge?.FanoutInventoryUpdate([session.ObjectId], invUpdate);
        SaveInventory(session, player.Inventory);
        ZoneLog.Info($"destroy-bag object={session.ObjectId} item={item.ItemId} x{count} all={removedEntirely}");
        return null;
    }

    string? TryDropItem(ZoneSession session, int objectId, int count, int x, int y, int z)
    {
        if (!_byId.TryGetValue(session.ObjectId, out var player) || player.Inventory == null)
            return "world";
        if (player.Dead)
            return InventoryProtocol.Dead;
        if (objectId <= 0 || count < 1)
            return "bad_request";

        var item = player.Inventory.Find(objectId);
        if (item == null || item.IsEquipped)
            return InventoryProtocol.NotOwned;
        if (!item.Template.CanDropTradeStore)
            return "bad_request"; // Mobius: quest items are not dropable
        if (item.Count < count)
            return "bad_request";

        bool useCoords = x != 0 || y != 0 || z != 0;
        int dropX = useCoords ? x : player.X;
        int dropY = useCoords ? y : player.Y;
        int dropZ = useCoords ? z : player.Z;
        AdvanceMove(player, _clock().ToUnixTimeMilliseconds());
        if (useCoords && !ZoneCombat.InDropRange(player.X, player.Y, player.Z, dropX, dropY, dropZ))
        {
            // Mobius: CANNOT_DISCARD_DISTANCE_TOO_FAR, item stays. Re-send the row so a client that
            // hid the slot on drag puts it back without a relog.
            ShotNotice(session, SysMsgDiscardTooFar, "That is too far from you to discard.");
            var keep = InventoryMessages.Update(InventoryChange.Modify, item);
            NetBridge?.FanoutInventoryUpdate([session.ObjectId], keep);
            ZoneLog.Info($"drop-bag refused far object={session.ObjectId} item={item.ItemId} player={player.X},{player.Y},{player.Z} at={dropX},{dropY},{dropZ}");
            return "range";
        }

        int itemId = item.ItemId;
        if (!player.Inventory.TryReduce(item, count, out bool removedEntirely))
            return "bad_request";

        InventoryUpdateMsg invUpdate;
        if (removedEntirely)
        {
            invUpdate = new InventoryUpdateMsg { Changes = [InventoryMessages.RemoveRow(item, -1)] };
        }
        else
        {
            invUpdate = InventoryMessages.Update(InventoryChange.Modify, item);
        }

        if (NetBridge != null)
            NetBridge.FanoutInventoryUpdate([session.ObjectId], invUpdate);

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
            count,
            dropX,
            dropY,
            dropZ,
            player.InstanceId,
            watchers,
            dropperObjectId: player.ObjectId);
        ZoneLog.Info($"drop-bag object={session.ObjectId} item={itemId} count={count} @ {dropX},{dropY},{dropZ}");
        return null;
    }

    /// <summary>Death loot onto the corpse tile — SpawnItem / DropItem fan-out for pickup + FX.</summary>
    void SpawnDeathDrops(
        int npcId,
        int x,
        int y,
        int z,
        ZoneEntity? killer,
        IReadOnlyList<ZoneEntity> watchers)
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
            SpawnGroundStack(stack.ItemId, stack.Count, x + dx, y + dy, z, instanceId, watchers, dropperId,
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
        List<int>? netWatchers = null;
        foreach (var watcher in watchers)
        {
            if (watcher.Session == null)
                continue;
            watcher.Knownlist.Add(objectId);
            netWatchers ??= [];
            netWatchers.Add(watcher.ObjectId);
        }

        if (netWatchers is { Count: > 0 } && NetBridge != null)
        {
            NetBridge.FanoutDropItem(netWatchers, new DropItemMsg
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
        }

        ZoneLog.Info($"drop object={objectId} item={itemId} count={count} dropper={dropperObjectId} @ {x},{y},{z}");
    }

    void RemoveGroundItem(ZoneEntity ground)
    {
        int objectId = ground.ObjectId;
        _expiringGround.Remove(objectId);
        _aoi.Leave(objectId);
        _byId.Remove(objectId);

        List<int>? netWatchers = null;
        foreach (var player in Players())
        {
            if (player.Session == null)
                continue;
            if (player.Knownlist.Remove(objectId))
            {
                netWatchers ??= [];
                netWatchers.Add(player.ObjectId);
            }
        }

        if (netWatchers is { Count: > 0 } && NetBridge != null)
        {
            NetBridge.FanoutEntityDespawn(netWatchers, objectId);
        }
    }

}
