using Fineage.Net.Contract;
using Xunit;

namespace Fineage.Net.Tests;

/// <summary>Summon / pet wire: commands, the summon window, the gone notice, the pet bag and the EntitySpawn owner tail.</summary>
public sealed class SummonCodecRoundTripTests
{
    [Fact]
    public void Summon_command_roundtrip_and_old_payload_without_item_tail()
    {
        var msg = new SummonCommandNetMsg
        {
            Kind = SummonCommandKind.UseSkill,
            TargetObjectId = 77,
            SkillId = 4068,
            X = 1,
            Y = -2,
            Z = 3,
            Forced = true,
            ItemObjectId = 900,
            Count = 5
        };
        var back = NetCodec.DecodeSummonCommand(NetCodec.EncodeSummonCommand(msg));
        Assert.Equal(SummonCommandKind.UseSkill, back.Kind);
        Assert.Equal((77, 4068, 1, -2, 3, true, 900, 5),
            (back.TargetObjectId, back.SkillId, back.X, back.Y, back.Z, back.Forced, back.ItemObjectId, back.Count));

        byte[] full = NetCodec.EncodeSummonCommand(msg);
        var shortForm = NetCodec.DecodeSummonCommand(full.AsSpan(0, full.Length - 8));
        Assert.Equal((0, 0), (shortForm.ItemObjectId, shortForm.Count));
        Assert.Equal(77, shortForm.TargetObjectId);
    }

    [Fact]
    public void Summon_info_gone_and_pet_inventory_roundtrip()
    {
        var info = NetCodec.DecodeSummonInfo(NetCodec.EncodeSummonInfo(new SummonInfoNetMsg
        {
            ObjectId = 5,
            Kind = SummonKindNet.Servitor,
            NpcId = 14111,
            Name = "Kat the Cat",
            Level = 22,
            Hp = 300,
            MaxHp = 387,
            Mp = 10,
            MaxMp = 20,
            Exp = 1L << 40,
            ExpLevelStart = 3,
            ExpLevelEnd = 4,
            Feed = 600,
            MaxFeed = 1200,
            PAtk = 51,
            PDef = 92,
            MAtk = 35,
            MDef = 67,
            Accuracy = 4,
            Evasion = 5,
            Critical = 4,
            PAtkSpd = 253,
            CastSpd = 333,
            RunSpeed = 120,
            Following = true,
            TargetObjectId = 9,
            Load = 1,
            MaxLoad = 2,
            SkillIds = [4068, 4139],
            Dead = true
        }));
        Assert.Equal((5, SummonKindNet.Servitor, 14111, "Kat the Cat", 22), (info.ObjectId, info.Kind, info.NpcId, info.Name, info.Level));
        Assert.Equal(1L << 40, info.Exp);
        Assert.Equal((600, 1200, 253, 120, true, 9), (info.Feed, info.MaxFeed, info.PAtkSpd, info.RunSpeed, info.Following, info.TargetObjectId));
        Assert.Equal([4068, 4139], info.SkillIds);
        Assert.True(info.Dead);
        Assert.False(info.Mountable);

        var mounted = NetCodec.DecodeSummonInfo(NetCodec.EncodeSummonInfo(new SummonInfoNetMsg
        { ObjectId = 1, Kind = SummonKindNet.Pet, NpcId = 12526, Mountable = true }));
        Assert.True(mounted.Mountable);
        byte[] infoBytes = NetCodec.EncodeSummonInfo(new SummonInfoNetMsg { ObjectId = 1, Kind = SummonKindNet.Pet, Mountable = true });
        var oldInfo = NetCodec.DecodeSummonInfo(infoBytes.AsSpan(0, infoBytes.Length - 1));
        Assert.False(oldInfo.Mountable);

        var gone = NetCodec.DecodeSummonGone(NetCodec.EncodeSummonGone(new SummonGoneNetMsg { ObjectId = 5, Kind = SummonKindNet.Pet, Reason = 3 }));
        Assert.Equal((5, SummonKindNet.Pet, 3), (gone.ObjectId, gone.Kind, gone.Reason));

        var bag = NetCodec.DecodePetInventory(NetCodec.EncodePetInventory(new PetInventoryNetMsg
        {
            PetObjectId = 5,
            Items = [new PetItemNet { ObjectId = 1, ItemId = 2515, Count = 10 }, new PetItemNet { ObjectId = 2, ItemId = 2375, Count = 1, Equipped = true, EnchantLevel = 3 }]
        }));
        Assert.Equal(5, bag.PetObjectId);
        Assert.Equal(2, bag.Items.Length);
        Assert.True(bag.Items[1].Equipped);
        Assert.Equal(3, bag.Items[1].EnchantLevel);
    }

    [Fact]
    public void Entity_spawn_carries_summon_owner_tail_and_old_payloads_decode_as_none()
    {
        var spawn = NetCodec.DecodeEntitySpawn(NetCodec.EncodeEntitySpawn(new EntitySpawnMsg
        {
            EntityValue = 4,
            Kind = EntityKindNet.Npc,
            Name = "Kat the Cat",
            Title = "Owner",
            SummonKind = SummonKindNet.Servitor,
            OwnerObjectId = 268_435_500
        }));
        Assert.Equal(SummonKindNet.Servitor, spawn.SummonKind);
        Assert.Equal(268_435_500, spawn.OwnerObjectId);
        Assert.Equal("Owner", spawn.Title);

        byte[] full = NetCodec.EncodeEntitySpawn(new EntitySpawnMsg { EntityValue = 4, Name = "Old", Hero = true });
        var old = NetCodec.DecodeEntitySpawn(full.AsSpan(0, full.Length - 16)); // summon tail (5) + empty cubic list (2) + duel team (1) + rhand/lhand (8)
        Assert.True(old.Hero);
        Assert.Equal(SummonKindNet.None, old.SummonKind);
        Assert.Equal(0, old.OwnerObjectId);
    }

    [Fact]
    public void Cubic_lists_ride_on_char_info_and_user_info_and_old_payloads_decode_as_empty()
    {
        var spawn = NetCodec.DecodeEntitySpawn(NetCodec.EncodeEntitySpawn(new EntitySpawnMsg { EntityValue = 5, Name = "Warlock", Cubics = [1, 3] }));
        Assert.Equal(new[] { 1, 3 }, spawn.Cubics);
        byte[] full = NetCodec.EncodeEntitySpawn(new EntitySpawnMsg { EntityValue = 5, Name = "Old", OwnerObjectId = 7, Cubics = [2] });
        var old = NetCodec.DecodeEntitySpawn(full.AsSpan(0, full.Length - 15)); // cubic list (2 + 4) + duel team (1) + rhand/lhand (8)
        Assert.Equal(7, old.OwnerObjectId);
        Assert.Empty(old.Cubics);

        var info = NetCodec.DecodeUserInfo(NetCodec.EncodeUserInfo(new UserInfoNetMsg { Name = "Warlock", Hero = true, ClassIndex = 1, Cubics = [9] }));
        Assert.Equal(new[] { 9 }, info.Cubics);
        Assert.Equal(1, info.ClassIndex);
        byte[] ui = NetCodec.EncodeUserInfo(new UserInfoNetMsg { Name = "Old", ClassIndex = 2, Cubics = [4] });
        var oldInfo = NetCodec.DecodeUserInfo(ui.AsSpan(0, ui.Length - 7));
        Assert.Equal(2, oldInfo.ClassIndex);
        Assert.Empty(oldInfo.Cubics);
    }
}
