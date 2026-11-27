using Fineage.LoadTest.Bot;
using Fineage.LoadTest.Consistency;
using Fineage.LoadTest.Models;
using Fineage.Net.Contract;
using Xunit;

namespace Fineage.LoadTest.Tests;

public sealed class ConsistencyOracleTests
{
    [Fact]
    public void Tracker_flags_duplicate_spawn_without_despawn()
    {
        var log = new BotObservationLog(1);
        var spawn = new EntitySpawnMsg
        {
            EntityValue = 42,
            EntityGeneration = 1,
            Kind = EntityKindNet.Npc,
            Attackable = true,
            X = 1,
            Y = 2,
            Z = 3,
            MaxHp = 100,
            CurHp = 100
        };
        var at = DateTimeOffset.UtcNow;
        log.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(spawn), at);
        log.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(spawn), at.AddMilliseconds(10));
        log.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(spawn), at.AddMilliseconds(20));
        log.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(spawn), at.AddMilliseconds(30));

        var ent = log.Tracker.Entities[42];
        Assert.Equal(4, ent.SpawnCount);
        Assert.Equal(4, ent.SpawnsWithoutDespawn);
        Assert.Equal(0, ent.LastDespawnOrdinal);

        var report = ConsistencyOracle.Analyze([Bot(log)], new LoadScenario());
        Assert.Contains(report.Issues, i => i.Kind == ConsistencyKind.DuplicateSpawn && i.SubjectObjectId == 42);
    }

    [Fact]
    public void Respawn_after_despawn_does_not_count_as_a_duplicate_spawn()
    {
        var log = new BotObservationLog(1);
        var at = DateTimeOffset.UtcNow;
        var spawn = NetCodec.EncodeEntitySpawn(new EntitySpawnMsg
        {
            EntityValue = 42,
            EntityGeneration = 1,
            Kind = EntityKindNet.Npc,
            Attackable = true,
            MaxHp = 40,
            CurHp = 40
        });
        var despawn = NetCodec.EncodeEntityDespawn(new EntityDespawnMsg
        {
            EntityValue = 42,
            EntityGeneration = 1
        });
        for (int life = 0; life < 3; life++)
        {
            log.Record(NetOpcode.EntitySpawn, spawn, at.AddSeconds(life * 22));
            log.Record(NetOpcode.EntityDespawn, despawn, at.AddSeconds(life * 22 + 7));
        }

        log.Record(NetOpcode.EntitySpawn, spawn, at.AddSeconds(66));

        var ent = log.Tracker.Entities[42];
        Assert.Equal(4, ent.SpawnCount);
        Assert.Equal(1, ent.SpawnsWithoutDespawn);
        Assert.True(ent.LastDespawnOrdinal > 0);
        Assert.False(ent.Despawned);

        var report = ConsistencyOracle.Analyze([Bot(log)], new LoadScenario());
        Assert.DoesNotContain(report.Issues, i => i.Kind == ConsistencyKind.DuplicateSpawn);
    }

    [Fact]
    public void Four_spawns_after_a_closed_life_are_still_a_duplicate()
    {
        var log = new BotObservationLog(1);
        var at = DateTimeOffset.UtcNow;
        var spawn = NetCodec.EncodeEntitySpawn(new EntitySpawnMsg
        {
            EntityValue = 42,
            EntityGeneration = 1,
            Kind = EntityKindNet.Npc
        });
        log.Record(NetOpcode.EntitySpawn, spawn, at);
        log.Record(NetOpcode.EntityDespawn, NetCodec.EncodeEntityDespawn(new EntityDespawnMsg
        {
            EntityValue = 42,
            EntityGeneration = 1
        }), at.AddSeconds(1));
        for (int i = 0; i < 4; i++)
            log.Record(NetOpcode.EntitySpawn, spawn, at.AddSeconds(2 + i));

        Assert.Equal(4, log.Tracker.Entities[42].SpawnsWithoutDespawn);
        var report = ConsistencyOracle.Analyze([Bot(log)], new LoadScenario());
        Assert.Contains(report.Issues, i => i.Kind == ConsistencyKind.DuplicateSpawn);
    }

    [Fact]
    public void Status_update_sets_hp_from_interlude_attribute_ids()
    {
        var log = new BotObservationLog(2);
        log.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(new EntitySpawnMsg
        {
            EntityValue = 7,
            EntityGeneration = 1,
            Kind = EntityKindNet.Player,
            MaxHp = 200,
            CurHp = 200
        }), DateTimeOffset.UtcNow);

        log.Record(NetOpcode.StatusUpdate, NetCodec.EncodeStatusUpdate(new StatusUpdateNetMsg
        {
            EntityValue = 7,
            EntityGeneration = 1,
            AttributeIds = [Fineage.Protocol.InterludeStatus.CurHp],
            Values = [155]
        }), DateTimeOffset.UtcNow);

        Assert.Equal(155, log.Tracker.Entities[7].CurHp);
    }

    [Fact]
    public void Self_spawn_adopts_observer_and_picks_nearest_attackable_npc()
    {
        var log = new BotObservationLog(3);
        var at = DateTimeOffset.UtcNow;
        log.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(new EntitySpawnMsg
        {
            EntityValue = 1001,
            EntityGeneration = 1,
            Kind = EntityKindNet.Player,
            IsSelf = true,
            X = 0,
            Y = 0,
            Z = 0,
            MaxHp = 100,
            CurHp = 100
        }), at);
        log.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Npc(200, 800, 0)), at);
        log.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Npc(201, 100, 0)), at);
        log.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Npc(202, 5000, 0)), at);

        Assert.True(log.TryPickAttackTarget(out int target));
        Assert.Equal(201, target);
        Assert.Equal(100, log.LastPickedTargetDistance);
    }

    [Fact]
    public void Without_self_spawn_combat_targets_and_samples_stay_closed()
    {
        var log = new BotObservationLog(4);
        var at = DateTimeOffset.UtcNow;
        log.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(new EntitySpawnMsg
        {
            EntityValue = 1001,
            EntityGeneration = 1,
            Kind = EntityKindNet.Player,
            IsSelf = false,
            X = 0,
            Y = 0,
            MaxHp = 100,
            CurHp = 100
        }), at);
        log.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Npc(201, 100, 0)), at);

        Assert.False(log.TryPickAttackTarget(out _));
        log.RecordCommandSent(NetOpcode.UseSkill, at, targetObjectId: 201, skillId: 1177);
        log.Record(NetOpcode.MagicSkillUse, NetCodec.EncodeMagicSkillUse(new MagicSkillUseNetMsg
        {
            CasterObjectId = 1001,
            TargetObjectId = 201,
            SkillId = 1177,
            SkillLevel = 1
        }), at.AddMilliseconds(40));

        Assert.Equal(0, log.Tracker.SkillSampleCount);
    }

    [Fact]
    public void Self_spawn_pairs_use_skill_and_attack_with_own_fanout_only()
    {
        var log = new BotObservationLog(5);
        var at = DateTimeOffset.UtcNow;
        log.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(new EntitySpawnMsg
        {
            EntityValue = 1001,
            EntityGeneration = 1,
            Kind = EntityKindNet.Player,
            IsSelf = true,
            X = 0,
            Y = 0,
            MaxHp = 100,
            CurHp = 100
        }), at);
        log.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Npc(201, 80, 0)), at);

        log.RecordCommandSent(NetOpcode.UseSkill, at, targetObjectId: 201, skillId: 1177);
        log.Record(NetOpcode.MagicSkillUse, NetCodec.EncodeMagicSkillUse(new MagicSkillUseNetMsg
        {
            CasterObjectId = 9999,
            TargetObjectId = 201,
            SkillId = 1177,
            SkillLevel = 1
        }), at.AddMilliseconds(20));
        Assert.Equal(0, log.Tracker.SkillSampleCount);

        log.Record(NetOpcode.MagicSkillUse, NetCodec.EncodeMagicSkillUse(new MagicSkillUseNetMsg
        {
            CasterObjectId = 1001,
            TargetObjectId = 201,
            SkillId = 3,
            SkillLevel = 1
        }), at.AddMilliseconds(30));
        Assert.Equal(0, log.Tracker.SkillSampleCount);

        log.Record(NetOpcode.MagicSkillUse, NetCodec.EncodeMagicSkillUse(new MagicSkillUseNetMsg
        {
            CasterObjectId = 1001,
            TargetObjectId = 201,
            SkillId = 1177,
            SkillLevel = 1
        }), at.AddMilliseconds(50));
        Assert.Equal(1, log.Tracker.SkillSampleCount);

        log.RecordCommandSent(NetOpcode.AttackRequest, at.AddMilliseconds(60), targetObjectId: 201);
        log.Record(NetOpcode.AttackState, NetCodec.EncodeAttackState(new AttackStateMsg
        {
            AttackerValue = 1001,
            AttackerGeneration = 1,
            TargetValue = 201,
            TargetGeneration = 1,
            Damage = 12
        }), at.AddMilliseconds(80));
        Assert.Equal(1, log.Tracker.AttackSampleCount);
    }

    [Fact]
    public void Stale_self_ground_does_not_flag_a_newer_peer_view()
    {
        // Bot74 in the post-#1040 soak: every peer saw the subject at the server's live position, but the subject's
        // own self MoveState was 3m49s old. The newer peer view is the truth; the stale self must not be ground.
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-5);
        var subject = new BotObservationLog(74);
        var observer = new BotObservationLog(1);
        subject.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(74, 45157, 41981, isSelf: true)), t0);
        subject.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(1, 45484, 41048, isSelf: false)), t0);
        observer.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(1, 45484, 41048, isSelf: true)), t0);
        observer.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(74, 45157, 41981, isSelf: false)), t0);
        observer.Record(NetOpcode.MoveState, Move(74, 45484, 41048), t0.AddMinutes(4));

        var report = ConsistencyOracle.Analyze(
            [Bot(subject, inWorld: true), Bot(observer, inWorld: true)],
            new LoadScenario());

        Assert.DoesNotContain(report.Issues, i => i.Kind == ConsistencyKind.StalePosition);
    }

    [Fact]
    public void Newer_ground_still_reports_a_stale_peer_view()
    {
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-5);
        var subject = new BotObservationLog(74);
        var observer = new BotObservationLog(1);
        subject.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(74, 45157, 41981, isSelf: true)), t0);
        subject.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(1, 45484, 41048, isSelf: false)), t0);
        observer.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(1, 45484, 41048, isSelf: true)), t0);
        observer.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(74, 45157, 41981, isSelf: false)), t0);
        subject.Record(NetOpcode.MoveState, Move(74, 46000, 41000), t0.AddMinutes(4));

        var report = ConsistencyOracle.Analyze(
            [Bot(subject, inWorld: true), Bot(observer, inWorld: true)],
            new LoadScenario());

        Assert.Contains(report.Issues, i => i.Kind == ConsistencyKind.StalePosition && i.OtherBotId == 74);
    }

    [Fact]
    public void Moving_peer_at_prior_leg_is_not_compared_with_a_later_self_origin()
    {
        // The observer's view is the origin of the leg both sides saw at t+1. The subject's later self origin
        // is on the next leg while the pair is no longer visible, then they meet again at the end. Comparing
        // the old view with the final from-origin invents a stale follower; at a common instant the view was exact.
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-5);
        var subject = new BotObservationLog(74);
        var observer = new BotObservationLog(1);
        subject.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(74, 1000, 1000, isSelf: true)), t0);
        subject.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(1, 1200, 1000, isSelf: false)), t0);
        observer.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(1, 1200, 1000, isSelf: true)), t0);
        observer.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(74, 1000, 1000, isSelf: false)), t0);
        // Same first leg on both lanes: the view and the ground agree at t+1.
        subject.Record(NetOpcode.MoveState, MoveFromTo(74, 1000, 1000, 1400, 1000), t0.AddSeconds(1));
        observer.Record(NetOpcode.MoveState, MoveFromTo(74, 1000, 1000, 1400, 1000), t0.AddSeconds(1));
        // Next leg moves the subject out of the observer's watch; only the subject receives that origin.
        subject.Record(NetOpcode.MoveState, MoveFromTo(74, 7000, 1000, 7400, 1000), t0.AddSeconds(2));
        // They meet again at the end, but the subject has not emitted a move since the view.
        observer.Record(NetOpcode.MoveState, MoveFromTo(1, 6800, 1000, 6900, 1000), t0.AddSeconds(3));

        var report = ConsistencyOracle.Analyze(
            [Bot(subject, inWorld: true), Bot(observer, inWorld: true)],
            new LoadScenario());

        Assert.DoesNotContain(report.Issues, i => i.Kind == ConsistencyKind.StalePosition);
    }

    [Fact]
    public void Frozen_follower_still_fails_while_the_subject_stays_visible()
    {
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-5);
        var subject = new BotObservationLog(74);
        var observer = new BotObservationLog(1);
        subject.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(74, 1000, 1000, isSelf: true)), t0);
        subject.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(1, 1200, 1000, isSelf: false)), t0);
        observer.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(1, 1200, 1000, isSelf: true)), t0);
        observer.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(74, 1000, 1000, isSelf: false)), t0);
        // The subject walks in front of the observer and the observer never receives the move.
        subject.Record(NetOpcode.MoveState, MoveFromTo(74, 2500, 1000, 2900, 1000), t0.AddSeconds(1));

        var report = ConsistencyOracle.Analyze(
            [Bot(subject, inWorld: true), Bot(observer, inWorld: true)],
            new LoadScenario());

        Assert.Contains(report.Issues, i => i.Kind == ConsistencyKind.StalePosition && i.OtherBotId == 74);
    }

    [Fact]
    public void Stationary_same_time_position_mismatch_still_fails()
    {
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-5);
        var subject = new BotObservationLog(74);
        var observer = new BotObservationLog(1);
        subject.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(74, 2200, 1000, isSelf: true)), t0);
        subject.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(1, 1200, 1000, isSelf: false)), t0);
        observer.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(1, 1200, 1000, isSelf: true)), t0);
        // The observer's spawn of the subject carries a position 1200 units away at the same instant.
        observer.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(74, 1000, 1000, isSelf: false)), t0);

        var report = ConsistencyOracle.Analyze(
            [Bot(subject, inWorld: true), Bot(observer, inWorld: true)],
            new LoadScenario());

        Assert.Contains(report.Issues, i => i.Kind == ConsistencyKind.StalePosition && i.OtherBotId == 74);
    }

    [Fact]
    public void Missing_spawn_skips_pairs_whose_self_positions_are_from_different_eras()
    {
        // The subject's last self position is two minutes old; the observer only reaches its area near the end.
        // There is no common instant where both timelines carry a fresh position, so no pair is invented.
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-5);
        var subject = new BotObservationLog(2);
        var observer = new BotObservationLog(1);
        subject.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(1002, 10000, 10000, isSelf: true)), t0);
        observer.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(1001, 1000, 1000, isSelf: true)), t0);
        observer.Record(NetOpcode.MoveState, MoveFromTo(1001, 10000, 10000, 10100, 10000), t0.AddMinutes(2));

        var report = ConsistencyOracle.Analyze(
            [Bot(observer, inWorld: true), Bot(subject, inWorld: true)],
            new LoadScenario());

        Assert.DoesNotContain(report.Issues,
            i => i.Kind == ConsistencyKind.MissingSpawn && i.ObserverBotId == 1 && i.SubjectObjectId == 1002);
    }

    [Fact]
    public void Missing_spawn_still_fails_at_a_comparable_time()
    {
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-5);
        var subject = new BotObservationLog(2);
        var observer = new BotObservationLog(1);
        observer.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(1001, 1000, 1000, isSelf: true)), t0);
        observer.Record(NetOpcode.MoveState, MoveFromTo(1001, 1000, 1000, 1100, 1000), t0.AddSeconds(1));
        subject.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(1002, 1500, 1000, isSelf: true)), t0);

        var report = ConsistencyOracle.Analyze(
            [Bot(observer, inWorld: true), Bot(subject, inWorld: true)],
            new LoadScenario());

        Assert.Contains(report.Issues,
            i => i.Kind == ConsistencyKind.MissingSpawn && i.ObserverBotId == 1 && i.SubjectObjectId == 1002);
    }

    [Fact]
    public void Missing_spawn_ignores_pairs_two_cells_apart_even_when_euclidean_distance_is_under_6000()
    {
        // Observer at the east edge of cell 0, subject just inside cell 2: 3002 units, but GridAoi's 3x3 watch
        // stops at cell 1, so neither can see the other. The old distance<=6000 rule invented this pair.
        var observer = new BotObservationLog(1);
        var subject = new BotObservationLog(2);
        var at = DateTimeOffset.UtcNow;
        observer.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(1001, 2999, 0, isSelf: true)), at);
        subject.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(1002, 6001, 0, isSelf: true)), at);

        var report = ConsistencyOracle.Analyze(
            [Bot(observer, inWorld: true), Bot(subject, inWorld: true)],
            new LoadScenario());

        Assert.DoesNotContain(report.Issues, i => i.Kind == ConsistencyKind.MissingSpawn);
    }

    [Fact]
    public void Missing_spawn_expects_diagonal_neighbour_cells_beyond_6000_units()
    {
        // (5999,5999) is the far corner of the adjacent cell: 8484 units away, but GridAoi's 3x3 watch includes it.
        var observer = new BotObservationLog(1);
        var subject = new BotObservationLog(2);
        var at = DateTimeOffset.UtcNow;
        observer.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(1001, 0, 0, isSelf: true)), at);
        subject.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(1002, 5999, 5999, isSelf: true)), at);

        var report = ConsistencyOracle.Analyze(
            [Bot(observer, inWorld: true), Bot(subject, inWorld: true)],
            new LoadScenario());

        Assert.Contains(report.Issues,
            i => i.Kind == ConsistencyKind.MissingSpawn && i.ObserverBotId == 1 && i.SubjectObjectId == 1002);
        Assert.Contains(report.Issues,
            i => i.Kind == ConsistencyKind.MissingSpawn && i.ObserverBotId == 2 && i.SubjectObjectId == 1001);
    }

    [Fact]
    public void Alternating_long_leg_destinations_do_not_invent_a_missing_spawn_at_the_unreached_tip()
    {
        var observer = new BotObservationLog(1);
        var subject = new BotObservationLog(2);
        var at = DateTimeOffset.UtcNow;
        observer.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(1001, 6000, 0, isSelf: true)), at);
        subject.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(1002, 0, 0, isSelf: true)), at);
        // Server origin stays in cell 0 while the requested leg tip is in cell 2. Dest is not the pawn.
        for (int i = 1; i <= 5; i++)
            subject.Record(NetOpcode.MoveState, MoveFromTo(1002, 0, 0, 6000, 0), at.AddMilliseconds(i * 200));

        var report = ConsistencyOracle.Analyze(
            [Bot(observer, inWorld: true), Bot(subject, inWorld: true)],
            new LoadScenario());

        Assert.DoesNotContain(report.Issues, i => i.Kind == ConsistencyKind.MissingSpawn);
    }

    [Fact]
    public void Stationary_players_in_the_same_watch_still_report_missing_spawn()
    {
        var observer = new BotObservationLog(1);
        var subject = new BotObservationLog(2);
        var at = DateTimeOffset.UtcNow;
        observer.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(1001, 0, 0, isSelf: true)), at);
        subject.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(1002, 500, 500, isSelf: true)), at);

        var report = ConsistencyOracle.Analyze(
            [Bot(observer, inWorld: true), Bot(subject, inWorld: true)],
            new LoadScenario());

        Assert.Contains(report.Issues,
            i => i.Kind == ConsistencyKind.MissingSpawn && i.ObserverBotId == 1 && i.SubjectObjectId == 1002);
        Assert.Contains(report.Issues,
            i => i.Kind == ConsistencyKind.MissingSpawn && i.ObserverBotId == 2 && i.SubjectObjectId == 1001);
    }

    [Fact]
    public void Position_correction_is_authoritative_for_missing_spawn_pairs()
    {
        var observer = new BotObservationLog(1);
        var subject = new BotObservationLog(2);
        var at = DateTimeOffset.UtcNow;
        observer.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(1001, 0, 0, isSelf: true)), at);
        subject.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(1002, 6000, 0, isSelf: true)), at);
        subject.Record(NetOpcode.PositionCorrection, NetCodec.EncodePositionCorrection(new PositionCorrectionMsg
        {
            EntityValue = 1002,
            EntityGeneration = 1,
            X = 500,
            Y = 400,
            Z = 0
        }), at.AddSeconds(1));

        var report = ConsistencyOracle.Analyze(
            [Bot(observer, inWorld: true), Bot(subject, inWorld: true)],
            new LoadScenario());

        Assert.Contains(report.Issues,
            i => i.Kind == ConsistencyKind.MissingSpawn && i.ObserverBotId == 1 && i.SubjectObjectId == 1002);
    }

    [Fact]
    public void Dead_observer_is_excluded_but_live_observer_still_checks_corpse_visibility()
    {
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-2);
        var dead = new BotObservationLog(1);
        var live = new BotObservationLog(2);
        dead.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(1001, 1000, 1000, true)), t0);
        dead.Record(NetOpcode.EntityDie, NetCodec.EncodeEntityDie(new EntityDieMsg
        {
            EntityValue = 1001,
            EntityGeneration = 1,
            KillerObjectId = 0
        }), t0.AddSeconds(1));
        live.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(1002, 1500, 1500, true)), t0);

        var report = ConsistencyOracle.Analyze(
            [Bot(dead, inWorld: true), Bot(live, inWorld: true)],
            new LoadScenario());

        Assert.Contains(report.Issues,
            i => i.Kind == ConsistencyKind.MissingSpawn && i.ObserverBotId == 2 && i.SubjectObjectId == 1001);
        Assert.DoesNotContain(report.Issues,
            i => i.Kind == ConsistencyKind.MissingSpawn && i.ObserverBotId == 1);
    }

    [Fact]
    public void Death_closes_the_spawn_run_so_a_respawn_is_not_a_duplicate()
    {
        var log = new BotObservationLog(1);
        var at = DateTimeOffset.UtcNow;
        var spawn = NetCodec.EncodeEntitySpawn(Npc(42, 0, 0));
        log.Record(NetOpcode.EntitySpawn, spawn, at);
        log.Record(NetOpcode.EntityDie, NetCodec.EncodeEntityDie(new EntityDieMsg
        {
            EntityValue = 42,
            EntityGeneration = 1,
            KillerObjectId = 0
        }), at.AddSeconds(1));
        log.Record(NetOpcode.EntitySpawn, spawn, at.AddSeconds(2));

        Assert.Equal(1, log.Tracker.Entities[42].SpawnsWithoutDespawn);
        var report = ConsistencyOracle.Analyze([Bot(log)], new LoadScenario());
        Assert.DoesNotContain(report.Issues, i => i.Kind == ConsistencyKind.DuplicateSpawn);
    }

    [Fact]
    public void Four_spawns_after_a_death_are_still_a_duplicate()
    {
        var log = new BotObservationLog(1);
        var at = DateTimeOffset.UtcNow;
        var spawn = NetCodec.EncodeEntitySpawn(Npc(42, 0, 0));
        log.Record(NetOpcode.EntitySpawn, spawn, at);
        log.Record(NetOpcode.EntityDie, NetCodec.EncodeEntityDie(new EntityDieMsg
        {
            EntityValue = 42,
            EntityGeneration = 1,
            KillerObjectId = 0
        }), at.AddSeconds(1));
        for (int i = 0; i < 4; i++)
            log.Record(NetOpcode.EntitySpawn, spawn, at.AddSeconds(2 + i));

        Assert.Equal(4, log.Tracker.Entities[42].SpawnsWithoutDespawn);
        var report = ConsistencyOracle.Analyze([Bot(log)], new LoadScenario());
        Assert.Contains(report.Issues, i => i.Kind == ConsistencyKind.DuplicateSpawn && i.SubjectObjectId == 42);
    }

    [Fact]
    public void Death_after_observation_suppresses_historical_stale_position()
    {
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-5);
        var subject = new BotObservationLog(74);
        var observer = new BotObservationLog(1);
        subject.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(74, 0, 0, true)), t0);
        subject.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(1, 0, 0, false)), t0);
        observer.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(1, 0, 0, true)), t0);
        observer.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(74, 1000, 1000, false)), t0.AddSeconds(1));
        subject.Record(NetOpcode.EntityDie, NetCodec.EncodeEntityDie(new EntityDieMsg
        {
            EntityValue = 74,
            EntityGeneration = 1,
            KillerObjectId = 0
        }), t0.AddSeconds(3));
        subject.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(74, 5000, 5000, true)), t0.AddSeconds(4));

        var report = ConsistencyOracle.Analyze(
            [Bot(subject, inWorld: true), Bot(observer, inWorld: true)],
            new LoadScenario());

        Assert.DoesNotContain(report.Issues, i => i.Kind == ConsistencyKind.StalePosition);
    }

    [Fact]
    public void Repeated_charinfo_refresh_for_a_known_player_is_not_a_duplicate_spawn()
    {
        var t0 = DateTimeOffset.UtcNow;
        var subject = new BotObservationLog(2);
        subject.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(42, 0, 0, isSelf: true)), t0);
        var observer = new BotObservationLog(1);
        observer.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(42, 0, 0, isSelf: false)), t0);

        for (int i = 1; i <= 6; i++)
        {
            var at = t0.AddSeconds(i);
            observer.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(42, 0, 0, isSelf: false)), at);
            // Mobius broadcastUserInfo: the owner's UserInfo and the peers' CharInfo come from the same step.
            subject.Record(NetOpcode.UserInfo, UserInfo(42), at);
        }

        // The protocol-blind open-run counter still sees every spawn; the oracle classifies the paired replies.
        Assert.Equal(7, observer.Tracker.Entities[42].SpawnsWithoutDespawn);

        var report = ConsistencyOracle.Analyze([Bot(observer), Bot(subject)], new LoadScenario());

        Assert.DoesNotContain(report.Issues, i => i.Kind == ConsistencyKind.DuplicateSpawn);
    }

    [Fact]
    public void Repeated_lifecycle_player_spawn_without_owner_userinfo_is_still_a_duplicate()
    {
        var log = new BotObservationLog(1);
        var at = DateTimeOffset.UtcNow;
        var spawn = NetCodec.EncodeEntitySpawn(Player(42, 0, 0, isSelf: false));
        for (int i = 0; i < 4; i++)
            log.Record(NetOpcode.EntitySpawn, spawn, at.AddSeconds(i));

        var report = ConsistencyOracle.Analyze([Bot(log)], new LoadScenario());

        Assert.Contains(report.Issues, i => i.Kind == ConsistencyKind.DuplicateSpawn && i.SubjectObjectId == 42);
    }

    [Fact]
    public void Enter_duplicate_is_not_explained_by_the_owner_enter_userinfo()
    {
        var t0 = DateTimeOffset.UtcNow;
        var subject = new BotObservationLog(2);
        subject.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(42, 0, 0, isSelf: true)), t0);
        subject.Record(NetOpcode.UserInfo, UserInfo(42), t0);
        var observer = new BotObservationLog(1);
        var spawn = NetCodec.EncodeEntitySpawn(Player(42, 0, 0, isSelf: false));
        for (int i = 0; i < 4; i++)
            observer.Record(NetOpcode.EntitySpawn, spawn, t0.AddMilliseconds(i * 20));

        var report = ConsistencyOracle.Analyze([Bot(observer), Bot(subject)], new LoadScenario());

        Assert.Contains(report.Issues, i => i.Kind == ConsistencyKind.DuplicateSpawn && i.SubjectObjectId == 42);
    }

    [Fact]
    public void Unrelated_owner_userinfo_does_not_refresh_another_entity_spawn()
    {
        var log = new BotObservationLog(1);
        var other = new BotObservationLog(2);
        var at = DateTimeOffset.UtcNow;
        other.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(7, 0, 0, isSelf: true)), at);
        var spawn = NetCodec.EncodeEntitySpawn(Player(42, 0, 0, isSelf: false));
        for (int i = 0; i < 4; i++)
        {
            log.Record(NetOpcode.EntitySpawn, spawn, at.AddSeconds(i));
            other.Record(NetOpcode.UserInfo, UserInfo(7), at.AddSeconds(i));
        }

        var report = ConsistencyOracle.Analyze([Bot(log), Bot(other)], new LoadScenario());

        Assert.Contains(report.Issues, i => i.Kind == ConsistencyKind.DuplicateSpawn && i.SubjectObjectId == 42);
    }

    [Fact]
    public void Owner_userinfo_does_not_classify_npc_spawns_as_charinfo_refreshes()
    {
        var log = new BotObservationLog(1);
        var owner = new BotObservationLog(2);
        var at = DateTimeOffset.UtcNow;
        owner.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(42, 0, 0, isSelf: true)), at);
        var spawn = NetCodec.EncodeEntitySpawn(Npc(42, 0, 0));
        for (int i = 0; i < 4; i++)
        {
            log.Record(NetOpcode.EntitySpawn, spawn, at.AddSeconds(i));
            owner.Record(NetOpcode.UserInfo, UserInfo(42), at.AddSeconds(i));
        }

        var report = ConsistencyOracle.Analyze([Bot(log), Bot(owner)], new LoadScenario());

        Assert.Contains(report.Issues, i => i.Kind == ConsistencyKind.DuplicateSpawn && i.SubjectObjectId == 42);
    }

    [Fact]
    public void Combat_fanout_uses_exact_grid_watch_instead_of_euclidean_radius()
    {
        // 3002 units apart (<= the old 6000 Euclidean radius) but in cells 0 and 2: GridAoi's 3x3 watch does not
        // include the watcher, so its missing AttackState is not a fan-out failure.
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-5);
        var attacker = new BotObservationLog(1);
        var witness = new BotObservationLog(0);
        var watcher = new BotObservationLog(2);
        attacker.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(500, 2999, 0, true)), t0);
        attacker.Record(NetOpcode.AttackState, Attack(500), t0);
        witness.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(1000, 0, 0, true)), t0);
        witness.Record(NetOpcode.AttackState, Attack(500), t0);
        watcher.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(2000, 6001, 0, true)), t0);

        var report = ConsistencyOracle.Analyze(
            [Bot(attacker, inWorld: true), Bot(witness, inWorld: true), Bot(watcher, inWorld: true)],
            new LoadScenario());

        Assert.DoesNotContain(report.Issues,
            i => i.Kind == ConsistencyKind.MissingCombatFanout && i.ObserverBotId == 2);
    }

    [Fact]
    public void Combat_fanout_reports_a_visible_stationary_watcher()
    {
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-5);
        var attacker = new BotObservationLog(1);
        var witness = new BotObservationLog(0);
        var watcher = new BotObservationLog(2);
        attacker.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(500, 0, 0, true)), t0);
        attacker.Record(NetOpcode.AttackState, Attack(500), t0);
        witness.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(1000, 0, 0, true)), t0);
        witness.Record(NetOpcode.AttackState, Attack(500), t0);
        watcher.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(2000, 500, 0, true)), t0);

        var report = ConsistencyOracle.Analyze(
            [Bot(attacker, inWorld: true), Bot(witness, inWorld: true), Bot(watcher, inWorld: true)],
            new LoadScenario());

        Assert.Contains(report.Issues,
            i => i.Kind == ConsistencyKind.MissingCombatFanout && i.ObserverBotId == 2);
    }

    [Fact]
    public void Combat_fanout_uses_liveness_at_attack_not_end_state()
    {
        // Bot 2 was alive at the attack and died later: its earlier missing fan-out still counts. Bot 3 was dead
        // at the attack and alive again at the end: the end-state pawn must not make it a live observer.
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-5);
        var attacker = new BotObservationLog(1);
        var witness = new BotObservationLog(0);
        var diedLater = new BotObservationLog(2);
        var deadAtAttack = new BotObservationLog(3);
        attacker.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(500, 0, 0, true)), t0);
        attacker.Record(NetOpcode.AttackState, Attack(500), t0);
        witness.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(1000, 0, 0, true)), t0);
        witness.Record(NetOpcode.AttackState, Attack(500), t0);

        diedLater.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(2000, 500, 0, true)), t0);
        diedLater.Record(NetOpcode.EntityDie, NetCodec.EncodeEntityDie(new EntityDieMsg
        {
            EntityValue = 2000,
            EntityGeneration = 1,
            KillerObjectId = 0
        }), t0.AddSeconds(1));

        deadAtAttack.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(3000, 600, 0, true)), t0.AddSeconds(-2));
        deadAtAttack.Record(NetOpcode.EntityDie, NetCodec.EncodeEntityDie(new EntityDieMsg
        {
            EntityValue = 3000,
            EntityGeneration = 1,
            KillerObjectId = 0
        }), t0.AddSeconds(-1));
        deadAtAttack.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(3000, 600, 0, true)), t0.AddSeconds(1));

        var report = ConsistencyOracle.Analyze(
            [
                Bot(attacker, inWorld: true),
                Bot(witness, inWorld: true),
                Bot(diedLater, inWorld: true),
                Bot(deadAtAttack, inWorld: true)
            ],
            new LoadScenario());

        Assert.Contains(report.Issues,
            i => i.Kind == ConsistencyKind.MissingCombatFanout && i.ObserverBotId == 2);
        Assert.DoesNotContain(report.Issues,
            i => i.Kind == ConsistencyKind.MissingCombatFanout && i.ObserverBotId == 3);
    }

    [Fact]
    public void Move_origin_view_does_not_compare_the_next_turn_as_stale()
    {
        // The observer received the MoveState origin (1000,1000) and is rendering that turn. The subject's later
        // self origin is the next turn along the same leg; it is not evidence that the received view froze.
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-5);
        var subject = new BotObservationLog(74);
        var observer = new BotObservationLog(1);
        subject.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(74, 1000, 1000, true)), t0);
        subject.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(1, 1200, 1000, false)), t0);
        observer.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(1, 1200, 1000, true)), t0);
        observer.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(74, 1000, 1000, false)), t0);
        subject.Record(NetOpcode.MoveState, MoveFromTo(74, 1000, 1000, 7000, 1000), t0.AddSeconds(1));
        observer.Record(NetOpcode.MoveState, MoveFromTo(74, 1000, 1000, 7000, 1000), t0.AddSeconds(1));
        subject.Record(NetOpcode.MoveState, MoveFromTo(74, 4000, 1000, 7000, 1000), t0.AddSeconds(2));

        var report = ConsistencyOracle.Analyze(
            [Bot(subject, inWorld: true), Bot(observer, inWorld: true)],
            new LoadScenario());

        Assert.DoesNotContain(report.Issues, i => i.Kind == ConsistencyKind.StalePosition);
    }

    [Fact]
    public void Move_origin_view_with_a_same_time_mismatch_still_fails()
    {
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-5);
        var subject = new BotObservationLog(74);
        var observer = new BotObservationLog(1);
        subject.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(74, 1000, 1000, true)), t0);
        subject.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(1, 1200, 1000, false)), t0);
        observer.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(1, 1200, 1000, true)), t0);
        observer.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(74, 1000, 1000, false)), t0);
        // The observer's MoveState origin is 1200 units from the subject's own position at that instant.
        observer.Record(NetOpcode.MoveState, MoveFromTo(74, 2200, 1000, 2300, 1000), t0.AddSeconds(1));

        var report = ConsistencyOracle.Analyze(
            [Bot(subject, inWorld: true), Bot(observer, inWorld: true)],
            new LoadScenario());

        Assert.Contains(report.Issues,
            i => i.Kind == ConsistencyKind.StalePosition && i.OtherBotId == 74);
    }

    [Fact]
    public void Despawned_peer_view_is_not_reported_as_stale()
    {
        // The observer's view is closed by the despawn; a later origin belongs to a new visibility interval.
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-5);
        var subject = new BotObservationLog(74);
        var observer = new BotObservationLog(1);
        subject.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(74, 1000, 1000, true)), t0);
        subject.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(1, 1200, 1000, false)), t0);
        observer.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(1, 1200, 1000, true)), t0);
        observer.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(74, 1000, 1000, false)), t0);
        observer.Record(NetOpcode.EntityDespawn, NetCodec.EncodeEntityDespawn(new EntityDespawnMsg
        {
            EntityValue = 74,
            EntityGeneration = 1
        }), t0.AddSeconds(1));
        subject.Record(NetOpcode.MoveState, MoveFromTo(74, 4000, 1000, 7000, 1000), t0.AddSeconds(2));

        var report = ConsistencyOracle.Analyze(
            [Bot(subject, inWorld: true), Bot(observer, inWorld: true)],
            new LoadScenario());

        Assert.DoesNotContain(report.Issues,
            i => i.Kind == ConsistencyKind.StalePosition && i.OtherBotId == 74);
    }

    [Fact]
    public void Missing_spawn_does_not_pair_an_observer_at_run_end_with_a_subject_that_stopped_long_ago()
    {
        // Long-soak sample: subject 544's self timeline ends early while the observer keeps moving. Current
        // membership (observer does not know the subject) must not be combined with a co-visible moment long ago.
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-30);
        var subject = new BotObservationLog(179);
        var observer = new BotObservationLog(29);
        subject.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(544, 46453, 45307, isSelf: true)), t0);
        subject.Record(NetOpcode.MoveState, Move(544, 46453, 45307), t0.AddSeconds(55));
        observer.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(394, 47000, 45307, isSelf: true)), t0);
        observer.Record(NetOpcode.MoveState, Move(394, 46453, 45307), t0.AddMinutes(29));

        var report = ConsistencyOracle.Analyze(
            [Bot(observer, inWorld: true), Bot(subject, inWorld: true)],
            new LoadScenario());

        Assert.DoesNotContain(report.Issues,
            i => i.Kind == ConsistencyKind.MissingSpawn && i.ObserverBotId == 29 && i.SubjectObjectId == 544);
    }

    [Fact]
    public void Missing_spawn_ignores_a_subject_that_left_the_world_before_the_pair_anchor()
    {
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-30);
        var subject = new BotObservationLog(179);
        var observer = new BotObservationLog(29);
        subject.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(544, 46453, 45307, isSelf: true)), t0);
        subject.Record(NetOpcode.EntityDespawn, NetCodec.EncodeEntityDespawn(new EntityDespawnMsg
        {
            EntityValue = 544,
            EntityGeneration = 1
        }), t0.AddSeconds(55));
        observer.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(394, 47000, 45307, isSelf: true)), t0);
        observer.Record(NetOpcode.MoveState, Move(394, 46453, 45307), t0.AddMinutes(29));

        var report = ConsistencyOracle.Analyze(
            [Bot(observer, inWorld: true), Bot(subject, inWorld: true)],
            new LoadScenario());

        Assert.DoesNotContain(report.Issues,
            i => i.Kind == ConsistencyKind.MissingSpawn && i.ObserverBotId == 29 && i.SubjectObjectId == 544);
    }

    [Fact]
    public void Missing_spawn_still_flags_two_live_bots_with_fresh_self_samples()
    {
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-5);
        var observer = new BotObservationLog(1);
        var subject = new BotObservationLog(2);
        observer.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(1001, 1000, 1000, isSelf: true)), t0);
        observer.Record(NetOpcode.MoveState, Move(1001, 1050, 1050), t0.AddSeconds(2));
        subject.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(1002, 1500, 1000, isSelf: true)), t0);
        subject.Record(NetOpcode.MoveState, Move(1002, 1550, 1050), t0.AddSeconds(2));

        var report = ConsistencyOracle.Analyze(
            [Bot(observer, inWorld: true), Bot(subject, inWorld: true)],
            new LoadScenario());

        Assert.Contains(report.Issues,
            i => i.Kind == ConsistencyKind.MissingSpawn && i.ObserverBotId == 1 && i.SubjectObjectId == 1002);
        Assert.NotNull(report.Issues.First(
            i => i.Kind == ConsistencyKind.MissingSpawn && i.ObserverBotId == 1).ObservedAt);
    }

    [Fact]
    public void Hp_desync_does_not_treat_a_self_only_level_up_as_a_missed_fanout()
    {
        // Kill/level status carries Level/Exp/Sp and is sent to the owner only. The observer's last bar (56)
        // matches the last peer-visible state even though the subject's end self HP is 111.
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-30);
        var subject = new BotObservationLog(134);
        var observer = new BotObservationLog(9);
        subject.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(499, 46002, 42065, isSelf: true)), t0);
        subject.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(374, 46010, 42060, isSelf: false)), t0);
        observer.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(374, 46010, 42060, isSelf: true)), t0);
        observer.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(499, 46002, 42065, isSelf: false)), t0);
        subject.Record(NetOpcode.StatusUpdate, NetCodec.EncodeStatusUpdate(new StatusUpdateNetMsg
        {
            EntityValue = 499,
            EntityGeneration = 1,
            AttributeIds = [Fineage.Protocol.InterludeStatus.CurHp],
            Values = [56]
        }), t0.AddSeconds(1));
        observer.Record(NetOpcode.StatusUpdate, NetCodec.EncodeStatusUpdate(new StatusUpdateNetMsg
        {
            EntityValue = 499,
            EntityGeneration = 1,
            AttributeIds = [Fineage.Protocol.InterludeStatus.CurHp],
            Values = [56]
        }), t0.AddSeconds(29));
        subject.Record(NetOpcode.StatusUpdate, NetCodec.EncodeStatusUpdate(new StatusUpdateNetMsg
        {
            EntityValue = 499,
            EntityGeneration = 1,
            AttributeIds =
            [
                Fineage.Protocol.InterludeStatus.Level,
                Fineage.Protocol.InterludeStatus.Exp,
                Fineage.Protocol.InterludeStatus.Sp,
                Fineage.Protocol.InterludeStatus.CurHp,
                Fineage.Protocol.InterludeStatus.MaxHp
            ],
            Values = [2, 1200, 300, 111, 111]
        }), t0.AddSeconds(30));

        var report = ConsistencyOracle.Analyze(
            [Bot(observer, inWorld: true), Bot(subject, inWorld: true)],
            new LoadScenario());

        Assert.Equal(111, subject.Tracker.Entities[499].CurHp);
        Assert.Equal(56, subject.Tracker.Entities[499].ExpectedPeerHp);
        Assert.DoesNotContain(report.Issues, i => i.Kind == ConsistencyKind.HpDesync);
    }

    [Fact]
    public void Hp_desync_flags_a_missed_peer_visible_hp_loss()
    {
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-5);
        var subject = new BotObservationLog(134);
        var observer = new BotObservationLog(9);
        subject.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(499, 46002, 42065, isSelf: true)), t0);
        subject.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(374, 46010, 42060, isSelf: false)), t0);
        observer.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(374, 46010, 42060, isSelf: true)), t0);
        observer.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(499, 46002, 42065, isSelf: false)), t0);
        subject.Record(NetOpcode.StatusUpdate, NetCodec.EncodeStatusUpdate(new StatusUpdateNetMsg
        {
            EntityValue = 499,
            EntityGeneration = 1,
            AttributeIds = [Fineage.Protocol.InterludeStatus.CurHp],
            Values = [40]
        }), t0.AddSeconds(5));

        var report = ConsistencyOracle.Analyze(
            [Bot(observer, inWorld: true), Bot(subject, inWorld: true)],
            new LoadScenario());

        var issue = Assert.Single(report.Issues,
            i => i.Kind == ConsistencyKind.HpDesync && i.ObserverBotId == 9 && i.SubjectObjectId == 499);
        Assert.Contains("expected 40", issue.Detail);
    }

    [Fact]
    public void Hp_desync_respects_the_bounded_delivery_window()
    {
        var t0 = DateTimeOffset.UtcNow.AddSeconds(-2);
        var subject = new BotObservationLog(134);
        var observer = new BotObservationLog(9);
        subject.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(499, 46002, 42065, isSelf: true)), t0);
        subject.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(374, 46010, 42060, isSelf: false)), t0);
        observer.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(374, 46010, 42060, isSelf: true)), t0);
        observer.Record(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(Player(499, 46002, 42065, isSelf: false)), t0);
        subject.Record(NetOpcode.StatusUpdate, NetCodec.EncodeStatusUpdate(new StatusUpdateNetMsg
        {
            EntityValue = 499,
            EntityGeneration = 1,
            AttributeIds = [Fineage.Protocol.InterludeStatus.CurHp],
            Values = [40]
        }), t0.AddSeconds(2));

        var report = ConsistencyOracle.Analyze(
            [Bot(observer, inWorld: true), Bot(subject, inWorld: true)],
            new LoadScenario());

        Assert.DoesNotContain(report.Issues, i => i.Kind == ConsistencyKind.HpDesync);
    }

    static FineageBotClient Bot(BotObservationLog log) => Bot(log, inWorld: false);

    static FineageBotClient Bot(BotObservationLog log, bool inWorld)
    {
        var bot = new FineageBotClient(
            new LoadScenario { GatekeeperTravelShare = 0 },
            log.BotId,
            new BotStats { BotId = log.BotId, InWorld = inWorld, LastActionAt = inWorld ? DateTimeOffset.UtcNow : null },
            new BotPeerDirectory());
        foreach (var frame in log.Snapshot())
            bot.ObservationLog.Record(frame.Opcode, frame.Payload, frame.At);
        if (log.Tracker.SelfEntityValue != 0)
            bot.SetSelfEntityForTest(log.Tracker.SelfEntityValue);
        return bot;
    }

    static byte[] UserInfo(ulong id) => NetCodec.EncodeUserInfo(new UserInfoNetMsg
    {
        EntityValue = id,
        EntityGeneration = 1,
        Name = $"Bot{id}",
        MaxHp = 100,
        CurHp = 100
    });

    static EntitySpawnMsg Player(ulong id, int x, int y, bool isSelf) => new()
    {
        EntityValue = id,
        EntityGeneration = 1,
        Kind = EntityKindNet.Player,
        IsSelf = isSelf,
        X = x,
        Y = y,
        Z = 0,
        MaxHp = 100,
        CurHp = 100
    };

    static byte[] Attack(ulong attacker) => NetCodec.EncodeAttackState(new AttackStateMsg
    {
        AttackerValue = attacker,
        AttackerGeneration = 1,
        TargetValue = 9000,
        TargetGeneration = 1,
        Damage = 10,
        AttackerX = 0,
        AttackerY = 0,
        AttackerZ = 0
    });

    static byte[] Move(ulong id, int x, int y) => MoveFromTo(id, x, y, x, y);

    static byte[] MoveFromTo(ulong id, int originX, int originY, int destX, int destY) =>
        NetCodec.EncodeMoveState(new MoveStateMsg
        {
            EntityValue = id,
            EntityGeneration = 1,
            OriginX = originX,
            OriginY = originY,
            OriginZ = 0,
            DestX = destX,
            DestY = destY,
            DestZ = 0,
            Running = true
        });

    static EntitySpawnMsg Npc(ulong id, int x, int y) => new()
    {
        EntityValue = id,
        EntityGeneration = 1,
        Kind = EntityKindNet.Npc,
        Attackable = true,
        X = x,
        Y = y,
        MaxHp = 40,
        CurHp = 40
    };
}