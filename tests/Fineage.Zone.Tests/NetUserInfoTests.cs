using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Net;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Net UserInfo (Mobius status window sheet + paperdoll) and remote CharInfo paperdoll.</summary>
public sealed class NetUserInfoTests
{
    [Fact]
    public void UserInfo_codec_roundtrips_every_field()
    {
        var msg = new UserInfoNetMsg
        {
            EntityValue = 77, EntityGeneration = 1, Name = "Elf", ClassId = 25, Race = 1, Sex = 1,
            X = 1, Y = -2, Z = 3, RunSpeed = 126, WalkSpeed = 88, Running = false,
            Level = 7, Exp = 1234567890123, Sp = 9, CurHp = 10, MaxHp = 11, CurMp = 12, MaxMp = 13, CurCp = 14, MaxCp = 15,
            Str = 21, Dex = 24, Con = 25, Int = 37, Wit = 23, Men = 40,
            PAtk = 5, MAtk = 6, PDef = 50, MDef = 41, PAtkSpd = 300, MAtkSpd = 333, Accuracy = 30, Evasion = 31, CritRate = 44,
            WeaponGradePenalty = 1, ArmorGradePenalty = 2, ClanId = 3,
            Paperdoll = [new PaperdollSlotNet(), new PaperdollSlotNet { ObjectId = 100, ItemId = 177, EnchantLevel = 3 }]
        };
        var back = NetCodec.DecodeUserInfo(NetCodec.EncodeUserInfo(msg));
        Assert.Equivalent(msg, back, strict: true);
    }

    [Fact]
    public void EntitySpawn_paperdoll_is_optional_trailing_field()
    {
        var spawn = new EntitySpawnMsg { EntityValue = 5, EntityGeneration = 1, Kind = EntityKindNet.Player, Name = "P", Paperdoll = [0, 1101, 177] };
        byte[] wire = NetCodec.EncodeEntitySpawn(spawn);
        Assert.Equal(new[] { 0, 1101, 177 }, NetCodec.DecodeEntitySpawn(wire).Paperdoll);

        // Older peers stop after the Attackable byte: decode must still work with an empty paperdoll.
        int tailBytes = 2 + 3 * 4 + 1 + 4 + 4 + 4; // u16 length + 3 ints + Dead + Level + MaxHp + PAtkSpd
        var old = NetCodec.DecodeEntitySpawn(wire.AsSpan(0, wire.Length - tailBytes));
        Assert.Empty(old.Paperdoll);
        Assert.Equal("P", old.Name);
        Assert.Equal(0, old.Level); // trailing additions stay optional for old frames
    }

    [Fact]
    public async Task Elf_mage_enter_sheet_has_live_combat_stats_and_dressed_paperdoll()
    {
        var kit = new NetTestKit();
        var (session, elf) = await kit.EnterAsync(classId: 25, race: 1);
        Assert.True(kit.World.TryBuildNetUserInfo(session, out var info));

        var stats = kit.World.PlayerStats(elf);
        Assert.Equal(stats.PAtk, info.PAtk);
        Assert.Equal(stats.MAtk, info.MAtk);
        Assert.Equal(stats.PDef, info.PDef);
        Assert.Equal(stats.MDef, info.MDef);
        Assert.Equal(stats.PAtkSpd, info.PAtkSpd);
        Assert.Equal(stats.MAtkSpd, info.MAtkSpd);
        Assert.Equal(stats.Accuracy, info.Accuracy);
        Assert.Equal(stats.Evasion, info.Evasion);
        Assert.Equal(stats.CritRate, info.CritRate);
        Assert.True(info.PAtk > 0 && info.MAtk > 0 && info.PDef > 0 && info.MDef > 0 && info.PAtkSpd > 0);
        Assert.True(info.Int > 0 && info.Men > 0, "Elven Mystic base INT/MEN");
        Assert.Equal(1, info.Race);
        Assert.Equal(25, info.ClassId);
        Assert.Equal(elf.MaxHp, info.MaxHp);

        // Starter kit is equipped: paperdoll carries the same equipped rows as the inventory.
        Assert.Equal(InventoryMessages.PaperdollSlotCount, info.Paperdoll.Length);
        var equipped = elf.Inventory!.Items.Where(i => i.EquippedSlot != null).ToList();
        Assert.NotEmpty(equipped);
        foreach (var item in equipped)
        {
            var slot = info.Paperdoll[(int)item.EquippedSlot!.Value];
            Assert.Equal(item.ObjectId, slot.ObjectId);
            Assert.Equal(item.ItemId, slot.ItemId);
        }
        Assert.NotEqual(0, info.Paperdoll[7].ItemId); // right hand: starter staff/weapon

        // Remote presence (Mobius CharInfo) also carries the paperdoll ids.
        var remote = NetEnterWorldProjector.BuildRemoteSpawn(elf, EntityId.FromObjectId(elf.ObjectId, 1));
        Assert.Equal(InventoryMessages.ItemIds(elf.Inventory), remote.Paperdoll);
    }

    [Fact]
    public async Task Unequip_sends_net_userinfo_with_lower_pdef_and_charinfo_to_peer()
    {
        var kit = new NetTestKit();
        var (session, elf) = await kit.EnterAsync();
        var (_, peer) = await kit.EnterAsync(x: NetTestKit.ElfX + 100);
        Assert.Contains(peer.ObjectId, elf.Knownlist);
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;

        Assert.True(kit.World.TryBuildNetUserInfo(session, out var before));
        var armor = elf.Inventory!.Items.First(i => i.EquippedSlot != null && (int)i.EquippedSlot.Value != 7);
        int slot = (int)armor.EquippedSlot!.Value;
        Assert.Null(await kit.World.TryUnequipItemAsync(session, slot, CancellationToken.None));

        var infos = rec.Of(nameof(IWorldEventPublisher.FanoutUserInfo));
        Assert.Single(infos);
        Assert.Equal([elf.ObjectId], (IReadOnlyList<int>)infos[0][0]!);
        var after = (UserInfoNetMsg)infos[0][1]!;
        Assert.Equal(0, after.Paperdoll[slot].ItemId);
        Assert.True(after.PDef < before.PDef, $"pdef {before.PDef} -> {after.PDef}");

        var charInfos = rec.Of(nameof(IWorldEventPublisher.FanoutCharInfo));
        Assert.Single(charInfos);
        Assert.Contains(peer.ObjectId, (IReadOnlyList<int>)charInfos[0][0]!);
        Assert.Equal(elf.ObjectId, (int)charInfos[0][1]!);
    }
}
