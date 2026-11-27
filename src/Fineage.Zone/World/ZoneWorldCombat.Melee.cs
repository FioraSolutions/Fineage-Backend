using Fineage.Game;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Drops;
using Fineage.Zone.Simulation;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    void TickMeleeHits(long now)
    {
        if (_meleeHits.Count == 0)
            return;
        var due = _meleeHits.Where(h => h.DueUnixMs <= now).ToList();
        if (due.Count == 0)
            return;
        _meleeHits.RemoveAll(h => h.DueUnixMs <= now);
        foreach (var hit in due)
            ResolveMeleeHit(hit, now);
    }

    void ResolveMeleeHit(PendingMeleeHit hit, long now)
    {
        if (!_byId.TryGetValue(hit.AttackerId, out var attacker) || attacker.CurHp <= 0 || attacker.Dead)
            return;
        // Mobius onHitTimer: recharge auto shots at hit time — also when the target died or left reach meanwhile.
        if (hit.RechargeShots && attacker.Kind == ZoneEntityKind.Player)
            RechargeAutoShots(attacker);
        if (!_byId.TryGetValue(hit.TargetId, out var target) || target.CurHp <= 0 || target.Dead)
            return;
        // Mobius onHitTimer has no second range gate (the swing already started in range). We still drop the
        // hit if the target has left the same attack reach used to start the swing — no extra HitRangeTolerance,
        // which previously let a dagger land from ~120 instead of weapon 40 + collisions.
        int reach = AttackReachAgainst(attacker, target);
        if (!ZoneCombat.Within(attacker.X, attacker.Y, target.X, target.Y, reach))
            return;

        // Mobius Creature.doAttack: any swing at a playable (hit or miss) runs updatePvPStatus on the players behind
        // both sides (a summon acts for its owner, a hit on a summon is a hit on its owner).
        var attackerPc = CreditOf(attacker);
        var targetPc = CreditOf(target);
        if (attackerPc.Kind == ZoneEntityKind.Player && targetPc.Kind == ZoneEntityKind.Player && !ReferenceEquals(attackerPc, targetPc))
            UpdatePvPStatus(attackerPc, targetPc, now);

        SendVictimHitLines(attacker, target, hit.Damage, hit.Damage <= 0 ? (byte)(hit.Flags | ZoneCombat.HitFlagMiss) : hit.Flags);

        if (hit.Damage <= 0)
        {
            // Miss (Attack carried HitFlagMiss): no HP change / StatusUpdate, but the fight and hate still start.
            attacker.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;
            target.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;
            if (target.Kind == ZoneEntityKind.Npc && target.Summon == null)
                AddHate(target, attacker);
            return;
        }

        if (target.Summon != null)
        {
            DamageSummon(attacker, target, hit.Damage, now);
            return;
        }

        if (target.Kind == ZoneEntityKind.Npc)
        {
            DamageNpcBy(attacker, target, hit.Damage, now);
            return;
        }

        if (DuelBlocksHit(attacker, target))
            return;
        // Mobius Creature.isInvul: admin_invul / setInvul blocks HP loss (hits still start combat / hate above).
        if (target.Invulnerable)
            return;
        // Mobius CreatureAI.checkTargetLostOrDead (FAKE_DEATH_DAMAGE_STAND=true): a melee hit on a playing-dead pawn
        // stands it up before the damage lands.
        if (target.FakeDeath)
            WakeUpFromFakeDeath(target, "damage");
        ApplyTargetDamage(target, attacker, hit.Damage);
        ApplyReflectAndAbsorb(attacker, target, hit.Damage);
        if (_olympiadArena.Contains(attacker.ObjectId))
            _olympiad.Board.NoteDamage(attacker.ObjectId, hit.Damage);

        target.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;
        FanoutVitals(target, PlayerVitalsStatus(target));
        // Mobius onCreatureDamageReceived → TriggerSkillByDamage (Mirage 445 cancels the attacker's target).
        RunDamageTriggers(target, attacker, hit.Damage);
        if (target.CurHp > 0)
            MaybeBreakCast(target, hit.Damage);
        if (target.CurHp <= 0)
        {
            if (attacker.Summon == null)
            {
                attacker.HateList.Remove(target.ObjectId);
                SetAggroTarget(attacker, MostHated(attacker));
            }
            else
            {
                attacker.Summon.TargetId = 0;
            }
            if (target.Session != null)
                target.Intent.AutoAttackTargetId = 0;
            ApplyPlayerDeath(target, attacker.ObjectId);
        }
    }

    /// <summary>
    /// Damage on an NPC from a player or a summon (melee or skill). A summon's damage, quest attack and kill count for
    /// its owner (Mobius <c>asPlayer()</c>), while the monster hates the summon itself.
    /// </summary>
    void DamageNpcBy(ZoneEntity attacker, ZoneEntity target, int damage, long now)
    {
        // Mobius Creature.reduceCurrentHp: once _isDead is set, later hits in the same tick (queued melee,
        // cubic/signet/skill messages at the stage barrier) are not applied to the corpse at all.
        if (target.Dead)
            return;
        var credit = CreditOf(attacker);
        DuelNoteOutsideHit(attacker, target);
        // Castle gates / walls, towers and headquarters have their own Mobius reduceHp rules.
        if (SiegeNpcHit(credit, target, damage, now))
            return;
        // Mobius FolkStatus.reduceHp: swing may land, but Folk/Merchant/Trainer take no damage and never hate.
        if (!ZoneCombat.TakesNpcCombatDamage(target.NpcType, target.Attackable))
        {
            attacker.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;
            return;
        }

        // A still-closed treasure box explodes on the first hit (Unlock skills go through OpenChest instead).
        if (TryExplodeTreasureBox(attacker, target, now))
            return;

        ApplyTargetDamage(target, attacker, damage);
        ApplyReflectAndAbsorb(attacker, target, damage);
        FanoutVitals(target, StatusFor(target));
        RecordDamage(target, credit, damage);
        TryApplyRaidCurse(target, attacker, magicSkill: false, now);
        ApplyQuestAttack(credit, target, byPet: attacker.Summon?.Pet != null);
        AddHate(target, attacker, DamageHate(damage, target.Level));
        if (IsGuard(target))
            CallGuardHelp(target, attacker);
        attacker.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;
        if (target.CurHp <= 0)
        {
            if (attacker.Summon != null)
                attacker.Summon.TargetId = 0;
            if (RemoveDeadNpc(target, killer: credit))
            {
                TryGrantFestivalBloodOffering(credit, target);
                GrantKillReward(credit, target);
            }
        }
    }

    /// <summary>
    /// Mobius <c>Creature.doDie</c>: "killing is only possible one time" — the synchronized guard sets the dead flag
    /// before rewards, drops or decay, so a second damage source in the same tick cannot roll loot or pay XP again.
    /// A respawn materializes a fresh entity, so the flag does not leak into the next lifecycle.
    /// </summary>
    /// <returns><c>true</c> when this call performed the death transition; <c>false</c> when the NPC was already dead.</returns>
    bool RemoveDeadNpc(ZoneEntity npc, ZoneEntity? killer = null)
    {
        if (npc.Dead)
            return false;
        npc.Dead = true;
        npc.CurHp = 0;
        // Kill quest steps are not applied here. IQuestKillHook is the later combat entry.
        if (npc.SiegeNpc != null)
            OnSiegeNpcDied(npc);
        NoteBossDeath(npc);
        NoteRaidBossPoints(npc, killer);
        int objectId = npc.ObjectId;
        int npcId = npc.NpcId;
        int x = npc.X;
        int y = npc.Y;
        int z = npc.Z;

        var watchers = WatchingPlayers(objectId);

        // Mobius Creature.doDie -> stopAllEffects(): a corpse carries no debuffs. Clearing them before the Die packet
        // also empties the watchers' abnormal bar, so the client's stun/sleep pose no longer holds the pawn on the
        // Idle state and the death animation actually plays (Zariche/herbs included).
        if (npc.Abnormals.Count > 0)
        {
            npc.Abnormals.Clear();
            _byId.Leave(npc, ActiveSets.WithAbnormals);
            FanoutAbnormal(npc);
        }

        // Mobius Creature.doDie -> broadcastPacket(new Die(this)): client plays the death animation.
        SetAggroTarget(npc, 0);
        npc.Chasing = false;
        List<int>? netWatchers = null;
        foreach (var watcher in watchers)
        {
            if (watcher.Session == null)
                continue;
            netWatchers ??= [];
            netWatchers.Add(watcher.ObjectId);
            if (watcher.Session.SelectedTargetId == objectId)
                watcher.Intent.AutoAttackTargetId = 0;
        }

        if (netWatchers is { Count: > 0 } && NetBridge != null)
        {
            NetBridge.FanoutDie(netWatchers, objectId, killer?.ObjectId ?? 0);
        }

        npc.SweepItems.Clear();
        bool raid = npc.NpcType is "RaidBoss" or "GrandBoss";
        int killerLevel = killer?.Level ?? npc.Level;
        if (npc.SpoilerObjectId != 0 && _dropCatalog.TryGet(npcId, out var definition))
        {
            foreach (var stack in DropRoller.RollCorpse(definition, Rng(_dropRandom, npc), _dropRates, npc.Level, killerLevel, raid))
                npc.SweepItems.Add(stack);
        }

        int dropNpcId = ChestDropNpcId(npcId, npc.ChestSpecialDrop);
        LastDeathDropNpcIdForTest = dropNpcId;
        int lootOwner = raid
            ? RaidLootOwnerObjectId(npc, killer, id => _byId.TryGetValue(id, out var e) ? e : null, _party.Of)
            : killer?.ObjectId ?? 0;
        SpawnDeathDrops(dropNpcId, x, y, z, killer, watchers, npc.Level, raid, seeded: npc.Seeded, lootOwnerObjectId: lootOwner);
        CheckCursedDropOnMobDeath(npc, killer, Rng(_dropRandom, npc));
        // Mobius DecayTaskManager: a seeded corpse gets the same extension as a spoiled one (time to harvest).
        bool spoiled = npc.SpoilerObjectId != 0 || npc.Seeded;
        npc.CorpseAwaitingSweep = npc.SweepItems.Count > 0;
        if (spoiled)
            ZoneLog.Info($"corpse object={objectId} spoiler={npc.SpoilerObjectId} sweep={npc.SweepItems.Count}");
        // Mobius DecayTaskManager: DefaultCorpseTime, + SpoiledCorpseExtendTime when spoiled; unswept spoil decays with the corpse.
        SetDecayAtMs(npc, NowUnixMs() + _spoilRules.CorpseMs(spoiled));
        if (npc.NpcType is "RaidBoss" or "GrandBoss")
        {
            foreach (int minionId in npc.MinionIds.ToArray())
            {
                if (_byId.TryGetValue(minionId, out var minion))
                    DespawnNpc(minion);
            }
        }

        return true;
    }

    void DespawnNpc(ZoneEntity npc)
    {
        int objectId = npc.ObjectId;
        _signets.Remove(objectId);

        _aoi.Leave(objectId);
        NoteNpcDespawned();
        npc.DropNetWatcherAudience();
        _byId.Remove(objectId);
        npc.SpoilerObjectId = 0;
        npc.ClearSeed();
        npc.SweepItems.Clear();
        npc.CorpseAwaitingSweep = false;
        SetDecayAtMs(npc, 0);
        npc.Chasing = false;
        SetAggroTarget(npc, 0);
        npc.LastAiTickMs = 0;
        ScheduleRespawn(objectId);

        // Every player, not only watchers: a target selected before the NPC left view must be cleared too.
        List<int>? netWatchers = null;
        foreach (var player in Players())
        {
            if (player.Session == null)
                continue;
            if (player.Knownlist.Remove(objectId))
            {
                UntrackWatcher(player.ObjectId, objectId);
                netWatchers ??= [];
                netWatchers.Add(player.ObjectId);
            }
            if (player.Session.SelectedTargetId == objectId)
                player.Session.SelectedTargetId = 0;
        }

        if (netWatchers is { Count: > 0 } && NetBridge != null)
        {
            NetBridge.FanoutEntityDespawn(netWatchers, objectId);
        }
    }

    internal int LastDeathDropNpcIdForTest { get; private set; }

    internal void DamageNpcByForTest(ZoneEntity attacker, ZoneEntity target, int damage)
    {
        using (_affinity.Step())
            DamageNpcBy(attacker, target, damage, NowUnixMs());
    }

    bool VisibleTo(int observerId, int otherId)
    {
        if (!SameInstance(observerId, otherId))
            return false;
        foreach (int id in _aoi.GetVisible(observerId))
        {
            if (id == otherId)
                return true;
        }

        return false;
    }
}
