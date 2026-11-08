using Fineage.Net.Contract;
using Xunit;

namespace Fineage.Net.Tests;

/// <summary>
/// Mandatory encode/decode round-trips for UserInfo / stats / NPC HTML (#353 NetCodec track).
/// </summary>
public sealed class PresenceCodecRoundTripTests
{
    [Fact]
    public void UserInfo_roundtrip_with_paperdoll()
    {
        var back = NetCodec.DecodeUserInfo(NetCodec.EncodeUserInfo(new UserInfoNetMsg
        {
            EntityValue = 11,
            EntityGeneration = 2,
            Name = "Scout",
            ClassId = 0,
            Race = 0,
            Sex = 0,
            X = 1,
            Y = 2,
            Z = 3,
            RunSpeed = 120f,
            WalkSpeed = 80f,
            Running = true,
            Level = 5,
            Exp = 1000,
            Sp = 50,
            CurHp = 40,
            MaxHp = 100,
            CurMp = 20,
            MaxMp = 50,
            CurCp = 10,
            MaxCp = 30,
            Str = 40,
            Dex = 30,
            Con = 43,
            Int = 21,
            Wit = 11,
            Men = 25,
            PAtk = 12,
            MAtk = 8,
            PDef = 40,
            MDef = 35,
            PAtkSpd = 300,
            MAtkSpd = 333,
            Accuracy = 50,
            Evasion = 40,
            CritRate = 40,
            WeaponGradePenalty = 0,
            ArmorGradePenalty = 1,
            ClanId = 7,
            Paperdoll =
            [
                new PaperdollSlotNet { ObjectId = 9, ItemId = 1, EnchantLevel = 0 },
                new PaperdollSlotNet { ObjectId = 10, ItemId = 2, EnchantLevel = 3 }
            ]
        }));
        Assert.Equal("Scout", back.Name);
        Assert.Equal(5, back.Level);
        Assert.Equal(1000, back.Exp);
        Assert.Equal(40, back.CurHp);
        Assert.Equal(1, back.ArmorGradePenalty);
        Assert.Equal(7, back.ClanId);
        Assert.Equal(2, back.Paperdoll.Length);
        Assert.Equal(2, back.Paperdoll[1].ItemId);
        Assert.Equal(3, back.Paperdoll[1].EnchantLevel);
    }

    [Fact]
    public void PlayerStats_and_NpcHtml_roundtrip()
    {
        var stats = NetCodec.DecodePlayerStats(NetCodec.EncodePlayerStats(new PlayerStatsMsg
        {
            EntityValue = 3,
            EntityGeneration = 1,
            Level = 12,
            Exp = 999,
            Sp = 40,
            Hp = 80,
            MaxHp = 100,
            Mp = 30,
            MaxMp = 50,
            Cp = 5,
            MaxCp = 20
        }));
        Assert.Equal(12, stats.Level);
        Assert.Equal(80, stats.Hp);
        Assert.Equal(20, stats.MaxCp);

        var html = NetCodec.DecodeNpcHtml(NetCodec.EncodeNpcHtml(new NpcHtmlNetMsg
        {
            NpcObjectId = 55,
            NpcId = 30001,
            Html = "<html><body>hi</body></html>",
            ItemId = 0
        }));
        Assert.Equal(55, html.NpcObjectId);
        Assert.Equal(30001, html.NpcId);
        Assert.Equal("<html><body>hi</body></html>", html.Html);
    }

    [Fact]
    public void SocialAction_and_AutoSoulShot_roundtrip()
    {
        var social = NetCodec.DecodeSocialAction(NetCodec.EncodeSocialAction(new SocialActionNetMsg
        {
            EntityValue = 8,
            EntityGeneration = 1,
            ActionId = 15
        }));
        Assert.Equal(8uL, social.EntityValue);
        Assert.Equal(15, social.ActionId);

        var shot = NetCodec.DecodeAutoSoulShot(NetCodec.EncodeAutoSoulShot(new AutoSoulShotNetMsg
        {
            ItemId = 1835,
            Enabled = true
        }));
        Assert.Equal(1835, shot.ItemId);
        Assert.True(shot.Enabled);
    }
}
