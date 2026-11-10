using Fineage.Game.Services;
using Fineage.Game.Skills;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Skills;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    void DistributeSweepAdena(ZoneParty party, ZoneEntity sweeper, ZoneEntity corpse, int adena, HashSet<int> touched)
    {
        var inRange = new List<ZoneEntity>();
        foreach (int id in party.MemberIds)
        {
            if (!_byId.TryGetValue(id, out var m) || m.Kind != ZoneEntityKind.Player || m.Inventory == null || m.Dead)
                continue;
            if (!ZoneCombat.Within(corpse.X, corpse.Y, m.X, m.Y, PartyLootRules.PartyRange))
                continue;
            inRange.Add(m);
        }
        if (inRange.Count == 0)
        {
            GrantSweepStack(sweeper, WarehouseRules.AdenaItemId, adena, touched);
            return;
        }

        int share = PartyLootRules.AdenaShare(adena, inRange.Count);
        if (share <= 0)
            return;
        foreach (var m in inRange)
            GrantSweepStack(m, WarehouseRules.AdenaItemId, share, touched);
    }

    void GrantSweepStack(ZoneEntity looter, int itemId, int count, HashSet<int> touched)
    {
        if (looter.Inventory == null || looter.Session == null || count <= 0)
            return;
        var added = looter.Inventory.Add(itemId, count);
        var update = InventoryMessages.Update(InventoryChange.Add, added);
        NetBridge?.FanoutInventoryUpdate([looter.Session.ObjectId], update);
        touched.Add(looter.ObjectId);
    }

    bool CanReceiveSweep(ZoneEntity player, List<Fineage.Zone.Drops.DropStack> stacks)
    {
        if (player.Inventory == null)
            return false;
        int slots = stacks.Select(s => s.ItemId).Distinct().Count(id => player.Inventory.FindByItemId(id) == null);
        if (player.Inventory.Items.Count + slots > WarehouseRules.InventorySlots(player.Race))
            return false;
        return CanReceiveAnySweepWeight(player, stacks);
    }

    bool CanReceiveAnySweepWeight(ZoneEntity player, List<Fineage.Zone.Drops.DropStack> stacks)
    {
        if (player.Inventory == null)
            return false;
        long incoming = stacks.Sum(s => (long)_weights.Of(s.ItemId) * s.Count);
        int carried = _weights.Load(player.Inventory.Items.Select(item => (item.ItemId, item.Count)));
        return carried + incoming <= MaxLoadOf(player);
    }

    bool CanReceiveOne(int objectId, int itemId, int count, ZoneEntity corpse)
    {
        if (!_byId.TryGetValue(objectId, out var m) || m.Kind != ZoneEntityKind.Player || m.Inventory == null || m.Dead)
            return false;
        if (!ZoneCombat.Within(corpse.X, corpse.Y, m.X, m.Y, PartyLootRules.PartyRange))
            return false;
        bool newSlot = m.Inventory.FindByItemId(itemId) == null;
        if (newSlot && m.Inventory.Items.Count + 1 > WarehouseRules.InventorySlots(m.Race))
            return false;
        long incoming = (long)_weights.Of(itemId) * count;
        int carried = _weights.Load(m.Inventory.Items.Select(item => (item.ItemId, item.Count)));
        return carried + incoming <= MaxLoadOf(m);
    }

    /// <summary>Skill 254 / 42 through the normal SkillUse path: MP, reuse, MagicSkillUse fan-out, then the spoil/sweep effect.</summary>
    string? TrySkillUseSpoilOrSweep(ZoneSession session, ZoneEntity player, SkillUseIntent request, SkillDefinition template,
        SkillLevelData levelData, int level, long now, bool sweep)
    {
        int targetId = request.TargetObjectId > 0 ? request.TargetObjectId : session.SelectedTargetId;
        if (targetId <= 0 || !_byId.TryGetValue(targetId, out var target) || !VisibleTo(player.ObjectId, targetId))
            return SkillProtocol.Target;
        if (target.Kind != ZoneEntityKind.Npc || !target.Attackable)
        {
            Notice(session, _spoilRules.Messages.InvalidTarget, "Invalid target.");
            return null;
        }

        if (!ZoneCombat.InCastRange(player.X, player.Y, target.X, target.Y, Math.Max(40, template.CastRange) + 100))
        {
            if (sweep)
                return SkillProtocol.Range;
            long travelMs = ApproachTarget(player, target, Math.Max(40, template.CastRange));
            if (travelMs < 0)
                return SkillProtocol.Range;
            session.AutoAttackTargetId = 0;
            session.SelectedTargetId = targetId;
            session.PendingCastSkillId = request.SkillId;
            session.PendingCastTargetId = targetId;
            session.PendingCastAtUnixMs = now + travelMs;
            return null;
        }

        if (!sweep)
        {
            // Pre-cast checks cost nothing (109 / 357); the magic roll and the spoil flag land at hitTime.
            if (target.CurHp <= 0 || target.Dead)
            {
                Notice(session, _spoilRules.Messages.InvalidTarget, "Invalid target.");
                return null;
            }

            if (target.SpoilerObjectId != 0)
            {
                Notice(session, _spoilRules.Messages.AlreadySpoiled, "It has already been spoiled.");
                return null;
            }
        }
        else if (!ApplySweep(session, player, target))
            return null; // Sweeper conditions (343/683/1247) cost nothing

        player.CurMp = Math.Max(0, player.CurMp - levelData.MpConsume);
        var (hitMs, reuseMs) = ScaledSkillTimes(player, template);
        if (reuseMs > 0)
            player.SkillReuseUntilMs[request.SkillId] = now + reuseMs;
        var cast = BuildCast(player, target, request.SkillId, level, hitMs, reuseMs);
        StopForCast(player, target, now);
        FanoutSkill(player, target, cast, targetStatus: null, CasterMpStatus(player));
        if (!sweep)
            player.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;
        player.CastingUntilMs = now + Math.Max(0, hitMs);
        session.SelectedTargetId = sweep ? 0 : targetId;
        if (!sweep)
        {
            int skillId = request.SkillId;
            // Mobius Spoil effect (instant, physical skill: not interrupted by hits) at hitTime 1.8 s.
            ScheduleEffect(player, target, template, level, now, hitMs,
                (t, _) => ApplySpoil(session, player, t, forced: false, skillId, level));
        }
        return null;
    }

    /// <summary>Test: first live attackable NPC (spoil target).</summary>
    internal ZoneEntity? AttackableForTest()
    {
        lock (_gate)
            return _byId.Values.FirstOrDefault(e => e.Kind == ZoneEntityKind.Npc && e.Attackable && e.CurHp > 0);
    }

    /// <summary>Test hook: up to <paramref name="take"/> living attackable NPCs.</summary>
    internal List<ZoneEntity> AttackablesForTest(int take = 2)
    {
        lock (_gate)
            return _byId.Values
                .Where(e => e.Kind == ZoneEntityKind.Npc && e.Attackable && e.CurHp > 0)
                .Take(take)
                .ToList();
    }

    /// <summary>Test: kill an NPC through the normal death path (corpse, drops, spoil roll, decay timer).</summary>
    internal void KillForTest(ZoneEntity npc, ZoneEntity killer)
    {
        lock (_gate)
        {
            npc.CurHp = 0;
            RemoveDeadNpc(npc, killer);
        }
    }

    /// <summary>Test: run corpse decay at the kit clock.</summary>
    internal void DecayForTest()
    {
        lock (_gate)
            TickCorpseDecay(_clock().ToUnixTimeMilliseconds());
    }
}
