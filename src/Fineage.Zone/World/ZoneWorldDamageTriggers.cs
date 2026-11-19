using Fineage.Game;
using Fineage.Zone.Combat;

namespace Fineage.Zone.World;

/// <summary>
/// Mobius <c>TriggerSkillByDamage</c> (Mirage 445 → <c>TargetCancel</c> 5144, and the same family on other buffs).
/// <para>
/// Mobius registers a listener on <c>ON_CREATURE_DAMAGE_RECEIVED</c> while the effect is active: when the owner takes
/// damage from a matching attacker type/level for at least <c>minDamage</c>, the trigger skill is cast at that attacker
/// with a <c>chance</c> roll. Mirage's payload is <c>TargetCancel</c>: the attacker loses its target, stops attacking and
/// returns to idle.
/// </para>
/// </summary>
public sealed partial class ZoneWorld
{
    /// <summary>Mobius <c>TriggerSkillByDamage.onDamageReceivedEvent</c>: damage-over-time never fires the trigger.</summary>
    void RunDamageTriggers(ZoneEntity owner, ZoneEntity attacker, int damage, bool damageOverTime = false)
    {
        if (damage <= 0 || damageOverTime || attacker == null || ReferenceEquals(attacker, owner))
            return;
        if (owner.Abnormals.Count == 0)
            return;

        // Copy: the trigger may cancel the attacker's attack and remove nothing from the owner, but keeps the loop safe.
        var abnormals = owner.Abnormals.ToArray();
        foreach (var abnormal in abnormals)
        {
            if (!_skillEffects.TryGet(abnormal.Id, out var effects))
                continue;
            foreach (var effect in effects)
            {
                if (!effect.Name.Equals("TriggerSkillByDamage", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (effect.SkillId <= 0 || damage < effect.MinDamage)
                    continue;
                if (!AttackerTypeMatches(effect.AttackerType, attacker))
                    continue;
                if (attacker.Level < effect.MinAttackerLevel || attacker.Level > effect.MaxAttackerLevel)
                    continue;
                int chance = effect.Chance > 0 ? effect.Chance : 100;
                if (chance < 100 && _combatRandom.Next(100) >= chance)
                    continue;

                // Only ONE-target triggers check that the attacker is still aiming at the owner (Mirage is ONE).
                if (effect.AttackerType.Length > 0 && TargetOf(attacker) != owner.ObjectId)
                    continue;

                CastDamageTrigger(owner, attacker, effect.SkillId, effect.SkillLevel);
            }
        }
    }

    /// <summary>Mobius <c>InstanceType.isType(attackerType)</c> for the types the datapack actually uses.</summary>
    static bool AttackerTypeMatches(string attackerType, ZoneEntity attacker)
    {
        if (string.IsNullOrEmpty(attackerType))
            return true;
        bool playable = attacker.Kind == ZoneEntityKind.Player || attacker.Summon != null;
        return attackerType.ToLowerInvariant() switch
        {
            "playable" => playable,
            "player" => attacker.Kind == ZoneEntityKind.Player,
            "creature" => true,
            "npc" => attacker.Kind == ZoneEntityKind.Npc,
            _ => true
        };
    }

    /// <summary>
    /// The object a creature is currently fighting: a player's selected target, a monster's most hated hate entry
    /// (Mobius <c>Attackable.getTarget()</c> resolves to the hate target for monsters).
    /// </summary>
    static int TargetOf(ZoneEntity entity) =>
        entity.Kind == ZoneEntityKind.Player
            ? entity.Session?.SelectedTargetId ?? 0
            : entity.AggroTargetId;

    /// <summary>
    /// Mobius <c>makeTriggerCast</c>: the owner casts the trigger skill instantly (no MP, no reuse) at the attacker, and
    /// the launched packet goes out so nearby clients see the reflecting buff fire.
    /// </summary>
    void CastDamageTrigger(ZoneEntity owner, ZoneEntity attacker, int skillId, int level)
    {
        var template = _game.Skills.Find(skillId);
        long now = _clock().ToUnixTimeMilliseconds();
        ZoneLog.Info($"[trigger] {skillId} lv{level} owner={owner.ObjectId} attacker={attacker.ObjectId}");
        FanoutSkillLaunched(owner, skillId, level, attacker);

        int start = Math.Max(1, level);
        foreach (var effect in EffectsOf(skillId))
        {
            switch (effect.Name)
            {
                case "TargetCancel":
                    CancelTargetOf(attacker, effect.Chance);
                    break;
                case "AddHate":
                    if (attacker.Kind == ZoneEntityKind.Npc)
                        AddHate(attacker, owner, effect.Power > 0 ? (long)effect.Power : 1);
                    break;
                case "DeleteHateOfMe":
                    if (attacker.Kind == ZoneEntityKind.Npc && attacker.HateList.Remove(owner.ObjectId) &&
                        attacker.AggroTargetId == owner.ObjectId)
                        SetAggroTarget(attacker, MostHated(attacker));
                    break;
            }
        }

        // Trigger payloads (5144 TargetCancel) are internal skills with no skills.json row of their own: the effect
        // rows above are the whole behaviour. When a row does exist and carries an abnormal, apply it like a landed skill.
        if (template != null && template.AppliesAbnormal && template.TryLevelData(start, out var levelData))
            ApplyAbnormalFromSkill(template, levelData, attacker, now, attacker: owner);
    }

    SkillEffectCatalog.Effect[] EffectsOf(int skillId) =>
        _skillEffects.TryGet(skillId, out var effects) ? effects : [];

    /// <summary>
    /// Mobius instant payloads that the <c>&lt;for&gt;</c> block applies on the same hit as the abnormal:
    /// <c>Bluff</c> (the victim turns its back on the caster) plus <c>TargetCancel</c> / <c>DeleteHateOfMe</c>
    /// (the victim forgets who hit it). Each rolls its own <c>chance</c>; a skill without these rows is a no-op.
    /// </summary>
    void RunInstantPayloadEffects(ZoneEntity caster, ZoneEntity target, int skillId, int level = 1, long now = 0)
    {
        if (caster == null || target == null || skillId <= 0)
            return;
        if (now <= 0)
            now = _clock().ToUnixTimeMilliseconds();
        var template = _game.Skills.Find(skillId);
        foreach (var effect in EffectsOf(skillId))
        {
            switch (effect.Name)
            {
                case "Bluff":
                    BluffTurnBack(caster, target, effect.Chance);
                    break;
                case "TargetCancel":
                    CancelTargetOf(target, effect.Chance);
                    break;
                case "DeleteHateOfMe":
                    DeleteHateOf(caster, target, effect.Chance);
                    break;
                case "RandomizeHate":
                    RandomizeHate(caster, target, effect.Chance);
                    break;
                case "AddHate":
                    // Mobius AddHate.onStart: attackable only, addDamageHate(effector, 0, power).
                    // AddHate payloads skip EVT_ATTACKED / addAttackerToAttackByList (the payload itself wakes the mob),
                    // so this is the only hate write — quietly pulls the mob without clan-help notify.
                    if (target.Kind == ZoneEntityKind.Npc && target.Attackable)
                    {
                        long power = effect.Power > 0 ? (long)effect.Power : 1;
                        AddHate(target, caster, power);
                    }
                    break;
                case "GetAgro":
                    ApplyGetAgro(caster, target, effect, now);
                    break;
                case "TargetMe":
                    ApplyTargetMe(caster, target, effect);
                    break;
                case "CpDamPercent":
                    ApplyCpDamPercent(caster, target, effect, level);
                    break;
                case "Lethal":
                    ApplyLethalEffect(caster, target, effect, template, level);
                    break;
                case "RunAway":
                    ApplyRunAway(caster, target, effect, skillId, now);
                    break;
                case "DispelByCategory":
                    if (!string.IsNullOrEmpty(effect.Slot) && effect.Rate > 0)
                    {
                        int ml = template != null && template.TryLevelData(level, out var ld) ? ld.MagicLevel : caster.Level;
                        ApplyDispelByCategory(target, effect.Slot, effect.Rate, Math.Max(1, effect.Max), ml);
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// Mobius <c>Bluff.onStart</c>: broadcast <c>StartRotation</c> then <c>StopRotation</c> at the caster's heading and
    /// set it on the victim, so the dagger player can hit its back. Raid bosses, raid minions and the headquarters NPC
    /// (35062) never rotate.
    /// </summary>
    void BluffTurnBack(ZoneEntity caster, ZoneEntity target, int chance)
    {
        if (chance > 0 && chance < 100 && _combatRandom.Next(100) >= chance)
            return;
        if (target.NpcId == 35062 || target.NpcType is "RaidBoss" or "GrandBoss" || target.RaidMinion)
            return;
        // Mobius sends StartRotation + StopRotation to the region and settles the victim at the caster's heading, so the
        // caster now sees its back (positional crit). StopRotation carries the new heading, so the Net fanout reuses
        // PositionCorrection — the client turns the pawn instead of leaving it facing the caster until the next spawn.
        ApplyHeadingTurn(target, caster.Heading);
    }

    /// <summary>
    /// Mobius <c>StopRotation</c>: set the pawn's heading and tell every watcher, so the client's model turns at once.
    /// <see cref="ZoneEntity.Heading"/> alone is server-side only — the old Bluff path changed it and the monster kept
    /// facing the player on screen.
    /// </summary>
    void ApplyHeadingTurn(ZoneEntity entity, int heading)
    {
        entity.Heading = heading;
        List<int>? netWatchers = null;
        foreach (var watcher in Players())
        {
            if (watcher.Session != null && watcher.Knownlist.Contains(entity.ObjectId))
            {
                netWatchers ??= [];
                netWatchers.Add(watcher.ObjectId);
            }
        }
        if (netWatchers is { Count: > 0 } && NetBridge != null)
            NetBridge.FanoutPositionCorrection(netWatchers, entity.ObjectId, entity.X, entity.Y, entity.Z, heading);
    }

    /// <summary>
    /// Mobius <c>DeleteHateOfMe.onStart</c> (Trick 11): <c>stopHating(effector)</c>, <c>setWalking()</c> and
    /// <c>getAI().setIntention(AI_INTENTION_ACTIVE)</c> on an attackable victim.
    /// <para>
    /// Dropping the hate entry alone is not enough: the monster keeps its current <c>AggroTargetId</c> while the
    /// entry is gone from <c>HateList</c>, so it went on chasing and swinging at the player who just Trickshot it.
    /// Retail returns the victim to ACTIVE — it stops the chase, drops any swing in flight and walks.
    /// </para>
    /// </summary>
    void DeleteHateOf(ZoneEntity caster, ZoneEntity target, int chance)
    {
        if (chance > 0 && chance < 100 && _combatRandom.Next(100) >= chance)
            return;
        if (target.Kind != ZoneEntityKind.Npc || !target.Attackable)
            return;
        if (!target.HateList.Remove(caster.ObjectId))
            return;

        // Mobius setWalking(): return home is a walk, not a run.
        if (target.Running)
        {
            target.Running = false;
            FanoutMoveType(target);
        }

        int next = MostHated(target);
        if (next == caster.ObjectId)
            next = 0;
        if (next > 0)
        {
            // Another attacker is still hated: the mob keeps fighting, only the caster stops being a target.
            if (target.AggroTargetId == caster.ObjectId)
                SetAggroTarget(target, next);
            return;
        }

        // Nobody left: Mobius AI_INTENTION_ACTIVE — stop chasing, drop the swing, go idle (not returnHome: the
        // victim stays where it is until the normal thinkActive drift/returnHome tick).
        if (target.AggroTargetId == caster.ObjectId)
            SetAggroTarget(target, 0);
        _meleeHits.RemoveAll(h => h.AttackerId == target.ObjectId);
        target.NextMobAttackMs = 0;
        AbortCast(target, "trigger-delete-hate", notify: false);
        if (target.Chasing || target.MoveActive)
        {
            target.Chasing = false;
            StopTimedMove(target);
            BroadcastNpcMove(target, target.X, target.Y, target.Z);
        }
    }

    /// <summary>
    /// Mobius <c>RandomizeHate.onStart</c> (Switch 12): the victim's hate for the caster is moved onto a random other
    /// creature already on its hate list, so a mob fighting a group switches its attention without forgetting the
    /// caster entirely. Nothing happens when the caster is the only one it hates.
    /// </summary>
    void RandomizeHate(ZoneEntity caster, ZoneEntity target, int chance)
    {
        if (chance > 0 && chance < 100 && _combatRandom.Next(100) >= chance)
            return;
        if (target.Kind != ZoneEntityKind.Npc || !target.Attackable)
            return;
        if (ReferenceEquals(caster, target))
            return;

        List<int> others = [];
        foreach (int id in target.HateList.Keys)
        {
            if (id != caster.ObjectId && _byId.TryGetValue(id, out var other) && other.CurHp > 0 && !other.Dead)
                others.Add(id);
        }
        if (others.Count == 0)
            return;

        int picked = others[_combatRandom.Next(others.Count)];
        long hate = target.HateList.GetValueOrDefault(caster.ObjectId);
        target.HateList.Remove(caster.ObjectId);
        if (hate > 0)
            target.HateList[picked] = target.HateList.GetValueOrDefault(picked) + hate;
        SetAggroTarget(target, MostHated(target));
    }

    /// <summary>
    /// Mobius <c>TargetCancel.onStart</c>: <c>setTarget(null)</c> + <c>abortAttack</c> + <c>abortCast</c> +
    /// <c>AI_INTENTION_IDLE</c>. Player targets see their client target cleared; a monster drops its hate target and
    /// forgets the current victim for the swing in flight. Mobius skips <c>effected.isRaid()</c>: raid bosses, grand
    /// bosses and raid minions (<c>setIsRaidMinion</c> also sets <c>_isRaid</c>) keep their target.
    /// </summary>
    void CancelTargetOf(ZoneEntity entity, int chance)
    {
        if (IsRaidForEffects(entity))
            return;
        if (chance > 0 && chance < 100 && _combatRandom.Next(100) >= chance)
            return;

        bool abortAttack = false;
        if (entity.Kind == ZoneEntityKind.Player)
        {
            if (entity.Session is { } session)
            {
                int previous = session.SelectedTargetId;
                int autoAttack = session.AutoAttackTargetId;
                session.SelectedTargetId = 0;
                session.AutoAttackTargetId = 0;
                session.ClearApproach();
                if (previous > 0)
                    NetBridge?.FanoutTargetUnselected([entity.ObjectId], previous);
                abortAttack = autoAttack > 0;
            }
        }
        else
        {
            // Monster: Mobius abortAttack drops the swing in flight, then the hate list picks the next victim.
            abortAttack = entity.AggroTargetId > 0;
            if (abortAttack)
                SetAggroTarget(entity, MostHated(entity));
        }

        if (abortAttack)
        {
            _meleeHits.RemoveAll(h => h.AttackerId == entity.ObjectId);
            entity.NextMobAttackMs = 0;
        }
        AbortCast(entity, "trigger-target-cancel", notify: false);
    }

    /// <summary>Mobius <c>Attackable.isRaid()</c>: true for raid/grand bosses and for minions spawned by a raid.</summary>
    static bool IsRaidForEffects(ZoneEntity entity) =>
        entity.Kind == ZoneEntityKind.Npc && (entity.NpcType is "RaidBoss" or "GrandBoss" || entity.RaidMinion);
}
