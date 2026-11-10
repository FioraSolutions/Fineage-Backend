using Fineage.Game.Services;
using Fineage.Game.Skills;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Skills;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    public Task<string?> TryAdminSpoilAsync(ZoneSession session, AdminSpoilTargetMsg request, CancellationToken ct)
    {
        if (!IsLive(session))
            return Task.FromResult<string?>("world");

        string? error;
        lock (_gate)
            error = TryAdminSpoil(session, request.TargetObjectId);
        if (error != null)
            return Task.FromResult<string?>(error);

        return Task.FromResult<string?>(null);
    }

    public Task<string?> TryAdminSweepAsync(ZoneSession session, AdminSweepCorpseMsg request, CancellationToken ct)
    {
        if (!IsLive(session))
            return Task.FromResult<string?>("world");

        // Sweep grants items → SaveInventory after unlock (B8).
        return Task.FromResult(WithDeferredInventoryPersist(
            () => TryAdminSweep(session, request.TargetObjectId)));
    }

    readonly Fineage.Game.Services.SpoilRules _spoilRules = Fineage.Game.Services.SpoilRules.LoadDefault();

    internal Fineage.Game.Services.SpoilRules SpoilRules => _spoilRules;

    /// <summary>Admin hook: spoil without the magic roll (debug only, FINEAGE_ZONE_ADMIN).</summary>
    string? TryAdminSpoil(ZoneSession session, int targetObjectId)
    {
        if (!_byId.TryGetValue(session.ObjectId, out var player))
            return "world";
        if (targetObjectId <= 0)
            return "bad_request";
        if (!_byId.TryGetValue(targetObjectId, out var target) || !VisibleTo(session.ObjectId, targetObjectId))
            return "target";
        return ApplySpoil(session, player, target, forced: true, skillId: 254, level: 1) ? null : "target";
    }

    string? TryAdminSweep(ZoneSession session, int targetObjectId)
    {
        if (!_byId.TryGetValue(session.ObjectId, out var player) || player.Inventory == null)
            return "world";
        if (targetObjectId <= 0)
            return "bad_request";
        if (!_byId.TryGetValue(targetObjectId, out var target) || !VisibleTo(session.ObjectId, targetObjectId))
            return "sweep";
        return ApplySweep(session, player, target) ? null : "sweep";
    }

    /// <summary>
    /// Mobius Spoil effect (skill 254): monster alive, not spoiled (357), calcMagicSuccess (139 resisted) → spoiler set, SysMsg 612,
    /// EVT_ATTACKED (hate on the spoiler). Invalid target → 109.
    /// </summary>
    bool ApplySpoil(ZoneSession session, ZoneEntity player, ZoneEntity target, bool forced, int skillId, int level)
    {
        var msg = _spoilRules.Messages;
        bool alive = target.Kind == ZoneEntityKind.Npc && target.Attackable && target.CurHp > 0 && !target.Dead;
        var refusal = Fineage.Game.Services.SpoilRules.Spoil(alive, target.SpoilerObjectId != 0,
            () => forced || ZoneCombat.RollMagicSuccess(_combatRandom, target.Level, _spoilRules.MagicLevel(skillId, level)));
        switch (refusal)
        {
            case Fineage.Game.Services.SpoilRefusal.InvalidTarget:
                Notice(session, msg.InvalidTarget, "Invalid target.");
                return false;
            case Fineage.Game.Services.SpoilRefusal.AlreadySpoiled:
                Notice(session, msg.AlreadySpoiled, "It has already been spoiled.");
                return false;
            case Fineage.Game.Services.SpoilRefusal.Resisted:
                Notice(session, msg.Resisted, $"{target.Name} has resisted your Spoil.");
                AddHate(target, player);
                return false;
        }

        target.SpoilerObjectId = player.ObjectId;
        Notice(session, msg.SpoilActivated, "The Spoil condition has been activated.");
        // TargetStatus spoil icon rides AbnormalStatusUpdate (skill 254) for knownlist watchers.
        FanoutAbnormal(target);
        if (!forced)
            AddHate(target, player);
        ZoneLog.Info($"spoil object={target.ObjectId} spoiler={player.ObjectId}");
        return true;
    }

    bool SameParty(int a, int b) =>
        _partyByPlayer.TryGetValue(a, out int pa) && _partyByPlayer.TryGetValue(b, out int pb) && pa == pb;

    /// <summary>
    /// Mobius Sweeper (skill 42) + ConditionPlayerCanSweep + ConsumeBody: spoiled (343), spoiler or party mate (683), corpse not
    /// too old (1247), bag slots/weight; then spoil loot via <c>Party.distributeItem(..., spoil=true)</c> and the corpse is consumed.
    /// </summary>
    bool ApplySweep(ZoneSession session, ZoneEntity player, ZoneEntity target)
    {
        var msg = _spoilRules.Messages;
        long now = _clock().ToUnixTimeMilliseconds();
        bool corpse = target.Kind == ZoneEntityKind.Npc && target.Attackable && target.CurHp <= 0 && target.DecayAtMs > 0;
        var refusal = _spoilRules.Sweep(corpse, target.SpoilerObjectId, player.ObjectId,
            SameParty(player.ObjectId, target.SpoilerObjectId), target.DecayAtMs - now);
        switch (refusal)
        {
            case Fineage.Game.Services.SweepRefusal.InvalidTarget:
                Notice(session, msg.InvalidTarget, "Invalid target.");
                return false;
            case Fineage.Game.Services.SweepRefusal.NotSpoiled:
                Notice(session, msg.SweepNotSpoiled, "Sweeper failed, target not spoiled.");
                return false;
            case Fineage.Game.Services.SweepRefusal.NoRights:
                Notice(session, msg.NoSweepRights, "There are no priority rights on a sweeper.");
                return false;
            case Fineage.Game.Services.SweepRefusal.CorpseTooOld:
                Notice(session, msg.CorpseTooOld, "The corpse is too old. The skill cannot be used.");
                return false;
        }

        // Pre-check: sweeper must be able to hold everything in FindersKeepers / fallback path (Mobius
        // ConditionPlayerCanSweep capacity). Party modes re-check per looter below.
        if (!CanReceiveSweep(player, target.SweepItems))
        {
            if (!CanReceiveAnySweepWeight(player, target.SweepItems))
                Notice(session, NpcServiceProtocol.WeightLimit, NpcServiceProtocol.WeightLimitText);
            else
                Notice(session, NpcServiceProtocol.InventoryFull, NpcServiceProtocol.InventoryFullText);
            return false;
        }

        ZoneParty? party = null;
        if (_partyByPlayer.TryGetValue(player.ObjectId, out int partyId))
            _parties.TryGetValue(partyId, out party);

        var touchInventories = new HashSet<int>();
        foreach (var stack in target.SweepItems)
        {
            if (stack.ItemId == WarehouseRules.AdenaItemId && party != null)
            {
                DistributeSweepAdena(party, player, target, stack.Count, touchInventories);
                continue;
            }

            ZoneEntity looter = player;
            if (party != null)
            {
                int last = party.ItemLastLoot;
                int looterId = PartyLootRules.ActualLooter(
                    player.ObjectId,
                    (PartyDistributionType)party.ItemDistribution,
                    spoil: true,
                    party.MemberIds,
                    id => CanReceiveOne(id, stack.ItemId, stack.Count, target),
                    ref last,
                    _combatRandom);
                party.ItemLastLoot = last;
                if (_byId.TryGetValue(looterId, out var entity) && entity.Kind == ZoneEntityKind.Player && entity.Inventory != null)
                    looter = entity;
            }

            GrantSweepStack(looter, stack.ItemId, stack.Count, touchInventories);
        }

        foreach (int objectId in touchInventories)
        {
            if (_byId.TryGetValue(objectId, out var e) && e.Session != null && e.Inventory != null)
                SaveInventory(e.Session, e.Inventory);
        }

        ZoneLog.Info($"sweep object={target.ObjectId} spoiler={target.SpoilerObjectId} sweeper={player.ObjectId} items={target.SweepItems.Count} party={(party != null)}");
        target.SweepItems.Clear();
        DespawnNpc(target); // Mobius ConsumeBody
        return true;
    }

}
