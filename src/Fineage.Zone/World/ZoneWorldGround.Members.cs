using Fineage.Game;
using Fineage.Game.Economy;
using Fineage.Game.Services;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Drops;
using Fineage.Zone.Party;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    void GrantInventoryStack(ZoneEntity looter, int itemId, int count, bool notifyObtained)
    {
        if (looter.Inventory == null || looter.Session == null || count <= 0)
            return;
        looter.Inventory.Add(itemId, count);
        SaveInventory(looter.Session, looter.Inventory);
        if (notifyObtained)
            NotifyObtained(looter.Session, itemId, count);
    }

    void NotifyObtained(ZoneSession session, int itemId, int count)
    {
        string name = _game.Items.Require(itemId).Name;
        if (itemId == WarehouseRules.AdenaItemId)
            ShotNotice(session, SysMsgObtainedAdena, $"You have obtained {count} adena.");
        else if (count > 1)
            ShotNotice(session, SysMsgObtainedCount, $"You have obtained {count} {name}.");
        else
            ShotNotice(session, SysMsgObtainedItem, $"You have obtained {name}.");
    }

    void PickupFailed(ZoneSession session, ZoneEntity ground)
    {
        if (_byId.TryGetValue(session.ObjectId, out var player))
        {
            player.Intent.PendingPickupId = 0;
            player.Intent.PendingPickupAtUnixMs = 0;
        }
        string name = _game.Items.Require(ground.GroundItemId).Name;
        ShotNotice(session, SysMsgFailedToPickup, $"You have failed to pick up {name}.");
    }

    void TickPendingPickups(long now)
    {
        foreach (var player in Players().ToList())
        {
            var session = player.Session;
            if (session == null || player.Intent.PendingPickupId <= 0 || now < player.Intent.PendingPickupAtUnixMs)
                continue;
            int id = player.Intent.PendingPickupId;
            player.Intent.PendingPickupId = 0;
            player.Intent.PendingPickupAtUnixMs = 0;
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
        if (item.IsAugmented)
        {
            ShotNotice(session, SysMsgAugmentedCannotDiscard, "The augmented item cannot be discarded.");
            return "augmented";
        }
        if (!item.Template.CanDropTradeStore)
            return "bad_request"; // Mobius: quest items cannot be destroyed either
        if (item.Count < count)
            return "bad_request";

        var bag = LedgerAccount.Inventory(player.ObjectId);
        var move = BeginLedgerMove(LedgerTxKind.Destroy, player.ObjectId, 0);
        ObserveLedger(move, bag);
        if (!player.Inventory.TryReduce(item, count, out bool removedEntirely))
            return "bad_request";
        move.Post(bag, item.ItemId, -count, PiecesOf([item]));
        move.Post(LedgerAccount.Of(SystemAccount.Destroyed), item.ItemId, count);
        move.Commit(LedgerHoldingsOf);
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
        if (player.Fishing)
        {
            // Mobius RequestDropItem: no dropping while fishing.
            SelfLine(session, FishingProtocol.CannotWhileFishingDrop, "You cannot do that while fishing.");
            return FishingProtocol.Busy;
        }

        var item = player.Inventory.Find(objectId);
        if (item == null || item.IsEquipped)
            return InventoryProtocol.NotOwned;
        if (item.IsAugmented)
        {
            ShotNotice(session, SysMsgAugmentedCannotDiscard, "The augmented item cannot be discarded.");
            return "augmented";
        }
        if (!item.Template.CanDropTradeStore)
            return "bad_request"; // Mobius: quest items are not dropable
        if (item.Count < count)
            return "bad_request";

        bool useCoords = x != 0 || y != 0 || z != 0;
        int dropX = useCoords ? x : player.X;
        int dropY = useCoords ? y : player.Y;
        int dropZ = useCoords ? z : player.Z;
        AdvanceMove(player, NowUnixMs());
        ReconcileKnownlistAfterCommandAdvance(player);
        if (useCoords && !ZoneCombat.InDropRange(player.X, player.Y, player.Z, dropX, dropY, dropZ))
        {
            // Mobius: CANNOT_DISCARD_DISTANCE_TOO_FAR, item stays. Re-send the row so a client that
            // hid the slot on drag puts it back without a relog.
            ShotNotice(session, SysMsgDiscardTooFar, "That is too far from you to discard.");
            bool pending = player.Inventory.HasPendingChanges;
            player.Inventory.NoteRefresh(item);
            if (!pending)
                PublishCommittedInventory(session, player.Inventory);
            ZoneLog.Info($"drop-bag refused far object={session.ObjectId} item={item.ItemId} player={player.X},{player.Y},{player.Z} at={dropX},{dropY},{dropZ}");
            return "range";
        }

        int itemId = item.ItemId;
        var bag = LedgerAccount.Inventory(player.ObjectId);
        var move = BeginLedgerMove(LedgerTxKind.Drop, player.ObjectId, 0);
        ObserveLedger(move, bag);
        IReadOnlyList<int> pieces = item.Template.Stackable ? [] : [item.ObjectId];
        int groundId = item.Template.Stackable ? _itemIds.Next() : item.ObjectId;
        PostOntoGroundPlanned(move, bag, groundId, itemId, count, pieces);
        if (move.CheckCommit() != LedgerRejection.None)
        {
            ZoneLog.Warn($"drop refused for object {session.ObjectId}: ledger rejected instance {groundId}");
            return "ledger";
        }

        if (!player.Inventory.TryReduce(item, count, out bool removedEntirely))
            return "bad_request";

        var watchers = new List<ZoneEntity> { player };
        foreach (var other in Players())
        {
            if (ReferenceEquals(other, player))
                continue;
            if (other.Knownlist.Contains(player.ObjectId) || VisibleTo(other.ObjectId, player.ObjectId))
                watchers.Add(other);
        }

        var ground = SpawnGroundStack(
            itemId,
            count,
            dropX,
            dropY,
            dropZ,
            player.InstanceId,
            watchers,
            dropperObjectId: player.ObjectId,
            piece: item.Template.Stackable ? null : item,
            reserveObjectId: item.Template.Stackable ? groundId : null);
        if (ground == null)
        {
            if (removedEntirely)
                player.Inventory.Reinsert(item, count);
            else
                player.Inventory.RestoreCount(item, item.Count + count);
            player.Inventory.DiscardChanges();
            ZoneLog.Warn(
                $"drop aborted: ground spawn failed char={session.ObjectId} item={itemId} objectId={objectId} count={count}");
            return "bad_request";
        }

        move.Commit(LedgerHoldingsOf);
        SaveInventory(session, player.Inventory);
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
        IReadOnlyList<ZoneEntity> watchers,
        int victimLevel = 0,
        bool raid = false,
        bool seeded = false,
        int lootOwnerObjectId = 0)
    {
        if (!_dropCatalog.TryGet(npcId, out var definition))
        {
            ZoneLog.Info($"drop miss npc={npcId} (no droplist)");
            return;
        }

        int instanceId = killer?.InstanceId ?? (watchers.Count > 0 ? watchers[0].InstanceId : 0);
        var dropRng = _dropRandom.For(_regions.RegionOf(instanceId, x, y));
        int killerLevel = killer?.Level ?? victimLevel;
        long now = NowUnixMs();
        var rolls = DropRoller.RollDeath(definition, dropRng, _dropRates, victimLevel, killerLevel, raid).ToList();
        if (killer != null && !seeded)
            rolls.AddRange(_eventDropCatalog.RollExtra(dropRng, _dropRates, now, victimLevel, killerLevel));
        // Mobius NpcTemplate.calculateDrops: a seeded monster drops only adena and seal stones (the crop replaces the rest).
        if (seeded)
            rolls = rolls.Where(s => s.ItemId is 57 or 6360 or 6361 or 6362 || IsHerb(s.ItemId)).ToList();
        if (rolls.Count == 0)
            ZoneLog.Info($"drop empty npc={npcId}");

        int dropperId = killer?.ObjectId ?? 0;
        int ownerId = lootOwnerObjectId != 0
            ? lootOwnerObjectId
            : killer?.Session != null
                ? killer.ObjectId
                : 0;
        if (rolls.Count == 0)
            return;

        var move = BeginLedgerMove(LedgerTxKind.Loot, dropperId, npcId);
        var source = LedgerAccount.Of(SystemAccount.DropSource);
        var planned = new List<(DropStack Stack, int Dx, int Dy, int GroundId)>();
        var autoLoot = new List<(ZoneEntity Looter, DropStack Stack)>();
        ZoneEntity? autoLooter = AutoLootRecipient(killer, ownerId);
        foreach (var stack in rolls)
        {
            var template = _game.Items.Require(stack.ItemId);
            bool herb = IsHerb(stack.ItemId);
            if (autoLooter != null
                && _rates.ShouldAutoLootItem(stack.ItemId, raid, herb)
                && HasRoomForLootStack(autoLooter, stack.ItemId, stack.Count, template.Stackable))
            {
                var bag = LedgerAccount.Inventory(autoLooter.ObjectId);
                ObserveLedger(move, bag);
                move.Post(source, stack.ItemId, -stack.Count);
                move.Post(bag, stack.ItemId, stack.Count);
                autoLoot.Add((autoLooter, stack));
                continue;
            }

            int dx = dropRng.Next(DeathDropScatter * 2 + 1) - DeathDropScatter;
            int dy = dropRng.Next(DeathDropScatter * 2 + 1) - DeathDropScatter;
            int groundId = _itemIds.Next();
            IReadOnlyList<int> pieces = template.Stackable ? [] : [groundId];
            PostOntoGroundPlanned(move, source, groundId, stack.ItemId, stack.Count, pieces);
            planned.Add((stack, dx, dy, groundId));
        }

        if (!move.HasPostings)
            return;

        if (move.CheckCommit() != LedgerRejection.None)
        {
            ZoneLog.Warn($"loot refused for npc {npcId}: ledger rejected ground postings");
            return;
        }

        foreach (var (looter, stack) in autoLoot)
            GrantInventoryStack(looter, stack.ItemId, stack.Count, notifyObtained: true);

        foreach (var (stack, dx, dy, groundId) in planned)
        {
            SpawnGroundStack(stack.ItemId, stack.Count, x + dx, y + dy, z, instanceId, watchers, dropperId,
                ownerObjectId: ownerId,
                expireAtMs: now + (IsHerb(stack.ItemId) ? HerbAutoDestroyMs : GroundItemAutoDestroyMs),
                reserveObjectId: groundId);
        }

        move.Commit(LedgerHoldingsOf);
    }

    ZoneEntity? AutoLootRecipient(ZoneEntity? killer, int lootOwnerObjectId)
    {
        if (killer?.Session == null || killer.Inventory == null)
            return null;
        if (lootOwnerObjectId != 0
            && lootOwnerObjectId != killer.ObjectId
            && _byId.TryGetValue(lootOwnerObjectId, out var owner)
            && owner.Session != null
            && owner.Inventory != null)
            return owner;
        return killer;
    }

    bool HasRoomForLootStack(ZoneEntity player, int itemId, int count, bool stackable) =>
        player.Inventory != null
        && GroundRules.HasRoomForGroundItem(
            IsHerb(itemId),
            player.Inventory.Items.Count,
            player.Race,
            stackable,
            player.Inventory.FindByItemId(itemId) != null,
            count);

    /// <summary>
    /// Places one stack on the ground and returns it, or null when there is nothing to place (no count, unknown item).
    /// <paramref name="piece"/> is a piece leaving a bag: the stack takes over its object id and enchant.
    /// </summary>
    ZoneEntity? SpawnGroundStack(
        int itemId,
        int count,
        int x,
        int y,
        int z,
        int instanceId,
        IReadOnlyList<ZoneEntity> watchers,
        int dropperObjectId = 0,
        int ownerObjectId = 0,
        long expireAtMs = 0,
        ItemInstance? piece = null,
        int? reserveObjectId = null)
    {
        if (count <= 0)
            return null;
        var template = _game.Items.Require(itemId);

        if (piece != null && (template.Stackable || count != 1 || piece.ItemId != itemId))
            throw new ArgumentException($"item {piece.ObjectId} is not one piece of {itemId}", nameof(piece));
        int objectId = reserveObjectId ?? piece?.ObjectId ?? _itemIds.Next();
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
            GroundEnchant = piece?.EnchantLevel ?? 0,
            GroundOwnerId = ownerObjectId,
            GroundProtectUntilMs = ownerObjectId != 0 ? NowUnixMs() + DropProtectMs : 0,
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
            TrackWatcher(watcher.ObjectId, objectId);
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
        return ground;
    }

    void RemoveGroundItem(ZoneEntity ground)
    {
        int objectId = ground.ObjectId;
        _expiringGround.Remove(objectId);
        _aoi.Leave(objectId);
        ground.DropNetWatcherAudience();
        _byId.Remove(objectId);

        List<int>? netWatchers = null;
        ForEachWatchingPlayer(objectId, player =>
        {
            if (player.Knownlist.Remove(objectId))
            {
                UntrackWatcher(player.ObjectId, objectId);
                netWatchers ??= [];
                netWatchers.Add(player.ObjectId);
            }
        });
        DropWatcherIndex(objectId);

        if (netWatchers is { Count: > 0 } && NetBridge != null)
        {
            NetBridge.FanoutEntityDespawn(netWatchers, objectId);
        }
    }

}
