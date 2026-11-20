using Fineage.Game.Skills;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Npcs;

namespace Fineage.Zone.World;

/// <summary>
/// Mobius <c>RestorationRandom</c> (self item extraction) and <c>SummonNpc</c> signets.
/// <para>
/// Both are A1 self skills whose effect rows the generic dispatch cannot serve: Summon Treasure Key (419) has
/// <c>power=0</c> so it used to fall into the heal branch and throw on the missing heal amount, and the Battle
/// Symbol signets (454–460) have <c>power=200</c> with a SELF target so they fell into the attack branch and were
/// refused with <c>skill_target</c>. Mobius runs them through their own effect handlers, so they get their own
/// routing here.
/// </para>
/// </summary>
public sealed partial class ZoneWorld
{
    /// <summary>Mobius <c>SystemMessageId.THERE_WAS_NOTHING_FOUND_INSIDE</c> (1669).</summary>
    public const int SysMsgNothingFoundInside = 1669;

    /// <summary>
    /// Mobius <c>Creature.checkDoCastConditions</c>: a skill with <c>itemConsumeId</c> is refused with SysMsg 351
    /// ("There are not enough necessary items to use the skill.") when the bag is short. Nothing is spent.
    /// </summary>
    bool ConsumeItemBlocked(ZoneSession session, ZoneEntity player, SkillLevelData levelData)
    {
        if (levelData.ItemConsumeId <= 0 || levelData.ItemConsumeCount <= 0)
            return false;
        if ((player.Inventory?.CountOf(levelData.ItemConsumeId) ?? 0) >= levelData.ItemConsumeCount)
            return false;
        SelfLine(session, SysMsgNotEnoughItems, "There are not enough necessary items to use the skill.");
        return true;
    }

    /// <summary>Mobius <c>Creature.useMagic</c>: <c>destroyItemByItemId("Consume", …)</c> before the effect lands.</summary>
    bool ConsumeSkillItems(ZoneSession session, ZoneEntity player, SkillLevelData levelData)
    {
        if (levelData.ItemConsumeId <= 0 || levelData.ItemConsumeCount <= 0)
            return true;
        var inventory = player.Inventory;
        if (inventory == null)
            return false;
        var changes = new List<InventoryUpdateEntryMsg>();
        if (!Take(inventory, levelData.ItemConsumeId, levelData.ItemConsumeCount, changes))
        {
            SelfLine(session, SysMsgNotEnoughItems, "There are not enough necessary items to use the skill.");
            return false;
        }
        FanInventory(session, Collapse(changes));
        SaveInventory(session, inventory);
        return true;
    }

    // ---------------------------------------------------------------- Summon Treasure Key (419)

    /// <summary>
    /// Mobius <c>RestorationRandom</c> on Summon Treasure Key (419): a self A1 cast that spends 2 Thief Keys and
    /// rolls one <c>capsuled_items_skill</c> segment into the bag. Levels differ only by the roulette (level 1 hands
    /// out one Grade-5 key with certainty, level 4 splits 25 % across grades 5–8 for 1/2/3/4 keys).
    /// </summary>
    string? TrySkillUseRestorationRandom(
        ZoneSession session,
        ZoneEntity player,
        SkillUseIntent request,
        SkillDefinition template,
        SkillLevelData levelData,
        int level,
        long now)
    {
        if (player.Dead)
            return SkillProtocol.Dead;
        if (levelData.Extractable.Count == 0)
        {
            // Mobius logs "Extractable Skill with no data" and does nothing.
            ZoneLog.Warn($"skill {request.SkillId} lv{level} has no capsuled_items_skill groups");
            return null;
        }

        // Mobius <cond><and><player invSize="10"/><player weight="80"/></and></cond>: refuse when the bag is
        // nearly full (fewer than 10 free slots) or nearly overweight (80 % of max load).
        if (!RestorationRoomAvailable(session, player))
            return null;

        if (ConsumeItemBlocked(session, player, levelData))
            return null;

        ConsumeSkillMp(player, template, levelData.MpConsume);
        var (hitMs, reuseMs) = ScaledSkillTimes(player, template, level);
        if (reuseMs > 0)
            player.SkillReuseUntilMs[request.SkillId] = now + reuseMs;

        var cast = BuildCast(player, player, request.SkillId, level, hitMs, reuseMs);
        StopForCast(player, player, now);
        FanoutSkill(player, player, cast, targetStatus: null, CasterMpStatus(player));
        player.CastingUntilMs = now + Math.Max(0, hitMs);
        session.SelectedTargetId = player.ObjectId;

        var groups = levelData.Extractable;
        ScheduleEffect(player, player, template, level, now, hitMs, (t, at) =>
        {
            if (t.Dead)
                return;
            // Mobius destroys the consumable at launch and only then rolls the product list.
            if (!ConsumeSkillItems(session, t, levelData))
                return;
            var group = RollExtractable(groups);
            if (group == null || group.Items.Count == 0)
            {
                SelfLine(session, SysMsgNothingFoundInside, "There was nothing found inside.");
                return;
            }
            var granted = new List<Fineage.Game.ItemInstance>();
            foreach (var item in group.Items)
            {
                var stack = t.Inventory?.Add(item.Id, item.Count);
                if (stack != null)
                    granted.Add(stack);
            }
            foreach (var stack in granted)
                NetBridge?.FanoutInventoryUpdate([session.ObjectId],
                    InventoryMessages.Update(InventoryChange.Add, stack));
            foreach (var item in group.Items)
            {
                string name = _game.Items.TryGet(item.Id, out var type) ? type.Name : item.Id.ToString();
                if (item.Count > 1)
                    SelfLine(session, ManorProtocol.ObtainedMany, $"You have obtained {item.Count} {name}.", name,
                        item.Count.ToString());
                else
                    SelfLine(session, ManorProtocol.ObtainedOne, $"You have obtained {name}.", name);
            }
            if (granted.Count > 0 && t.Inventory != null)
                SaveInventory(session, t.Inventory);
            ZoneLog.Info($"restoration-random skill={request.SkillId} lv{level} groups={groups.Count} " +
                         $"granted={string.Join(',', group.Items.Select(i => $"{i.Id}x{i.Count}"))}");
        });
        return null;
    }

    /// <summary>
    /// Mobius <c>RestorationRandom.onStart</c> roulette: one roll of <c>100 · Rnd.nextDouble()</c>, then walk the
    /// segments accumulating their widths. A roll past the last segment finds nothing.
    /// </summary>
    ExtractableGroup? RollExtractable(IReadOnlyList<ExtractableGroup> groups)
    {
        double roll = 100 * FishingRandom.NextDouble();
        double from = 0;
        foreach (var group in groups)
        {
            if (roll >= from && roll <= from + group.Chance)
                return group;
            from += group.Chance;
        }
        return null;
    }

    /// <summary>
    /// Mobius <c>ConditionPlayerInvSize</c> + <c>ConditionPlayerWeight</c> on skill 419. The datapack asks for ten
    /// free inventory slots and a load below 80 % of the limit; a dwarf's bigger bag folds in through
    /// <see cref="Fineage.Game.Services.WarehouseRules.InventorySlots"/>.
    /// </summary>
    bool RestorationRoomAvailable(ZoneSession session, ZoneEntity player)
    {
        var inventory = player.Inventory;
        if (inventory == null)
            return false;
        int limit = Fineage.Game.Services.WarehouseRules.InventorySlots(player.Race);
        int used = inventory.Items.Count;
        if (used > limit - 10)
        {
            ShotNotice(session, SysMsgInventoryFull, "Your inventory is full.");
            return false;
        }

        int maxLoad = MaxLoadOf(player);
        if (maxLoad > 0)
        {
            long load = CurrentLoadOf(player) - BonusWeightPenaltyOf(player);
            if (load * 100 / maxLoad >= 80)
            {
                ShotNotice(session, SysMsgWeightLimit, "You have exceeded the weight limit.");
                return false;
            }
        }
        return true;
    }

    // ---------------------------------------------------------------- SummonNpc signets (454–460, 1422, 1424)

    /// <summary>Mobius <c>EffectPoint</c> signet ids the Batte Force line plants at the caster's feet.</summary>
    internal static readonly HashSet<int> SignetSkillIds = [454, 455, 456, 457, 458, 459, 460, 1422, 1424];

    /// <summary>
    /// Mobius <c>SummonNpc</c> on an EffectPoint template (signets 454–460): spend 1 Battle Symbol, then plant an
    /// invulnerable aura NPC that re-applies its buff to everyone inside <c>affectRange</c> every
    /// <c>skill_delay</c> seconds and leaves after <c>despawn_time</c> seconds.
    /// </summary>
    string? TrySkillUseSignet(
        ZoneSession session,
        ZoneEntity player,
        SkillUseIntent request,
        SkillDefinition template,
        SkillLevelData levelData,
        int level,
        long now)
    {
        if (player.Dead)
            return SkillProtocol.Dead;
        if (!_skillEffects.TryGet(request.SkillId, out var effects))
            return null;
        SkillEffectCatalog.Effect summon = default;
        bool found = false;
        foreach (var effect in effects)
        {
            if (!effect.Name.Equals("SummonNpc", StringComparison.OrdinalIgnoreCase))
                continue;
            summon = effect;
            found = true;
            break;
        }
        if (!found)
            return null;

        int npcId = summon.ReadExtraInt("npcId");
        int npcCount = Math.Max(1, summon.ReadExtraInt("npcCount", 1));
        if (npcId <= 0)
        {
            ZoneLog.Warn($"signet skill {request.SkillId} has no npcId");
            return null;
        }
        if (ConsumeItemBlocked(session, player, levelData))
            return null;

        // Mobius skill 455 is GROUND target: the symbol lands where the caster clicked, everything else at the caster.
        int x = player.X, y = player.Y, z = player.Z;
        if (string.Equals(template.TargetType, "GROUND", StringComparison.OrdinalIgnoreCase) &&
            request.TargetObjectId > 0 && _byId.TryGetValue(request.TargetObjectId, out var ground))
        {
            x = ground.X;
            y = ground.Y;
            z = ground.Z;
        }

        ConsumeSkillMp(player, template, levelData.MpConsume);
        var (hitMs, reuseMs) = ScaledSkillTimes(player, template, level);
        if (reuseMs > 0)
            player.SkillReuseUntilMs[request.SkillId] = now + reuseMs;

        var cast = BuildCast(player, player, request.SkillId, level, hitMs, reuseMs);
        StopForCast(player, player, now);
        FanoutSkill(player, player, cast, targetStatus: null, CasterMpStatus(player));
        player.CastingUntilMs = now + Math.Max(0, hitMs);
        session.SelectedTargetId = player.ObjectId;

        ScheduleEffect(player, player, template, level, now, hitMs, (t, at) =>
        {
            if (t.Dead)
                return;
            if (!ConsumeSkillItems(session, t, levelData))
                return;
            for (int i = 0; i < npcCount; i++)
                SpawnSignet(t, npcId, x, y, z, at);
        });
        return null;
    }

    /// <summary>
    /// Mobius <c>SummonNpc</c> EffectPoint branch: an invulnerable, non-attackable NPC owned by the caster that carries
    /// the template's <c>despawn_time</c>. Its aura buff comes from <c>npc-ai-support.json</c> (5123–5129), which the
    /// ordinary NPC support tick already casts on self and on nearby players.
    /// </summary>
    ZoneEntity? SpawnSignet(ZoneEntity owner, int npcId, int x, int y, int z, long now)
    {
        var npc = SpawnQuestNpc(owner, npcId, x, y, z, owner.InstanceId,
            ZoneCombat.HeadingFrom(x, y, owner.X, owner.Y));
        if (npc == null)
            return null;

        npc.HomeSet = false;
        // Mobius EffectPoint: setInvul(true) and attackable=false, so the aggression scan never picks it up.
        SetAggroTarget(npc, 0);
        if (NpcCatalog.LoadDefault().ById.TryGetValue(npcId, out var template))
        {
            int despawnSeconds = template.DespawnTimeSeconds > 0 ? template.DespawnTimeSeconds : 20;
            if (npc.QuestSpawn != null)
                npc.QuestSpawn.DespawnAtMs = now + despawnSeconds * 1000L;
            _signets.Add(npc.ObjectId);
            // First pulse waits one skill_delay, then repeats; LastAiTickMs is the signet's own cadence clock.
            npc.LastAiTickMs = now;
            ZoneLog.Info($"signet spawn npc={npcId} object={npc.ObjectId} owner={owner.ObjectId} " +
                         $"at={npc.X},{npc.Y},{npc.Z} despawn={despawnSeconds}s delay={template.SkillDelaySeconds}s");
        }
        return npc;
    }

    /// <summary>Test hook: plant a signet NPC without a cast (despawn timer included).</summary>
    internal ZoneEntity? SpawnSignetForTest(ZoneEntity owner, int npcId)
    {
        lock (_gate)
        {
            return SpawnSignet(owner, npcId, owner.X, owner.Y, owner.Z, NowUnixMs());
        }
    }

    /// <summary>Mobius <c>EffectPoint</c> aura: who the signet buffs is everyone inside the skill's affectRange.</summary>
    internal int SignetAffectRangeForTests(int npcId) =>
        _npcAiSupport.Get(npcId)?.Buff is { Count: > 0 } buff ? buff[0].AffectRange : 0;
}