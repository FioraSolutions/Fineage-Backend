using Fineage.Accounts;
using Fineage.Game;
using Fineage.Game.Shops;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>#924: Mobius Wear → ShopPreviewList / RequestPreviewItem → ShopPreviewInfo.</summary>
public sealed class WearPreviewTests
{
    const int MerchantObjectId = NetShopTests.MerchantObjectId;
    const int MerchantNpcId = NetShopTests.MerchantNpcId;
    const int Adena = NetShopTests.Adena;
    const int Blade = NetShopTests.Blade;
    const int Potion = NetShopTests.Potion;

    [Fact]
    public async Task Wear_bypass_sends_preview_list_of_equipable_rows()
    {
        var clock = new MutableClock();
        WorldBench bench = NetShopTests.Flat(clock, Kit((Adena, 1000)));
        BenchPlayer actor = await bench.EnterAsync("wear", "Wearer");

        Assert.Null(await actor.BypassAsync(MerchantObjectId, "Wear 1"));
        var list = LastPreviewList(bench.Rec);
        Assert.Equal(1, list.ListId);
        Assert.Equal(MerchantObjectId, list.NpcObjectId);
        Assert.Equal(1000, list.Adena);
        Assert.Contains(list.Items, row => row.ItemId == Blade && row.Price == ShopProtocol.WearPrice);
        Assert.DoesNotContain(list.Items, row => row.ItemId == Potion);
        Assert.Equal(1000, LastBalance(bench.Rec).Adena);
    }

    [Fact]
    public async Task Preview_charges_wear_price_and_sends_paperdoll_info()
    {
        var clock = new MutableClock();
        WorldBench bench = NetShopTests.Flat(clock, Kit((Adena, 1000)));
        BenchPlayer actor = await bench.EnterAsync("try", "TryOn");

        Assert.Null(await actor.BypassAsync(MerchantObjectId, "Wear 1"));
        Assert.Null(await actor.Preview(MerchantObjectId, 1, Blade));

        var info = LastPreviewInfo(bench.Rec);
        Assert.Equal(Blade, info.ItemIdsBySlot[(int)PaperdollSlot.RHand]);
        Assert.Equal(1000 - ShopProtocol.WearPrice, LastBalance(bench.Rec).Adena);
        Assert.Equal(1000 - ShopProtocol.WearPrice, actor.Saved().Single(i => i.ItemId == Adena).Count);
    }

    [Fact]
    public async Task Preview_logs_and_skips_unknown_item_without_charging()
    {
        var clock = new MutableClock();
        WorldBench bench = NetShopTests.Flat(clock, Kit((Adena, 1000)));
        BenchPlayer actor = await bench.EnterAsync("unk", "Unknown");

        Assert.Null(await actor.BypassAsync(MerchantObjectId, "Wear 1"));
        bench.Rec.Calls.Clear();
        Assert.Null(await actor.Preview(MerchantObjectId, 1, 999_001));
        Assert.Empty(bench.Rec.Of(nameof(IWorldEventPublisher.FanoutShopPreviewInfo)));
        Assert.Empty(bench.Rec.Of(nameof(IWorldEventPublisher.FanoutInventoryUpdate)));
        Assert.Equal(1000, actor.Saved().Single(i => i.ItemId == Adena).Count);
    }

    [Fact]
    public async Task Preview_refuses_slot_conflict()
    {
        var clock = new MutableClock();
        WorldBench bench = NetShopTests.Flat(clock, Kit((Adena, 1000)));
        BenchPlayer actor = await bench.EnterAsync("clash", "Clash");

        Assert.Null(await actor.BypassAsync(MerchantObjectId, "Wear 1"));
        // Two RightHand picks on one try-on — Mobius YOU_CAN_NOT_TRY_THOSE_ITEMS_ON_AT_THE_SAME_TIME.
        Assert.Null(await actor.Preview(MerchantObjectId, 1, Blade, Blade));
        Assert.Equal(ShopProtocol.WearSlotConflict, LastSystemMessage(bench.Rec).Id);
        Assert.Equal(1000, actor.Saved().Single(i => i.ItemId == Adena).Count);
    }

    [Fact]
    public void Preview_codec_roundtrips()
    {
        var req = new RequestPreviewItemNetMsg { NpcObjectId = 7, ListId = 3, ItemIds = [100, 200] };
        var decoded = NetCodec.DecodeRequestPreviewItem(NetCodec.EncodeRequestPreviewItem(req));
        Assert.Equal(7, decoded.NpcObjectId);
        Assert.Equal(3, decoded.ListId);
        Assert.Equal([100, 200], decoded.ItemIds);

        var list = new ShopPreviewListNetMsg
        {
            NpcObjectId = 1,
            ListId = 2,
            Adena = 50,
            Items = [new ShopPreviewListItemNet { ItemId = 10, Price = ShopProtocol.WearPrice }]
        };
        var list2 = NetCodec.DecodeShopPreviewList(NetCodec.EncodeShopPreviewList(list));
        Assert.Equal(50, list2.Adena);
        Assert.Equal(10, Assert.Single(list2.Items).ItemId);

        var info = new ShopPreviewInfoNetMsg { ItemIdsBySlot = new int[17] };
        info.ItemIdsBySlot[(int)PaperdollSlot.Chest] = 23;
        var info2 = NetCodec.DecodeShopPreviewInfo(NetCodec.EncodeShopPreviewInfo(info));
        Assert.Equal(23, info2.ItemIdsBySlot[(int)PaperdollSlot.Chest]);
    }

    static GameData Kit(params (int ItemId, int Count)[] rows) => NetShopTests.Kit(rows);

    static ShopPreviewListMsg LastPreviewList(NetBridgeRecorder rec) =>
        Assert.IsType<ShopPreviewListMsg>(rec.Of(nameof(IWorldEventPublisher.FanoutShopPreviewList)).Last()[1]);

    static ShopPreviewInfoMsg LastPreviewInfo(NetBridgeRecorder rec) =>
        Assert.IsType<ShopPreviewInfoMsg>(rec.Of(nameof(IWorldEventPublisher.FanoutShopPreviewInfo)).Last()[1]);

    static ShopBalanceMsg LastBalance(NetBridgeRecorder rec) =>
        Assert.IsType<ShopBalanceMsg>(rec.Of(nameof(IWorldEventPublisher.FanoutShopBalance)).Last()[1]);

    static SystemMessageMsg LastSystemMessage(NetBridgeRecorder rec) =>
        Assert.IsType<SystemMessageMsg>(rec.Of(nameof(IWorldEventPublisher.FanoutSystemMessage)).Last()[1]);
}
