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