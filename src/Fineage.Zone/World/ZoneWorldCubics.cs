using Fineage.Game;
using Fineage.Game.Services;
using Fineage.Game.Skills;
using Fineage.Protocol;
using Fineage.Zone.Combat;

namespace Fineage.Zone.World;

/// <summary>
/// Cubics after Mobius CT 0 (<c>SummonCubic</c>, <c>Cubic</c>, <c>CubicAction</c>, <c>CubicHeal</c>,
/// <c>CubicMastery</c>): the summon skill puts a cubic on the caster (or every party member for the mass skills);
/// one per kind, at most 1 (or the Cubic Mastery count) at once, the oldest leaves first. While the owner or its
/// summon is in attack stance an offensive cubic acts every <c>delay</c> seconds with its skill chance on the owner's
/// target (monster that hates the owner / summon, or a flagged enemy player in PvP); a Life Cubic heals the most hurt
/// party member within 900 from the moment it appears. After <c>maxCount</c> actions in one fight the cubic stops
/// (and is used up when <c>useUp</c>). It leaves at the end of its lifetime, on the owner's death and on logout.
/// The cubic list rides on UserInfo / CharInfo; every action is a <c>MagicSkillUse</c> from the owner.
/// </summary>
public sealed partial class ZoneWorld
{
    /// <summary>Mobius <c>Cubic.MAX_MAGIC_RANGE</c>.</summary>
    public const int CubicRange = 900;

    CubicCatalog? _cubics;
    readonly Dictionary<int, SkillDefinition> _cubicActionSkills = [];
    readonly HashSet<int> _withCubics = [];

    CubicCatalog Cubics => _cubics ??= LoadCubics();

    static CubicCatalog LoadCubics()
    {
        try
        {
            return CubicCatalog.LoadDefault();
        }
        catch (FileNotFoundException)
        {
            return CubicCatalog.Empty;
        }
    }

    internal static int[] CubicIdsOf(ZoneEntity e) => e.Kind == ZoneEntityKind.Player ? e.CubicIds() : [];

    // ---------------------------------------------------------------- summon skill

    string? TrySkillUseCubic(ZoneSession session, ZoneEntity player, SkillUseIntent request, CubicSummonSkill skill,
        SkillDefinition template, SkillLevelData levelData, int level, long now)
    {
        var row = skill.At(level);
        if (row == null)
            return "skill";
        if (row.ItemId > 0 && row.ItemCount > 0 && (player.Inventory?.CountOf(row.ItemId) ?? 0) < row.ItemCount)
        {
            SelfLine(session, SysMsgNotEnoughItems, "There are not enough necessary items to use the skill.");
            return null;
        }

        StartStaticCast(session, player, player, request.SkillId, template, levelData, level, now, (_, at) =>
        {
            if (player.Dead)
                return;
            if (row.ItemId > 0 && row.ItemCount > 0)
            {
                var inventory = player.Inventory;
                var changes = new List<InventoryUpdateEntryMsg>();
                if (inventory == null || !Take(inventory, row.ItemId, row.ItemCount, changes))
                {
                    SelfLine(session, SysMsgNotEnoughItems, "There are not enough necessary items to use the skill.");
                    return;
                }
                FanInventory(session, Collapse(changes));
                SaveInventory(session, inventory);
            }

            // Mobius PARTY target: the caster and every living party member within the skill's affect range.
            IEnumerable<ZoneEntity> targets = skill.Party
                ? PartyMembersOf(player).Where(m => !m.Dead && m.InstanceId == player.InstanceId &&
                    ZoneCombat.Within(player.X, player.Y, m.X, m.Y, Math.Max(template.EffectRange, CubicRange)))
                : [player];
            foreach (var m in targets)
                SummonCubic(m, skill, level, givenByOther: !ReferenceEquals(m, player), at);
        });
        return null;
    }

    /// <summary>Mobius <c>SummonCubic.onStart</c>: same kind replaced, else the oldest leaves when the cap is reached.</summary>
    bool SummonCubic(ZoneEntity player, CubicSummonSkill skill, int skillLevel, bool givenByOther, long now)
    {
        if (player.Kind != ZoneEntityKind.Player || player.Dead || _olympiadObservers.ContainsKey(player.ObjectId))
            return false;
        var row = skill.At(skillLevel);
        int cubicLevel = row is { CubicLevel: > 0 } ? row.CubicLevel : skillLevel > 100 ? (skillLevel - 100) / 7 + 8 : skillLevel;
        var template = Cubics.Template(skill.CubicId, cubicLevel);
        if (template == null)
        {
            ZoneLog.Info($"cubic skip unknown cubic={skill.CubicId} lv={cubicLevel} skill={skill.SkillId}");
            return false;
        }

        int existing = player.Cubics.FindIndex(c => c.Id == skill.CubicId);
        if (existing >= 0)
        {
            player.Cubics.RemoveAt(existing);
        }
        else
        {
            int max = Math.Max(1, MaxCubicCount(player));
            while (player.Cubics.Count >= max)
                player.Cubics.RemoveAt(0);
        }

        int durationSec = skill.DurationSec > 0 ? skill.DurationSec : template.DurationSec;
        var cubic = new CubicState
        {
            Template = template,
            Power = row is { Power: > 0 } ? row.Power : template.Power,
            DelayMs = (skill.DelaySec > 0 ? skill.DelaySec : template.DelaySec) * 1000L,
            Chance = skill.Chance,
            MaxCount = skill.MaxCount > -1 ? skill.MaxCount : template.MaxCount,
            ExpiresAtMs = now + durationSec * 1000L,
            GivenByOther = givenByOther,
        };
        player.Cubics.Add(cubic);
        _withCubics.Add(player.ObjectId);
        // Mobius Cubic constructor: a HEAL cubic starts its task at once.
        if (template.Heals)
            StartCubicAction(cubic, now);
        ZoneLog.Info($"cubic summon object={player.ObjectId} cubic={cubic.Id} lv={template.Level} power={cubic.Power} other={givenByOther}");
        BroadcastCubics(player);
        return true;
    }

    /// <summary>Mobius <c>PlayerStat.getMaxCubicCount</c>: 1, or the Cubic Mastery count while the passive is known.</summary>
    int MaxCubicCount(ZoneEntity player)
    {
        int max = 1;
        foreach (int id in Cubics.MasterySkillIds)
        {
            if (player.KnownSkills.TryGetValue(id, out int level) && Cubics.MasteryCount(id, level) is { } count)
                max = Math.Max(max, count);
        }
        return max;
    }

    /// <summary>Mobius <c>broadcastUserInfo</c> after the cubic list changed: UserInfo to the owner, CharInfo to peers.</summary>
    void BroadcastCubics(ZoneEntity player)
    {
        if (player.Session == null)
            return;
        SelfUserInfo(player);
        var peers = KnownNetPlayersOf(player).Where(id => id != player.ObjectId).ToList();
        if (peers.Count > 0)
            NetBridge?.FanoutCharInfo(peers, player.ObjectId);
    }

    /// <summary>Mobius <c>Player.removeCubic</c> for every cubic (death, logout).</summary>
    void RemoveAllCubics(ZoneEntity player, bool broadcast = true)
    {
        if (!player.HasCubics)
            return;
        player.Cubics.Clear();
        _withCubics.Remove(player.ObjectId);
        if (broadcast)
            BroadcastCubics(player);
    }

    void RemoveCubic(ZoneEntity player, CubicState cubic)
    {
        if (!player.Cubics.Remove(cubic))
            return;
        if (!player.HasCubics)
            _withCubics.Remove(player.ObjectId);
        BroadcastCubics(player);
    }

    static void StartCubicAction(CubicState cubic, long now)
    {
        if (cubic.Running || cubic.Template.Skills.Count == 0)
            return;
        cubic.Running = true;
        cubic.Count = 0;
        cubic.NextActionMs = now; // scheduleAtFixedRate(…, 0, delay)
    }

    static void StopCubicAction(CubicState cubic)
    {
        cubic.Running = false;
        cubic.Count = 0;
    }

    // ---------------------------------------------------------------- tick

    void TickCubics(long now)
    {
        if (_withCubics.Count == 0)
            return;
        foreach (int id in _withCubics.ToArray())
        {
            if (!_byId.TryGetValue(id, out var owner) || owner.Kind != ZoneEntityKind.Player || !owner.HasCubics)
            {
                _withCubics.Remove(id);
                continue;
            }
            if (owner.Dead || owner.Session == null)
            {
                RemoveAllCubics(owner);
                continue;
            }
            foreach (var cubic in owner.Cubics.ToArray())
            {
                if (now >= cubic.ExpiresAtMs)
                {
                    // Mobius CubicDisappear.
                    RemoveCubic(owner, cubic);
                    continue;
                }
                TickCubic(owner, cubic, now);
            }
        }
    }

    bool InAttackStance(ZoneEntity owner, long now) =>
        owner.CombatUntilUnixMs > now || SummonOf(owner) is { } s && s.CombatUntilUnixMs > now;

    void TickCubic(ZoneEntity owner, CubicState cubic, long now)
    {
        var template = cubic.Template;
        if (!template.Heals)
        {
            // Mobius Creature.doAttack / doCast → cubic.doAction(): the task starts with the fight.
            if (!cubic.Running)
            {
                if (!InAttackStance(owner, now))
                    return;
                StartCubicAction(cubic, now);
            }
        }
        if (!cubic.Running || now < cubic.NextActionMs)
            return;
        cubic.NextActionMs += cubic.DelayMs;
        if (cubic.NextActionMs <= now)
            cubic.NextActionMs = now + cubic.DelayMs;

        if (template.Heals)
        {
            CubicHealRun(owner, cubic, now);
            return;
        }

        // Mobius CubicAction.run.
        if (!InAttackStance(owner, now))
        {
            StopCubicAction(cubic);
            return;
        }
        if (CubicCapped(owner, cubic))
            return;
        if (_combatRandom.Next(100) >= cubic.Chance)
            return;
        var pick = PickCubicSkill(cubic);
        if (pick != null && TryCubicSkill(owner, cubic, pick, now))
            cubic.Count++;
    }

    /// <summary>Mobius maxCount check: stop the session; a <c>useUp</c> cubic goes away.</summary>
    bool CubicCapped(ZoneEntity owner, CubicState cubic)
    {
        if (cubic.MaxCount <= -1 || cubic.Count < cubic.MaxCount)
            return false;
        StopCubicAction(cubic);
        if (cubic.Template.UseUp)
            RemoveCubic(owner, cubic);
        return true;
    }

    CubicSkillRef? PickCubicSkill(CubicState cubic)
    {
        var skills = cubic.Template.Skills.Where(static s => !s.Priority).ToList();
        if (skills.Count == 0)
            return null;
        switch (cubic.Template.Selection.ToUpperInvariant())
        {
            case "ORDERED":
                foreach (var s in skills)
                {
                    if (s.TriggerRate >= 100 || _combatRandom.Next(100) < s.TriggerRate)
                        return s;
                }
                return null;
            case "WEIGHTED":
                {
                    int total = skills.Where(static s => s.TriggerRate > 0).Sum(static s => s.TriggerRate);
                    if (total > 0)
                    {
                        int roll = _combatRandom.Next(total);
                        foreach (var s in skills.Where(static s => s.TriggerRate > 0))
                        {
                            roll -= s.TriggerRate;
                            if (roll < 0)
                                return s;
                        }
                    }
                    return skills[_combatRandom.Next(skills.Count)];
                }
            default:
                return skills[_combatRandom.Next(skills.Count)];
        }
    }

    bool TryCubicSkill(ZoneEntity owner, CubicState cubic, CubicSkillRef pick, long now)
    {
        var action = Cubics.Action(pick.SkillId);
        if (action == null)
            return false;
        var target = CubicTarget(owner);
        if (target == null || target.Dead || target.CurHp <= 0 || !InCubicRange(owner, target))
            return false;
        FanoutCubicCast(owner, target, pick);
        ApplyCubicAction(owner, cubic, action, pick.Level, target, now);
        return true;
    }

    static bool InCubicRange(ZoneEntity owner, ZoneEntity target)
    {
        if (owner.InstanceId != target.InstanceId)
            return false;
        long dx = owner.X - target.X, dy = owner.Y - target.Y, dz = owner.Z - target.Z;
        return dx * dx + dy * dy + dz * dz <= (long)CubicRange * CubicRange;
    }

    /// <summary>Mobius <c>broadcastSkillPacket(new MagicSkillUse(owner, target, skill, level, 0, 0))</c>.</summary>
    void FanoutCubicCast(ZoneEntity owner, ZoneEntity target, CubicSkillRef pick)
    {
        if (NetBridge == null)
            return;
        var watchers = KnownNetPlayersOf(owner);
        foreach (int id in KnownNetPlayersOf(target))
        {
            if (!watchers.Contains(id))
                watchers.Add(id);
        }
        if (watchers.Count == 0)
            return;
        NetBridge.FanoutMagicSkillUse(watchers, new MagicSkillUseMsg
        {
            CasterObjectId = owner.ObjectId,
            TargetObjectId = target.ObjectId,
            SkillId = pick.SkillId,
            SkillLevel = pick.Level,
            HitTimeMs = 0,
            ReuseDelayMs = 0
        });
    }

    /// <summary>
    /// Mobius <c>Cubic.getCubicTarget</c>: the owner's target when it is a living monster that hates the owner or its
    /// summon; in Olympiad only the running opponent; a player only while the owner is flagged outside a peace zone
    /// (or both in a PvP zone), never a party / clan / alliance member, a white player outside PvP zones or anyone
    /// standing in a peace zone.
    /// </summary>
    ZoneEntity? CubicTarget(ZoneEntity owner)
    {
        int targetId = owner.Session?.SelectedTargetId ?? 0;
        if (targetId <= 0 || !_byId.TryGetValue(targetId, out var target) || target.Dead || target.CurHp <= 0)
            return null;
        if (ReferenceEquals(target, owner) || target.ObjectId == owner.SummonObjectId)
            return null;
        if (_olympiadArena.Contains(owner.ObjectId))
            return target.Kind == ZoneEntityKind.Player && !OlympiadTargetRefused(owner, target) ? target : null;
        if (owner.DuelActive)
            return DuelCubicTarget(owner, target);

        if (target.Kind == ZoneEntityKind.Npc && target.Summon == null)
        {
            if (target.HateList.ContainsKey(owner.ObjectId))
                return target;
            if (owner.SummonObjectId > 0 && target.HateList.ContainsKey(owner.SummonObjectId))
                return target;
            return null;
        }

        var enemy = CreditOf(target);
        if (enemy.Kind != ZoneEntityKind.Player || ReferenceEquals(enemy, owner))
            return null;
        bool ownerPvpZone = InPvpArea(owner);
        if (!(owner.PvpFlagged && !Peace.Contains(owner.X, owner.Y, owner.Z)) && !ownerPvpZone)
            return null;
        if (SameParty(owner.ObjectId, enemy.ObjectId))
            return null;
        if (!ownerPvpZone && owner.ClanId > 0 && owner.ClanId == enemy.ClanId)
            return null;
        if (!ownerPvpZone && owner.AllyId > 0 && owner.AllyId == enemy.AllyId)
            return null;
        if (!enemy.PvpFlagged && !InPvpArea(enemy))
            return null;
        if (Peace.Contains(enemy.X, enemy.Y, enemy.Z))
            return null;
        // Mobius takes the owner's target as is: a flagged player's summon is the summon itself.
        return target;
    }

    // ---------------------------------------------------------------- actions

    void ApplyCubicAction(ZoneEntity owner, CubicState cubic, CubicAction action, int level, ZoneEntity target, long now)
    {
        var row = action.At(level);
        switch (action.Kind)
        {
            case "mdam":
                CubicMagicDamage(owner, cubic, action, row, target, now, drain: false);
                break;
            case "drain":
                CubicMagicDamage(owner, cubic, action, row, target, now, drain: true);
                break;
            case "aggro":
                // Mobius TargetMeProbability: the monster turns on the cubic's owner.
                if (target.Kind == ZoneEntityKind.Npc && target.Summon == null && _combatRandom.Next(100) < action.AggroChance)
                {
                    AddHate(target, owner, DamageHate(Math.Max(1, (int)cubic.Power), target.Level));
                    SetAggroTarget(target, owner.ObjectId);
                }
                break;
            default:
                CubicAbnormal(owner, action, row, target, now);
                break;
        }
    }

    /// <summary>
    /// Mobius <c>Formulas.calcMagicDam(Cubic, …)</c>: <c>91 × (cubicPower + skillPower) / mDef</c>; a drain gives the
    /// owner 40 % of it back (<c>useCubicDrain</c>). The owner takes the credit, the hit lines and the PvP flag.
    /// </summary>
    void CubicMagicDamage(ZoneEntity owner, CubicState cubic, CubicAction action, CubicActionLevel row, ZoneEntity target,
        long now, bool drain)
    {
        double mDef = target.Kind == ZoneEntityKind.Player
            ? PlayerStats(target).MDef
            : Math.Max(1d, target.MDef > 0 ? target.MDef : target.Level * 30d);
        int damage = (int)(91d * (cubic.Power + row.Power) / Math.Max(1d, mDef));
        if (drain && damage > 0)
        {
            int add = (int)(damage * (action.DrainRatio > 0 ? action.DrainRatio : 0.4));
            if (add > 0 && owner.CurHp < owner.MaxHp)
            {
                owner.CurHp = Math.Min(owner.MaxHp, owner.CurHp + add);
                FanoutVitals(owner, PlayerVitalsStatus(owner));
            }
        }
        if (damage <= 0)
            return;
        QueueAttackerDamageLine(owner, target, damage);
        if (target.Kind == ZoneEntityKind.Player)
            ApplySkillDamageToPlayer(owner, target, damage, now);
        else if (target.Summon != null)
        {
            if (OwnerOf(target) is { } victimOwner)
                UpdatePvPStatus(owner, victimOwner, now);
            DamageSummon(owner, target, damage, now);
        }
        else
            DamageNpcBy(owner, target, damage, now);
    }

    /// <summary>
    /// Debuff, DoT, Paralysis, Shock, Hold: Mobius <c>calcCubicSkillSuccess</c> (land rate × stat × trait × level
    /// bonus) then the abnormal.
    /// </summary>
    void CubicAbnormal(ZoneEntity owner, CubicAction action, CubicActionLevel row, ZoneEntity target, long now)
    {
        var template = CubicActionSkill(action);
        if (!template.TryLevelData(row.Level, out var levelData))
            return;
        if (target.Kind == ZoneEntityKind.Player)
            UpdatePvPStatus(owner, target, now);
        else if (OwnerOf(target) is { } victimOwner)
            UpdatePvPStatus(owner, victimOwner, now);
        // A failed roll answers with the shared resist line (Mobius sends YOUR_ATTACK_HAS_FAILED here).
        if (!ApplyAbnormalFromSkill(template, levelData, target, now, attacker: owner))
            return;
        if (target.Kind == ZoneEntityKind.Npc && target.Summon == null)
        {
            AddHate(target, owner, 1);
            if (target.AggroTargetId <= 0)
                SetAggroTarget(target, owner.ObjectId);
        }
        owner.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;
    }

    /// <summary>A skill definition for a cubic action so the shared abnormal path (land roll, stats, DoT, control) runs.</summary>
    SkillDefinition CubicActionSkill(CubicAction action)
    {
        if (_cubicActionSkills.TryGetValue(action.SkillId, out var def))
            return def;
        var levels = action.Levels.Select(l => new SkillLevelData(l.Level, mpConsume: 0, power: (int)l.Power,
            abnormalLevel: l.AbnormalLevel > 0 ? l.AbnormalLevel : 1, magicLevel: Math.Max(1, l.MagicLevel))).ToArray();
        def = new SkillDefinition(action.SkillId, action.Name, passive: false, operateType: "A2", targetType: "ONE",
            castRange: CubicRange, hitTimeMs: 0, reuseDelayMs: 0, abnormalTimeSeconds: action.AbnormalTimeSec,
            isDebuff: true, levels: levels)
        {
            AbnormalType = action.AbnormalType,
            ActivateRate = action.ActivateRate,
            BasicProperty = action.BasicProperty.Equals("NONE", StringComparison.OrdinalIgnoreCase) ? "" : action.BasicProperty,
            Trait = action.Trait,
            LvlBonusRate = action.LvlBonusRate,
            // Mobius calcCubicSkillSuccess has no M.Atk / M.Def term ("TODO: Pending").
            MagicType = 0
        };
        _cubicActionSkills[action.SkillId] = def;
        return def;
    }

    /// <summary>
    /// Mobius <c>CubicHeal.run</c>: the most hurt (by HP %) living party member or party summon within 900, or the
    /// owner / its summon when alone; heals only when the missing HP is more than the heal.
    /// </summary>
    void CubicHealRun(ZoneEntity owner, CubicState cubic, long now)
    {
        if (CubicCapped(owner, cubic))
            return;
        var target = CubicHealTarget(owner);
        if (target == null)
            return;
        var pick = cubic.Template.Skills.FirstOrDefault(s => Cubics.Action(s.SkillId)?.Kind == "heal");
        if (pick == null || Cubics.Action(pick.SkillId) is not { } action)
            return;
        double amount = action.At(pick.Level).Heal;
        if (target.MaxHp - target.CurHp <= amount)
            return;
        target.CurHp = Math.Min(target.MaxHp, target.CurHp + (int)amount);
        FanoutVitals(target, target.Kind == ZoneEntityKind.Player ? PlayerVitalsStatus(target) : StatusFor(target));
        SyncNeedRegen(target);
        if (target.Summon != null)
            SendSummonInfo(target);
        FanoutCubicCast(owner, target, pick);
        cubic.Count++;
    }

    ZoneEntity? CubicHealTarget(ZoneEntity owner)
    {
        ZoneEntity? best = null;
        double left = 1d;
        void Consider(ZoneEntity e, bool rangeCheck)
        {
            if (e.Dead || e.CurHp <= 0 || e.MaxHp <= 0 || e.CurHp >= e.MaxHp)
                return;
            if (rangeCheck && !InCubicRange(owner, e))
                return;
            double pct = (double)e.CurHp / e.MaxHp;
            if (pct < left)
            {
                left = pct;
                best = e;
            }
        }

        var party = PartyMembersOf(owner);
        if (party.Count > 1 && !_olympiadArena.Contains(owner.ObjectId))
        {
            foreach (var m in party)
            {
                Consider(m, rangeCheck: true);
                if (SummonOf(m) is { } s)
                    Consider(s, rangeCheck: true);
            }
        }
        else
        {
            Consider(owner, rangeCheck: false);
            if (SummonOf(owner) is { } s)
                Consider(s, rangeCheck: true);
        }
        return best;
    }

    // ---------------------------------------------------------------- test hooks

    internal void TickCubicsForTest()
    {
        lock (_gate)
            TickCubics(_clock().ToUnixTimeMilliseconds());
    }

    internal bool SummonCubicForTest(ZoneEntity player, int skillId, int level, bool givenByOther = false)
    {
        lock (_gate)
        {
            var skill = Cubics.SummonSkill(skillId) ?? throw new ArgumentException($"no cubic skill {skillId}");
            return SummonCubic(player, skill, level, givenByOther, _clock().ToUnixTimeMilliseconds());
        }
    }

    internal CubicCatalog CubicCatalogForTest => Cubics;
}
