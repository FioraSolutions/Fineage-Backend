using Fineage.Game;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Drops;

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
        if (!_byId.TryGetValue(hit.TargetId, out var target) || target.CurHp <= 0 || target.Dead)
            return;
        // Mobius onHitTimer: target ran out of reach during the swing -> no damage.
        int reach = (attacker.Kind == ZoneEntityKind.Npc ? ZoneCombat.MobAttackReach(attacker.AttackRange) : PlayerReach(attacker)) + ZoneCombat.HitRangeTolerance;
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
        target.CurHp = Math.Max(0, target.CurHp - hit.Damage);
        if (_olympiadArena.Contains(attacker.ObjectId))
            _olympiad.NoteDamage(attacker.ObjectId, hit.Damage);

        target.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;
        FanoutVitals(target, PlayerVitalsStatus(target));
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
                target.Session.AutoAttackTargetId = 0;
            ApplyPlayerDeath(target, attacker.ObjectId);
        }
    }

    /// <summary>
    /// Damage on an NPC from a player or a summon (melee or skill). A summon's damage, quest attack and kill count for
    /// its owner (Mobius <c>asPlayer()</c>), while the monster hates the summon itself.
    /// </summary>
    void DamageNpcBy(ZoneEntity attacker, ZoneEntity target, int damage, long now)
    {
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

        target.CurHp = Math.Max(0, target.CurHp - damage);
        FanoutVitals(target, StatusFor(target));
        RecordDamage(target, credit, damage);
        ApplyQuestAttack(credit, target, byPet: attacker.Summon?.Pet != null);
        AddHate(target, attacker, DamageHate(damage, target.Level));
        attacker.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;
        if (target.CurHp <= 0)
        {
            if (attacker.Summon != null)
                attacker.Summon.TargetId = 0;
            RemoveDeadNpc(target, killer: credit);
            GrantKillReward(credit, target);
        }
    }

    void RemoveDeadNpc(ZoneEntity npc, ZoneEntity? killer = null)
    {
        // Kill quest steps are not applied here. IQuestKillHook is the later combat entry.
        if (npc.SiegeNpc != null)
            OnSiegeNpcDied(npc);
        int objectId = npc.ObjectId;
        int npcId = npc.NpcId;
        int x = npc.X;
        int y = npc.Y;
        int z = npc.Z;

        var watchers = new List<ZoneEntity>();
        foreach (var player in Players())
        {
            if (player.Knownlist.Contains(objectId))
                watchers.Add(player);
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
                watcher.Session.AutoAttackTargetId = 0;
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
            foreach (var stack in DropRoller.RollCorpse(definition, _dropRandom, _dropRates, npc.Level, killerLevel, raid))
                npc.SweepItems.Add(stack);
        }

        SpawnDeathDrops(npcId, x, y, z, killer, watchers, npc.Level, raid, seeded: npc.Seeded);
        // Mobius DecayTaskManager: a seeded corpse gets the same extension as a spoiled one (time to harvest).
        bool spoiled = npc.SpoilerObjectId != 0 || npc.Seeded;
        npc.CorpseAwaitingSweep = npc.SweepItems.Count > 0;
        if (spoiled)
            ZoneLog.Info($"corpse object={objectId} spoiler={npc.SpoilerObjectId} sweep={npc.SweepItems.Count}");
        // Mobius DecayTaskManager: DefaultCorpseTime, + SpoiledCorpseExtendTime when spoiled; unswept spoil decays with the corpse.
        SetDecayAtMs(npc, _clock().ToUnixTimeMilliseconds() + _spoilRules.CorpseMs(spoiled));
        if (npc.NpcType is "RaidBoss" or "GrandBoss")
        {
            foreach (int minionId in npc.MinionIds.ToArray())
            {
                if (_byId.TryGetValue(minionId, out var minion))
                    DespawnNpc(minion);
            }
        }
    }

    void DespawnNpc(ZoneEntity npc)
    {
        int objectId = npc.ObjectId;

        _aoi.Leave(objectId);
        _byId.Remove(objectId);
        npc.SpoilerObjectId = 0;
        npc.ClearSeed();
        npc.SweepItems.Clear();
        npc.CorpseAwaitingSweep = false;
        SetDecayAtMs(npc, 0);
        npc.Chasing = false;
        SetAggroTarget(npc, 0);
        npc.LastAiTickMs = 0;
        ForgetActiveSets(objectId);
        ScheduleRespawn(objectId);

        List<int>? netWatchers = null;
        foreach (var player in Players())
        {
            if (player.Session == null)
                continue;
            if (player.Knownlist.Remove(objectId))
            {
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
