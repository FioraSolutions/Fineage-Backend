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

    static FineageBotClient Bot(BotObservationLog log)
    {
        var bot = new FineageBotClient(new LoadScenario(), log.BotId, new BotStats { BotId = log.BotId }, new BotPeerDirectory());
        foreach (var frame in log.Snapshot())
            bot.ObservationLog.Record(frame.Opcode, frame.Payload, frame.At);
        return bot;
    }

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