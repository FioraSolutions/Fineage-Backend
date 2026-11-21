using Fineage.Game.Shops;
using Fineage.Game.Wedding;
using Fineage.Protocol;
using Fineage.Zone.Punishment;
using Fineage.Zone.Wedding;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class WeddingTests
{
    static (NetTestKit Kit, ZoneSession Sa, ZoneEntity Pa, ZoneSession Sb, ZoneEntity Pb) EngagedPair(NetTestKit kit)
    {
        var a = kit.Create(sex: 0);
        var b = kit.Create(sex: 1);
        var (sa, pa) = kit.EnterAsync(a).GetAwaiter().GetResult();
        var (sb, pb) = kit.EnterAsync(b).GetAwaiter().GetResult();
        Assert.Null(kit.World.TryFriendInviteAsync(sa, new FriendInviteMsg { Name = b.Name }, default).GetAwaiter().GetResult());
        Assert.Null(kit.World.TryFriendAnswerAsync(sb, new FriendAnswerMsg { Response = 1 }, default).GetAwaiter().GetResult());
        pa.Session!.SelectedTargetId = pb.ObjectId;
        Assert.Null(kit.World.TryWeddingVoicedAsync(sa, WeddingProtocol.Engage, default).GetAwaiter().GetResult());
        Assert.Null(kit.World.TryWeddingEngageAnswerAsync(sb, 1, default).GetAwaiter().GetResult());
        return (kit, sa, pa, sb, pb);
    }

    static void WearFormal(NetTestKit kit, ZoneEntity player)
    {
        int itemId = kit.World.WeddingSettingsForTests.FormalWearItemId;
        var row = player.Inventory!.Add(itemId, 1);
        player.Inventory.Equip(row.ObjectId);
    }

    static void AddAdena(ZoneEntity player, long amount) =>
        player.Inventory!.Add(ShopTrade.AdenaItemId, checked((int)amount));

    static ZoneEntity WeddingNpc(NetTestKit kit, ZoneEntity near)
    {
        var npc = new ZoneEntity
        {
            Kind = ZoneEntityKind.Npc,
            NpcId = kit.World.WeddingSettingsForTests.ManagerNpcId,
            ObjectId = 8_000_001,
            X = near.X + 40,
            Y = near.Y,
            Z = near.Z
        };
        kit.World.FanoutNewNpcForTests(npc);
        kit.World.PlaceForTests(npc);
        return npc;
    }

    [Fact]
    public async Task Engage_decline_leaves_no_couple()
    {
        var kit = new NetTestKit();
        var a = kit.Create(sex: 0);
        var b = kit.Create(sex: 1);
        var (sa, pa) = await kit.EnterAsync(a);
        var (sb, pb) = await kit.EnterAsync(b);
        Assert.Null(await kit.World.TryFriendInviteAsync(sa, new FriendInviteMsg { Name = b.Name }, default));
        Assert.Null(await kit.World.TryFriendAnswerAsync(sb, new FriendAnswerMsg { Response = 1 }, default));
        pa.Session!.SelectedTargetId = pb.ObjectId;
        Assert.Null(await kit.World.TryWeddingVoicedAsync(sa, WeddingProtocol.Engage, default));
        Assert.Null(await kit.World.TryWeddingEngageAnswerAsync(sb, 0, default));
        Assert.Equal(0, pa.PartnerId);
        Assert.Equal(0, pb.PartnerId);
        Assert.Empty(kit.World.WeddingActorForTests.Couples);
    }

    [Fact]
    public void Engage_accept_creates_couple_and_persists()
    {
        var kit = new NetTestKit();
        var (_, _, pa, _, pb) = EngagedPair(kit);
        Assert.Equal(pb.ObjectId, pa.PartnerId);
        Assert.Equal(pa.ObjectId, pb.PartnerId);
        Assert.False(pa.Married);
        Assert.Single(kit.World.WeddingActorForTests.Couples);
        Assert.Single(kit.Store.ListWeddingCouples());
    }

    [Fact]
    public void Marriage_requires_formal_wear_and_adena()
    {
        var kit = new NetTestKit();
        var (_, sa, pa, sb, pb) = EngagedPair(kit);
        var npc = WeddingNpc(kit, pa);
        var settings = kit.World.WeddingSettingsForTests;
        WearFormal(kit, pa);
        WearFormal(kit, pb);
        AddAdena(pa, settings.WeddingPrice);
        AddAdena(pb, settings.WeddingPrice);
        kit.World.WeddingNpcBypassForTests(sa, pa, npc, "ask");
        kit.World.WeddingNpcBypassForTests(sb, pb, npc, "accept");
        Assert.True(pa.Married);
        Assert.True(pb.Married);
        Assert.True(kit.World.WeddingActorForTests.Find(pa.CoupleId)!.Married);
    }

    [Fact]
    public async Task Divorce_married_transfers_adena_percent()
    {
        var kit = new NetTestKit();
        var (_, sa, pa, sb, pb) = EngagedPair(kit);
        var npc = WeddingNpc(kit, pa);
        var settings = kit.World.WeddingSettingsForTests;
        WearFormal(kit, pa);
        WearFormal(kit, pb);
        AddAdena(pa, settings.WeddingPrice);
        AddAdena(pb, settings.WeddingPrice);
        kit.World.WeddingNpcBypassForTests(sa, pa, npc, "ask");
        kit.World.WeddingNpcBypassForTests(sb, pb, npc, "accept");
        long extra = 1_000_000;
        AddAdena(pa, extra);
        long before = pa.Inventory.CountOf(ShopTrade.AdenaItemId);
        int pct = settings.DivorceCostPercent;
        long expected = before / 100 * pct;
        Assert.Null(await kit.World.TryWeddingVoicedAsync(sa, WeddingProtocol.Divorce, default));
        Assert.Equal(before - expected, pa.Inventory.CountOf(ShopTrade.AdenaItemId));
        Assert.Equal(expected, pb.Inventory!.CountOf(ShopTrade.AdenaItemId));
        Assert.False(pa.Married);
        Assert.Equal(0, pa.PartnerId);
    }

    [Fact]
    public async Task Go_to_love_blocked_in_combat()
    {
        var kit = new NetTestKit();
        var (_, sa, pa, sb, pb) = EngagedPair(kit);
        var npc = WeddingNpc(kit, pa);
        var settings = kit.World.WeddingSettingsForTests;
        WearFormal(kit, pa);
        WearFormal(kit, pb);
        AddAdena(pa, settings.WeddingPrice);
        AddAdena(pb, settings.WeddingPrice);
        kit.World.WeddingNpcBypassForTests(sa, pa, npc, "ask");
        kit.World.WeddingNpcBypassForTests(sb, pb, npc, "accept");
        pa.CombatUntilUnixMs = kit.Now.ToUnixTimeMilliseconds() + 60_000;
        AddAdena(pb, settings.TeleportPrice);
        Assert.Null(await kit.World.TryWeddingVoicedAsync(sa, WeddingProtocol.GoToLove, default));
        Assert.Equal(0, pa.Intent.WeddingTeleportPartnerId);
    }

    [Fact]
    public async Task Go_to_love_blocked_in_festival_arena()
    {
        var kit = new NetTestKit();
        var (_, sa, pa, sb, pb) = EngagedPair(kit);
        var npc = WeddingNpc(kit, pa);
        var settings = kit.World.WeddingSettingsForTests;
        WearFormal(kit, pa);
        WearFormal(kit, pb);
        AddAdena(pa, settings.WeddingPrice);
        AddAdena(pb, settings.WeddingPrice);
        kit.World.WeddingNpcBypassForTests(sa, pa, npc, "ask");
        kit.World.WeddingNpcBypassForTests(sb, pb, npc, "accept");
        PlayerZoneChecks.RegisterFestivalParticipation(kit.World, pa.ObjectId, inside: true);
        AddAdena(pa, settings.TeleportPrice);
        Assert.Null(await kit.World.TryWeddingVoicedAsync(sa, WeddingProtocol.GoToLove, default));
        Assert.Equal(0, pa.Intent.WeddingTeleportPartnerId);
    }

    [Fact]
    public void Rules_reject_same_sex_when_disabled()
    {
        var cfg = new WeddingSettings { AllowSameSex = false };
        var self = new ZoneEntity { Kind = ZoneEntityKind.Player, Sex = 0 };
        var target = new ZoneEntity { Kind = ZoneEntityKind.Player, Sex = 0 };
        Assert.NotNull(WeddingRules.EngageError(cfg, self, target, true, false, false, false, false));
    }
}