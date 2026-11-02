using Fineage.Net.Contract;
using Fineage.Zone.Net;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class NetPresenceHubTests
{
    [Fact]
    public void Remote_spawn_and_despawn_codec_round_trip()
    {
        var entity = new ZoneEntity
        {
            ObjectId = 7,
            Kind = ZoneEntityKind.Player,
            Name = "Peer",
            ClassId = 0,
            Race = 0,
            Sex = 0,
            X = 10,
            Y = 20,
            Z = 30,
            RunSpeed = 120,
            WalkSpeed = 80,
            Running = true
        };
        var spawn = NetEnterWorldProjector.BuildRemoteSpawn(entity, EntityId.FromObjectId(7));
        Assert.False(spawn.IsSelf);
        Assert.Equal("Peer", spawn.Name);
        Assert.Equal(10, spawn.X);
        Assert.Equal(EntityKindNet.Player, spawn.Kind);

        var again = NetCodec.DecodeEntitySpawn(NetCodec.EncodeEntitySpawn(spawn));
        Assert.False(again.IsSelf);
        Assert.Equal(spawn.EntityValue, again.EntityValue);

        var despawn = new EntityDespawnMsg
        {
            EntityValue = spawn.EntityValue,
            EntityGeneration = spawn.EntityGeneration
        };
        var round = NetCodec.DecodeEntityDespawn(NetCodec.EncodeEntityDespawn(despawn));
        Assert.Equal(despawn.EntityValue, round.EntityValue);
        Assert.Equal(despawn.EntityGeneration, round.EntityGeneration);
    }

    [Fact]
    public void Hub_register_lookup_unregister()
    {
        var hub = new NetPresenceHub();
        Assert.False(hub.TryGet(1, out _, out _));
        // Without a live socket we only assert Unregister is safe.
        hub.Unregister(1);
        Assert.False(hub.TryGet(1, out _, out _));
    }

    [Fact]
    public void Die_and_drop_codec_round_trip()
    {
        var die = new EntityDieMsg { EntityValue = 9001, EntityGeneration = 1, KillerObjectId = 7 };
        var dieAgain = NetCodec.DecodeEntityDie(NetCodec.EncodeEntityDie(die));
        Assert.Equal(9001ul, dieAgain.EntityValue);
        Assert.Equal(7, dieAgain.KillerObjectId);

        var drop = new DropItemNetMsg
        {
            DropperObjectId = 7,
            EntityValue = 55,
            EntityGeneration = 1,
            ItemId = 57,
            X = 1,
            Y = 2,
            Z = 3,
            Stackable = true,
            Count = 40
        };
        var dropAgain = NetCodec.DecodeDropItem(NetCodec.EncodeDropItem(drop));
        Assert.Equal(57, dropAgain.ItemId);
        Assert.Equal(40, dropAgain.Count);
        Assert.True(dropAgain.Stackable);
    }

    [Fact]
    public void Attack_and_status_codec_round_trip()
    {
        var attack = new AttackStateMsg
        {
            AttackerValue = 1,
            AttackerGeneration = 1,
            TargetValue = 9001,
            TargetGeneration = 1,
            Damage = 12,
            Flags = 0,
            AttackerX = 1,
            AttackerY = 2,
            AttackerZ = 3,
            TargetX = 4,
            TargetY = 5,
            TargetZ = 6
        };
        var again = NetCodec.DecodeAttackState(NetCodec.EncodeAttackState(attack));
        Assert.Equal(12, again.Damage);
        Assert.Equal(9001ul, again.TargetValue);

        var status = new StatusUpdateNetMsg
        {
            EntityValue = 9001,
            EntityGeneration = 1,
            AttributeIds = [9, 10],
            Values = [80, 100]
        };
        var statusAgain = NetCodec.DecodeStatusUpdate(NetCodec.EncodeStatusUpdate(status));
        Assert.Equal(2, statusAgain.AttributeIds.Length);
        Assert.Equal(80, statusAgain.Values[0]);
        Assert.Equal(100, statusAgain.Values[1]);
    }

    [Fact]
    public void Npc_presence_spawn_uses_npc_kind_and_template()
    {
        var entity = new ZoneEntity
        {
            ObjectId = 9001,
            Kind = ZoneEntityKind.Npc,
            NpcId = 4416,
            Name = "Gremlin",
            X = 100,
            Y = 200,
            Z = -3000,
            RunSpeed = 110,
            WalkSpeed = 50,
            Running = true,
            Attackable = true
        };
        var spawn = NetEnterWorldProjector.BuildNpcSpawn(entity, EntityId.FromObjectId(9001));
        Assert.Equal(EntityKindNet.Npc, spawn.Kind);
        Assert.False(spawn.IsSelf);
        Assert.Equal(4416, spawn.TemplateId);
        Assert.Equal("Gremlin", spawn.Name);

        var viaPresence = NetEnterWorldProjector.BuildPresenceSpawn(entity, EntityId.FromObjectId(9001));
        Assert.Equal(EntityKindNet.Npc, viaPresence.Kind);
        Assert.Equal(4416, viaPresence.TemplateId);

        var round = NetCodec.DecodeEntitySpawn(NetCodec.EncodeEntitySpawn(spawn));
        Assert.Equal(EntityKindNet.Npc, round.Kind);
        Assert.Equal(4416, round.TemplateId);
    }
}
