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

        // World mutate under _gate; Postgres inventory write after unlock (B8).
        return Task.FromResult(WithDeferredInventoryPersist(() => TryPickup(session, groundObjectId)));
    }

    /// <summary>
    /// Mobius <c>RequestDestroyItem</c>: the bag slot is deleted outright, no ground spawn. Except for the
    /// adena/quest guards the client could bypass, this is the trash-basket action.
    /// </summary>
    public Task<string?> TryDestroyItemAsync(ZoneSession session, int objectId, int count, CancellationToken ct)
    {
        if (!IsLive(session))
            return Task.FromResult<string?>("world");

        return Task.FromResult(WithDeferredInventoryPersist(() => TryDestroyItem(session, objectId, count)));
    }

    public Task<string?> TryDropItemAsync(
        ZoneSession session, int objectId, int count, int x, int y, int z, CancellationToken ct)
    {
        if (!IsLive(session))
            return Task.FromResult<string?>("world");

        return Task.FromResult(WithDeferredInventoryPersist(
            () => TryDropItem(session, objectId, count, x, y, z)));
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

}
