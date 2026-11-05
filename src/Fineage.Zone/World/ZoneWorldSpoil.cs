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

        string? error;
        lock (_gate)
            error = TryAdminSweep(session, request.TargetObjectId);
        if (error != null)
            return Task.FromResult<string?>(error);

        return Task.FromResult<string?>(null);
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
        if (!forced)
            AddHate(target, player);
        ZoneLog.Info($"spoil object={target.ObjectId} spoiler={player.ObjectId}");
        return true;
    }

    bool SameParty(int a, int b) =>
        _partyByPlayer.TryGetValue(a, out int pa) && _partyByPlayer.TryGetValue(b, out int pb) && pa == pb;

    /// <summary>
    /// Mobius Sweeper (skill 42) + ConditionPlayerCanSweep + ConsumeBody: spoiled (343), spoiler or party mate (683), corpse not
    /// too old (1247), bag slots/weight; then spoil loot to the sweeper and the corpse is consumed.
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

        var inventory = player.Inventory!;
        int slots = target.SweepItems.Select(s => s.ItemId).Distinct().Count(id => inventory.FindByItemId(id) == null);
        if (inventory.Items.Count + slots > WarehouseRules.InventorySlots(player.Race))
        {
            Notice(session, NpcServiceProtocol.InventoryFull, NpcServiceProtocol.InventoryFullText);
            return false;
        }

        long incoming = target.SweepItems.Sum(s => (long)_weights.Of(s.ItemId) * s.Count);
        int carried = _weights.Load(inventory.Items.Select(item => (item.ItemId, item.Count)));
        if (carried + incoming > MaxLoadOf(player))
        {
            Notice(session, NpcServiceProtocol.WeightLimit, NpcServiceProtocol.WeightLimitText);
            return false;
        }

        foreach (var stack in target.SweepItems)
        {
            var added = inventory.Add(stack.ItemId, stack.Count);
            var update = InventoryMessages.Update(InventoryChange.Add, added);
            // Swept loot is a bag change like any other, so Net clients need the same row the V2 frame carries.
            NetBridge?.FanoutInventoryUpdate([session.ObjectId], ToNetInventoryUpdate(update));
        }

        if (target.SweepItems.Count > 0)
            SaveInventory(session, inventory);
        ZoneLog.Info($"sweep object={target.ObjectId} spoiler={target.SpoilerObjectId} sweeper={player.ObjectId} items={target.SweepItems.Count}");
        target.SweepItems.Clear();
        DespawnNpc(target); // Mobius ConsumeBody
        return true;
    }

    /// <summary>Skill 254 / 42 through the normal SkillUse path: MP, reuse, MagicSkillUse fan-out, then the spoil/sweep effect.</summary>
    string? TrySkillUseSpoilOrSweep(ZoneSession session, ZoneEntity player, SkillUseRequestMsg request, SkillDefinition template,
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
        if (template.ReuseDelayMs > 0)
            player.SkillReuseUntilMs[request.SkillId] = now + template.ReuseDelayMs;
        var cast = BuildCast(player, target, request.SkillId, level, template);
        StopForCast(player, target, now);
        FanoutSkill(player, target, cast, targetStatus: null, CasterMpStatus(player));
        if (!sweep)
            player.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;
        player.CastingUntilMs = now + Math.Max(0, template.HitTimeMs);
        session.SelectedTargetId = sweep ? 0 : targetId;
        if (!sweep)
        {
            int skillId = request.SkillId;
            // Mobius Spoil effect (instant, physical skill: not interrupted by hits) at hitTime 1.8 s.
            ScheduleEffect(player, target, template, level, now, template.HitTimeMs,
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
