using Fineage.Game;
using Fineage.Game.Services;
using Fineage.Game.Skills;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Npcs;

namespace Fineage.Zone.World;

/// <summary>
/// Servitors after Mobius CT 0 (<c>Summon</c> effect, <c>Servitor</c>, <c>SummonAI</c>): a summon skill spends its
/// crystals and spawns the servitor NPC beside the owner; the owner orders it (follow / stay / attack / stop / move /
/// skill / dismiss); every 5 s its lifetime drops and the upkeep item is spent; its damage and kills count for the
/// owner, whose exp is multiplied by the servitor's exp multiplier while it is out.
/// </summary>
public sealed partial class ZoneWorld
{
    public const int SysMsgAlreadyHavePet = 543;
    public const int SysMsgSummonedMonsterUses = 1029;
    public const int SysMsgServitorUpkeepFailed = 1143;
    public const int SysMsgServitorPassedAway = 1520;
    public const int SysMsgSummonWrongTarget = 652;
    public const int SysMsgServitorInBattle = 1724;
    public const int SysMsgNotEnoughItems = 351;

    /// <summary>Mobius <c>Servitor</c>: <c>scheduleAtFixedRate(this, 0, 5000)</c>.</summary>
    public const int SummonLifeTickMs = 5000;
    /// <summary>A dead servitor's corpse stays this long before it is removed (Mobius decay).</summary>
    public const int SummonCorpseMs = 7000;
    /// <summary>Follow: the servitor runs back once the owner is farther than this…</summary>
    public const int SummonFollowRange = 150;
    /// <summary>…and stops this close to the owner (Mobius moveToPawn offset).</summary>
    public const int SummonFollowStop = 70;
    /// <summary>An attack order is dropped once the target is this far from the servitor.</summary>
    public const int SummonMaxChase = 2000;

    readonly SummonCatalog _summonSkills = SummonCatalog.LoadDefault();
    readonly HashSet<int> _summonIds = [];
    NpcCatalog? _summonTemplates;

    internal SummonCatalog SummonSkills => _summonSkills;

    /// <summary>The owner of a summon entity, or null for anything else.</summary>
    ZoneEntity? OwnerOf(ZoneEntity entity) =>
        entity.Summon is { } st && _byId.TryGetValue(st.OwnerObjectId, out var owner) && owner.Kind == ZoneEntityKind.Player
            ? owner
            : null;

    /// <summary>Mobius <c>asPlayer()</c>: the player behind a playable (the owner for a summon), else the entity.</summary>
    ZoneEntity CreditOf(ZoneEntity entity) => OwnerOf(entity) ?? entity;

    ZoneEntity? SummonOf(ZoneEntity player) =>
        player.SummonObjectId != 0 && _byId.TryGetValue(player.SummonObjectId, out var s) && s.Summon != null ? s : null;

    /// <summary>Mobius <c>Attackable.calculateRewards</c>: <c>hasServitor() ? getExpMultiplier() : 1</c>.</summary>
    double ServitorExpMultiplier(ZoneEntity player) =>
        SummonOf(player)?.Summon is { Kind: SummonKindNet.Servitor } st ? st.ExpMultiplier : 1d;

    // ---------------------------------------------------------------- summon skill

    string? TrySkillUseSummon(ZoneSession session, ZoneEntity player, SkillUseIntent request, SummonSkill skill,
        SkillDefinition template, SkillLevelData levelData, int level, long now)
    {
        var row = skill.At(level);
        if (row == null)
            return "skill";
        if (SummonOf(player) != null)
        {
            SelfLine(session, SysMsgAlreadyHavePet, "You already have a pet.");
            return null;
        }

        ZoneEntity? corpse = null;
        if (skill.Corpse)
        {
            int targetId = request.TargetObjectId > 0 ? request.TargetObjectId : session.SelectedTargetId;
            if (!_byId.TryGetValue(targetId, out corpse) || !IsSummonCorpse(corpse) ||
                !ZoneCombat.InCastRange(player.X, player.Y, corpse.X, corpse.Y, Math.Max(template.CastRange, 400) + ZoneCombat.HitRangeTolerance))
            {
                IncorrectTarget(session);
                return null;
            }
        }

        if (row.ItemId > 0 && row.ItemCount > 0 && (player.Inventory?.CountOf(row.ItemId) ?? 0) < row.ItemCount)
        {
            SelfLine(session, SysMsgNotEnoughItems, "There are not enough necessary items to use the skill.");
            return null;
        }

        // Cast on self; a corpse target is re-checked when the cast lands (the deferred path wants a living target).
        StartStaticCast(session, player, player, request.SkillId, template, levelData, level, now, (_, at) =>
        {
            if (player.Dead || SummonOf(player) != null)
                return;
            if (corpse != null && (!_byId.ContainsKey(corpse.ObjectId) || !IsSummonCorpse(corpse)))
            {
                IncorrectTarget(session);
                return;
            }
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

            // Mobius Summon on a CORPSE_MOB target: the body is used up.
            if (corpse != null)
                DespawnNpc(corpse);
            SpawnServitor(player, skill, row, level, at);
        });
        return null;
    }

    static bool IsSummonCorpse(ZoneEntity e) =>
        e.Kind == ZoneEntityKind.Npc && e.Summon == null && e.CurHp <= 0 && e.DecayAtMs > 0;

    ZoneEntity? SpawnServitor(ZoneEntity owner, SummonSkill skill, SummonLevel row, int skillLevel, long now)
    {
        _summonTemplates ??= NpcCatalog.LoadDefault();
        if (!_summonTemplates.ById.TryGetValue(row.NpcId, out var template))
        {
            ZoneLog.Info($"summon skip unknown npcId={row.NpcId}");
            return null;
        }

        int objectId = AllocateQuestNpcObjectId();
        // Mobius Summon effect: spawnMe(owner + 20, owner + 20, owner z).
        int x = owner.X + 20, y = owner.Y + 20, z = owner.Z;
        if (_geo.HasGeo(x, y))
            z = _geo.GetHeight(x, y, z);
        long interval = row.UpkeepIntervalSec > 0 ? row.UpkeepIntervalSec * 1000L : 0;
        if (row.UpkeepItemId > 0 && interval == 0)
            interval = SummonCatalog.DefaultUpkeepIntervalSec * 1000L;
        var entity = new ZoneEntity
        {
            ObjectId = objectId,
            Kind = ZoneEntityKind.Npc,
            InstanceId = owner.InstanceId,
            Name = template.Name,
            Title = owner.Name,
            NpcType = template.Type,
            X = x,
            Y = y,
            Z = z,
            Heading = owner.Heading,
            NpcId = template.NpcId,
            Attackable = false,
            MaxHp = template.Hp,
            CurHp = template.Hp,
            MaxMp = template.Mp,
            CurMp = template.Mp,
            Level = template.Level,
            PAtk = template.PAtk,
            HpRegen = template.HpRegen,
            MpRegen = template.MpRegen,
            PDef = template.PDef,
            PAtkSpd = template.AttackSpeed,
            BaseAccuracy = template.Accuracy,
            BaseEvasion = template.Evasion,
            BaseCritical = template.Critical,
            RandomDamage = template.RandomDamage,
            Dex = template.Dex,
            ShieldDef = template.ShieldDef,
            ShieldRate = template.ShieldRate,
            MAtk = template.MAtk,
            MDef = template.MDef,
            AttackRange = template.AttackRange,
            AttackType = template.AttackType,
            Rhand = template.Rhand,
            Lhand = template.Lhand,
            Aggressive = false,
            DefenceTraits = template.DefenceTraits,
            RunSpeed = template.RunSpeed,
            WalkSpeed = template.WalkSpeed,
            Running = true,
            HomeSet = false,
            Summon = new SummonState
            {
                OwnerObjectId = owner.ObjectId,
                Kind = SummonKindNet.Servitor,
                SkillId = skill.SkillId,
                SkillLevel = skillLevel,
                LifeTotalMs = row.LifeTimeSec * 1000L,
                LifeRemainingMs = row.LifeTimeSec * 1000L,
                UpkeepItemId = row.UpkeepItemId,
                UpkeepCount = row.UpkeepCount,
                UpkeepIntervalMs = interval,
                UpkeepRemainingMs = interval,
                ExpMultiplier = row.ExpMultiplier,
                Skills = _summonSkills.SkillsOf(template.NpcId),
                NextLifeTickMs = now + SummonLifeTickMs
            }
        };
        _byId.Add(objectId, entity);
        _aoi.Enter(objectId, x, y);
        _summonIds.Add(objectId);
        owner.SummonObjectId = objectId;
        FanoutNewNpc(entity);
        SendSummonInfo(entity);
        ZoneLog.Info($"summon spawn owner={owner.ObjectId} skill={skill.SkillId} lv{skillLevel} npc={template.NpcId} object={objectId}");
        return entity;
    }

    // ---------------------------------------------------------------- packets

    SummonInfoNetMsg BuildSummonInfo(ZoneEntity s)
    {
        var st = s.Summon!;
        var info = new SummonInfoNetMsg
        {
            ObjectId = s.ObjectId,
            Kind = st.Kind,
            NpcId = s.NpcId,
            Name = s.Name,
            Level = s.Level,
            Hp = s.CurHp,
            MaxHp = s.MaxHp,
            Mp = s.CurMp,
            MaxMp = s.MaxMp,
            Feed = (int)Math.Max(0, st.LifeRemainingMs / 1000),
            MaxFeed = (int)(st.LifeTotalMs / 1000),
            PAtk = (int)Math.Round(StatMods(s).Apply(CombatStat.PAtk, s.PAtk)),
            PDef = (int)Math.Round(s.PDef),
            MAtk = (int)Math.Round(s.MAtk),
            MDef = (int)Math.Round(s.MDef),
            Accuracy = (int)Math.Round(NpcAccuracy(s)),
            Evasion = (int)Math.Round(NpcEvasion(s)),
            Critical = NpcCritPermille(s) / 10,
            PAtkSpd = NpcPAtkSpd(s),
            CastSpd = 333,
            RunSpeed = (int)s.RunSpeed,
            Following = st.Following,
            TargetObjectId = st.TargetId,
            SkillIds = st.Skills.Select(static k => k.SkillId).ToArray(),
            Dead = s.Dead || s.CurHp <= 0
        };
        if (st.Pet is { } pet)
            FillPetInfo(info, s, pet);
        return info;
    }

    void SendSummonInfo(ZoneEntity summon)
    {
        if (NetBridge == null || OwnerOf(summon) is not { Session: not null } owner)
            return;
        NetBridge.FanoutSummonInfo([owner.ObjectId], BuildSummonInfo(summon));
    }

    List<int> KnownWatchers(int objectId) => CopyNetWatcherIds(objectId);

    // ---------------------------------------------------------------- removal / death

    /// <summary>Reason: 0 dismissed, 1 lifetime, 2 upkeep, 3 died, 4 owner left.</summary>
    void RemoveSummon(ZoneEntity summon, int reason)
    {
        var st = summon.Summon;
        if (st == null)
            return;
        int id = summon.ObjectId;
        _summonIds.Remove(id);
        if (!_byId.ContainsKey(id))
            return;
        var owner = OwnerOf(summon);
        if (owner != null && owner.SummonObjectId == id)
            owner.SummonObjectId = 0;
        SetMoveActive(summon, false);
        summon.MovePath.Clear();
        SetPendingHit(summon, null);
        _aoi.Leave(id);
        _byId.Remove(id);

        // Every player, not only watchers: a target or auto-attack set before the summon left view must be cleared.
        List<int>? watchers = null;
        foreach (var player in Players())
        {
            if (player.Session == null)
                continue;
            if (player.Knownlist.Remove(id))
            {
                UntrackWatcher(player.ObjectId, id);
                (watchers ??= []).Add(player.ObjectId);
            }
            if (player.Session.SelectedTargetId == id)
                player.Session.SelectedTargetId = 0;
            if (player.Intent.AutoAttackTargetId == id)
                player.Intent.AutoAttackTargetId = 0;
        }
        if (watchers is { Count: > 0 })
            NetBridge?.FanoutEntityDespawn(watchers, id);
        if (owner?.Session != null)
        {
            NetBridge?.FanoutSummonGone([owner.ObjectId], new SummonGoneNetMsg { ObjectId = id, Kind = st.Kind, Reason = reason });
            if (st.Kind == SummonKindNet.Pet)
                SendPlayerActionState(owner);
        }
        ZoneLog.Info($"summon gone object={id} owner={st.OwnerObjectId} reason={reason}");
    }

    void SummonDied(ZoneEntity summon, ZoneEntity? killer, long now)
    {
        var st = summon.Summon!;
        summon.CurHp = 0;
        summon.Dead = true;
        st.TargetId = 0;
        st.PendingSkillId = 0;
        st.MoveOrder = false;
        SetMoveActive(summon, false);
        summon.MovePath.Clear();
        SetPendingHit(summon, null);
        st.DecayAtMs = now + SummonCorpseMs;
        if (st.Pet is { } pet && OwnerOf(summon) is { } petOwner)
            PetDied(petOwner, summon, pet, now);
        var watchers = KnownWatchers(summon.ObjectId);
        foreach (int w in watchers)
        {
            if (_byId.TryGetValue(w, out var p) && p.Session is { } ws && p.Intent.AutoAttackTargetId == summon.ObjectId)
                p.Intent.AutoAttackTargetId = 0;
        }
        if (watchers.Count > 0)
            NetBridge?.FanoutDie(watchers, summon.ObjectId, killer == null ? 0 : CreditOf(killer).ObjectId);
        SendSummonInfo(summon);
    }

    /// <summary>Hit on a summon (melee, skill): HP, bars to the owner, PvP for a player attacker, death.</summary>
    void DamageSummon(ZoneEntity attacker, ZoneEntity summon, int damage, long now)
    {
        DuelNoteOutsideHit(attacker, summon);
        attacker.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;
        summon.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;
        if (damage <= 0 || summon.Dead)
            return;
        summon.CurHp = Math.Max(0, summon.CurHp - damage);
        FanoutVitals(summon, StatusFor(summon));
        SyncNeedRegen(summon);
        if (summon.CurHp <= 0)
            SummonDied(summon, attacker, now);
        else
            SendSummonInfo(summon);
    }

    void UnsummonOnLeave(ZoneEntity player)
    {
        if (SummonOf(player) is { } summon)
        {
            if (summon.Summon?.Pet != null)
                StorePet(player, summon);
            RemoveSummon(summon, 4);
        }
        player.SummonObjectId = 0;
    }

    /// <summary>Mobius <c>Player.teleToLocation</c> takes the summon along.</summary>
    void SummonFollowTeleport(ZoneEntity owner)
    {
        if (SummonOf(owner) is not { } summon || summon.Dead)
            return;
        var st = summon.Summon!;
        st.TargetId = 0;
        st.PendingSkillId = 0;
        st.MoveOrder = false;
        SetMoveActive(summon, false);
        summon.MovePath.Clear();
        summon.InstanceId = owner.InstanceId;
        summon.X = owner.X + 20;
        summon.Y = owner.Y + 20;
        summon.Z = owner.Z;
        _aoi.Move(summon.ObjectId, summon.X, summon.Y);
        RefreshNpcWatchers(summon);
        BroadcastNpcMove(summon, summon.X, summon.Y, summon.Z);
    }

    // ---------------------------------------------------------------- tick

    void TickSummons(long now)
    {
        if (_summonIds.Count == 0)
            return;
        foreach (int id in _summonIds.ToArray())
        {
            if (!_byId.TryGetValue(id, out var s) || s.Summon is not { } st)
            {
                _summonIds.Remove(id);
                continue;
            }
            var owner = OwnerOf(s);
            if (owner?.Session == null)
            {
                RemoveSummon(s, 4);
                continue;
            }
            if (st.Pet is { } pet && owner.Inventory?.Find(pet.ControlObjectId) == null)
            {
                // The collar left the owner's bag (traded, dropped, destroyed): the pet goes back.
                UnsummonPet(owner, s, 0);
                continue;
            }
            if (s.Dead || s.CurHp <= 0)
            {
                if (st.DecayAtMs > 0 && now >= st.DecayAtMs)
                {
                    if (st.Pet is { } deadPet)
                        PetDecayed(owner, s, deadPet);
                    else
                        RemoveSummon(s, 3);
                }
                continue;
            }
            if (st.Pet is { } fedPet)
            {
                if (now >= fedPet.NextFeedMs)
                {
                    fedPet.NextFeedMs = now + PetFeedTickMs;
                    if (!TickPetFeed(owner, s, fedPet, now))
                        continue;
                }
                if (fedPet.Fed <= 0)
                    continue; // Mobius: a starving pet does not obey (no attack / follow)
                if (ThinkPetPickup(owner, s, fedPet, now))
                    continue;
            }
            if (now >= st.NextLifeTickMs)
            {
                st.NextLifeTickMs = now + SummonLifeTickMs;
                if (!TickSummonLife(owner, s, st))
                    continue;
            }
            ThinkSummon(owner, s, st, now);
        }
    }

    /// <summary>Mobius <c>Servitor.run</c>: −5 s of life, upkeep item on its interval, SetSummonRemainTime.</summary>
    bool TickSummonLife(ZoneEntity owner, ZoneEntity s, SummonState st)
    {
        st.LifeRemainingMs -= SummonLifeTickMs;
        var session = owner.Session!;
        if (st.LifeRemainingMs < 0)
        {
            SelfLine(session, SysMsgServitorPassedAway, "Servitor passed away.");
            RemoveSummon(s, 1);
            return false;
        }
        if (st.UpkeepIntervalMs > 0 && st.UpkeepItemId > 0 && st.UpkeepCount > 0)
        {
            st.UpkeepRemainingMs -= SummonLifeTickMs;
            if (st.UpkeepRemainingMs <= 0)
            {
                var inventory = owner.Inventory;
                var changes = new List<InventoryUpdateEntryMsg>();
                if (inventory != null && Take(inventory, st.UpkeepItemId, st.UpkeepCount, changes))
                {
                    FanInventory(session, Collapse(changes));
                    SaveInventory(session, inventory);
                    string item = _game.Items.TryGet(st.UpkeepItemId, out var def) && def != null ? def.Name : st.UpkeepItemId.ToString();
                    SelfLine(session, SysMsgSummonedMonsterUses, $"A summoned monster uses {item}.", item);
                    st.UpkeepRemainingMs = st.UpkeepIntervalMs;
                }
                else
                {
                    SelfLine(session, SysMsgServitorUpkeepFailed,
                        "Since you do not have enough items to maintain the servitor's stay, the servitor will disappear.");
                    RemoveSummon(s, 2);
                    return false;
                }
            }
        }
        SendSummonInfo(s);
        return true;
    }

    void ThinkSummon(ZoneEntity owner, ZoneEntity s, SummonState st, long now)
    {
        if (IsActionBlocked(s) || IsImmobilized(s) || s.CastingUntilMs > now)
            return;

        ZoneEntity? target = null;
        if (st.TargetId != 0)
        {
            if (_byId.TryGetValue(st.TargetId, out var t) && ValidSummonTarget(owner, s, t))
                target = t;
            else
            {
                st.TargetId = 0;
                st.PendingSkillId = 0;
                SendSummonInfo(s);
            }
        }

        if (target != null)
        {
            if (st.PendingSkillId != 0)
            {
                TrySummonSkill(s, st, target, now);
                return;
            }
            int reach = AttackReachAgainst(s, target);
            if (!ZoneCombat.Within(s.X, s.Y, target.X, target.Y, reach) || !CanSeeTarget(s, target))
            {
                SummonApproach(s, target, reach - ZoneCombat.ApproachSlack, now);
                return;
            }
            if (s.MoveActive)
            {
                StopTimedMove(s);
                BroadcastNpcMove(s, s.X, s.Y, s.Z);
            }
            if (now < st.NextAttackMs)
                return;
            SummonSwing(s, st, target, now);
            return;
        }

        if (st.MoveOrder)
        {
            if (!s.MoveActive)
                st.MoveOrder = false;
            return;
        }
        if (!st.Following || owner.InstanceId != s.InstanceId)
            return;
        if (!ZoneCombat.Within(s.X, s.Y, owner.X, owner.Y, SummonFollowRange))
            SummonApproach(s, owner, SummonFollowStop, now);
    }

    bool ValidSummonTarget(ZoneEntity owner, ZoneEntity s, ZoneEntity t) =>
        !ReferenceEquals(t, s) && !ReferenceEquals(t, owner) && !t.Dead && t.CurHp > 0 &&
        t.Kind is ZoneEntityKind.Npc or ZoneEntityKind.Player && t.InstanceId == s.InstanceId &&
        ZoneCombat.Within(s.X, s.Y, t.X, t.Y, SummonMaxChase);

    void SummonApproach(ZoneEntity s, ZoneEntity target, int stopAt, long now)
    {
        double dx = target.X - s.X, dy = target.Y - s.Y;
        double dist = Math.Sqrt(dx * dx + dy * dy);
        stopAt = Math.Max(10, stopAt);
        if (dist <= stopAt)
            return;
        int destX = (int)Math.Round(target.X - dx / dist * stopAt);
        int destY = (int)Math.Round(target.Y - dy / dist * stopAt);
        long ddx = destX - (long)s.MoveDestX, ddy = destY - (long)s.MoveDestY;
        if (s.MoveActive && ddx * ddx + ddy * ddy <= 40L * 40L)
            return;
        StartNpcMove(s, destX, destY, target.Z, now);
    }

    void SummonSwing(ZoneEntity s, SummonState st, ZoneEntity target, long now)
    {
        int atkMs = ZoneCombat.AttackTimeMs(NpcPAtkSpd(s));
        st.NextAttackMs = now + atkMs;
        var (damage, flags) = NpcMeleeRoll(s, target);
        s.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;
        var attack = new AttackMsg
        {
            AttackerObjectId = s.ObjectId,
            TargetObjectId = target.ObjectId,
            Damage = damage,
            Flags = flags,
            AttackerX = s.X,
            AttackerY = s.Y,
            AttackerZ = s.Z,
            TargetX = target.X,
            TargetY = target.Y,
            TargetZ = target.Z
        };
        FanoutAttack(s, target, attack);
        _meleeHits.Add(new PendingMeleeHit(s.ObjectId, target.ObjectId, damage, now + atkMs / 2, flags));
    }

    /// <summary>Ordered skill: run into cast range, then cast it like an NPC attack skill (owner gets the credit).</summary>
    void TrySummonSkill(ZoneEntity s, SummonState st, ZoneEntity target, long now)
    {
        int skillId = st.PendingSkillId;
        var known = st.Skills.FirstOrDefault(k => k.SkillId == skillId);
        if (known.SkillId == 0 || !_skillCombat.TryGet(skillId, out var combat) || combat == null)
        {
            st.PendingSkillId = 0;
            return;
        }
        int level = Math.Clamp(known.Level, 1, Math.Max(1, combat.Levels));
        if (s.SkillReuseUntilMs.TryGetValue(skillId, out long until) && until > now)
        {
            st.PendingSkillId = 0;
            return;
        }
        if (s.CurMp < combat.MpAt(level) + combat.MpInitialAt(level))
        {
            st.PendingSkillId = 0;
            return;
        }
        int range = combat.CastRange > 0
            ? SkillCastReach(combat.CastRange, s, target)
            : AttackReachAgainst(s, target);
        if (!ZoneCombat.Within(s.X, s.Y, target.X, target.Y, range) || !CanSeeTarget(s, target))
        {
            SummonApproach(s, target, Math.Max(10, range - ZoneCombat.ApproachSlack), now);
            return;
        }
        if (s.MoveActive)
        {
            StopTimedMove(s);
            BroadcastNpcMove(s, s.X, s.Y, s.Z);
        }
        st.PendingSkillId = 0;
        if (CastNpcAttackSkill(s, target, new NpcAiSkill(skillId, level), combat, now))
        {
            st.NextAttackMs = s.CastingUntilMs;
            s.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;
        }
    }

    // ---------------------------------------------------------------- commands

    public Task<string?> TrySummonCommandAsync(ZoneSession session, SummonCommandNetMsg msg, CancellationToken ct)
    {
        if (!IsLive(session))
            return Task.FromResult<string?>("world");
        string? error = TrySummonCommand(session, msg);
        return Task.FromResult(error);
    }

    string? TrySummonCommand(ZoneSession session, SummonCommandNetMsg msg)
    {
        if (!_byId.TryGetValue(session.ObjectId, out var owner) || owner.Session == null)
            return "world";
        if (SummonOf(owner) is not { Summon: { } st } s)
            return null; // Mobius RequestActionUse with no summon: ActionFailed only.
        long now = NowUnixMs();
        bool dead = s.Dead || s.CurHp <= 0;
        if (dead && msg.Kind != SummonCommandKind.Unsummon)
            return null;
        if (owner.Dead && msg.Kind != SummonCommandKind.Unsummon)
            return null;
        var pet = st.Pet;
        if (pet != null && pet.Fed <= 0 && msg.Kind is not (SummonCommandKind.Unsummon or SummonCommandKind.TakeItem or SummonCommandKind.GiveItem or SummonCommandKind.UseItem))
        {
            SelfLine(session, SysMsgPetUnresponsive, "Your pet/servitor is unresponsive and will not obey any orders.");
            return null;
        }
        if (pet != null && msg.Kind != SummonCommandKind.PickUp)
            pet.PickupTargetId = 0;

        switch (msg.Kind)
        {
            case SummonCommandKind.Follow:
                st.Following = true;
                st.TargetId = 0;
                st.PendingSkillId = 0;
                st.MoveOrder = false;
                break;
            case SummonCommandKind.Stay:
                st.Following = false;
                st.TargetId = 0;
                st.PendingSkillId = 0;
                st.MoveOrder = false;
                HaltSummon(s);
                break;
            case SummonCommandKind.Stop:
                st.TargetId = 0;
                st.PendingSkillId = 0;
                st.MoveOrder = false;
                HaltSummon(s);
                break;
            case SummonCommandKind.Move:
                st.TargetId = 0;
                st.PendingSkillId = 0;
                st.Following = false;
                st.MoveOrder = true;
                StartNpcMove(s, msg.X, msg.Y, msg.Z, now);
                break;
            case SummonCommandKind.Attack:
            case SummonCommandKind.UseSkill:
                {
                    int targetId = msg.TargetObjectId > 0 ? msg.TargetObjectId : session.SelectedTargetId;
                    if (!_byId.TryGetValue(targetId, out var target) || !SummonTargetAllowed(session, owner, s, target, msg.Forced))
                    {
                        SelfLine(session, SysMsgSummonWrongTarget, "The target of the summoned monster is wrong.");
                        return null;
                    }
                    if (msg.Kind == SummonCommandKind.UseSkill)
                    {
                        if (!st.Skills.Any(k => k.SkillId == msg.SkillId))
                            return null;
                        st.PendingSkillId = msg.SkillId;
                    }
                    else
                    {
                        st.PendingSkillId = 0;
                    }
                    st.TargetId = target.ObjectId;
                    st.MoveOrder = false;
                    break;
                }
            case SummonCommandKind.Unsummon when pet != null:
                // Mobius RequestActionUse 19.
                if (dead)
                {
                    SelfLine(session, SysMsgDeadPetCannotReturn, "A dead pet cannot be sent back.");
                    return null;
                }
                if (s.CombatUntilUnixMs > now)
                {
                    SelfLine(session, SysMsgPetInBattle, "A pet cannot be sent back during battle.");
                    return null;
                }
                if (pet.Fed * 100L < PetReturnMinFedPercent * (long)pet.Template.At(s.Level).MaxMeal)
                {
                    SelfLine(session, SysMsgHungryPetNoReturn, "You may not restore a hungry pet.");
                    return null;
                }
                UnsummonPet(owner, s, 0);
                return null;
            case SummonCommandKind.PickUp when pet != null:
                if (dead || !_byId.TryGetValue(msg.TargetObjectId > 0 ? msg.TargetObjectId : session.SelectedTargetId, out var ground) ||
                    ground.Kind != ZoneEntityKind.GroundItem)
                    return null;
                st.TargetId = 0;
                st.PendingSkillId = 0;
                st.MoveOrder = false;
                pet.PickupTargetId = ground.ObjectId;
                break;
            case SummonCommandKind.GiveItem when pet != null:
                GiveItemToPet(session, owner, s, pet, msg.ItemObjectId, msg.Count);
                return null;
            case SummonCommandKind.TakeItem when pet != null:
                TakeItemFromPet(session, owner, s, pet, msg.ItemObjectId, msg.Count);
                return null;
            case SummonCommandKind.UseItem when pet != null:
                PetUseItem(session, owner, s, pet, msg.ItemObjectId);
                return null;
            case SummonCommandKind.Unsummon:
                if (!dead && s.CombatUntilUnixMs > now)
                {
                    SelfLine(session, SysMsgServitorInBattle, "A servitor whom is engaged in battle cannot be de-activated.");
                    return null;
                }
                RemoveSummon(s, 0);
                return null;
            default:
                // Pet-only commands (pick up, pet inventory) are not for servitors.
                return null;
        }

        SendSummonInfo(s);
        return null;
    }

    void HaltSummon(ZoneEntity s)
    {
        if (!s.MoveActive)
            return;
        StopTimedMove(s);
        BroadcastNpcMove(s, s.X, s.Y, s.Z);
    }

    /// <summary>
    /// Mobius <c>Summon.doAttack</c> / <c>useMagic</c> gates for an owner's order: no self, no owner, a living creature
    /// in sight; a monster always, a peaceful NPC never; a player (or another player's summon) only when the owner could
    /// attack it (Ctrl for a white player, no peace zone, no protected level).
    /// </summary>
    bool SummonTargetAllowed(ZoneSession session, ZoneEntity owner, ZoneEntity s, ZoneEntity target, bool forced)
    {
        if (!ValidSummonTarget(owner, s, target))
            return false;
        var victim = CreditOf(target);
        if (ReferenceEquals(victim, owner))
            return false;
        if (victim.Kind == ZoneEntityKind.Npc)
            return ZoneCombat.TakesNpcCombatDamage(target.NpcType, target.Attackable) || target.SiegeNpc != null;
        if (!forced && NeedsForce(owner, victim))
            return false;
        if (HostilePlayableRefused(session, owner, victim, badSkill: false))
            return false;
        if (PeacePlayableRefused(session, owner, victim, notify: true))
            return false;
        return true;
    }

    // ---------------------------------------------------------------- test hooks

    internal ZoneEntity? SummonOfForTest(ZoneEntity player)
    {
        using (_affinity.Step())
            return SummonOf(player);
    }

    internal bool TryGetEntityForTest(int objectId, out ZoneEntity? entity)
    {
        using (_affinity.Step())
        {
            bool found = _byId.TryGetValue(objectId, out var e);
            entity = e;
            return found;
        }
    }

    internal void TickSummonsForTest()
    {
        using (_affinity.Step())
            TickSummons(NowUnixMs());
    }

    internal ZoneEntity? SpawnServitorForTest(ZoneEntity owner, int skillId, int level)
    {
        using (_affinity.Step())
        {
            var skill = _summonSkills.Find(skillId) ?? throw new ArgumentException($"no summon skill {skillId}");
            return SpawnServitor(owner, skill, skill.At(level)!, level, NowUnixMs());
        }
    }
}
