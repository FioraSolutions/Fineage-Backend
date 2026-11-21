using Fineage.LoadTest.Bot;
using Fineage.LoadTest.Consistency;
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
        Assert.Equal(0, ent.LastDespawnOrdinal);
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
}