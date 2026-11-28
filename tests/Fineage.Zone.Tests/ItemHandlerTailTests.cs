using Fineage.Game.Services;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>#925: Mobius Book / Maps / RollingDice / Calculator / SpecialXMas via ItemHandlerCatalog.</summary>
public sealed class ItemHandlerTailTests
{
    const int WorldMap = 1665;
    const int Calculator = 4393;
    const int Dice = 4625;
    const int XMasSeal = 5555;
    const int GuideBook = 5588;

    static NetBridgeRecorder Recorder(NetTestKit kit)
    {
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        return rec;
    }

    static int Grant(NetTestKit kit, ZoneEntity player, int itemId, int count = 1)
    {
        lock (kit.World)
        {
            var item = player.Inventory!.Add(itemId, count);
            player.Inventory.DiscardChanges();
            return item.ObjectId;
        }
    }

    [Fact]
    public void Catalog_loads_mobius_named_handlers()
    {
        var catalog = ItemHandlerCatalog.LoadDefault();
        Assert.True(catalog.TryGet(WorldMap, out string maps));
        Assert.Equal("Maps", maps);
        Assert.True(catalog.TryGet(Calculator, out string calc));
        Assert.Equal("Calculator", calc);
        Assert.True(catalog.TryGet(Dice, out string dice));
        Assert.Equal("RollingDice", dice);
        Assert.True(catalog.TryGet(XMasSeal, out string xmas));
        Assert.Equal("SpecialXMas", xmas);
        Assert.True(catalog.TryGet(GuideBook, out string book));
        Assert.Equal("Book", book);
    }

    [Fact]
    public void Codec_roundtrips_item_handler_packets()
    {
        var dice = new DiceNetMsg { ObjectId = 1, ItemId = Dice, Number = 4, X = 10, Y = 20, Z = 30 };
        var dice2 = NetCodec.DecodeDice(NetCodec.EncodeDice(dice));
        Assert.Equal(4, dice2.Number);
        Assert.Equal(30, dice2.Z);

        var map = new ShowMiniMapNetMsg { MapId = WorldMap, SevenSignsPeriod = 2 };
        var map2 = NetCodec.DecodeShowMiniMap(NetCodec.EncodeShowMiniMap(map));
        Assert.Equal(WorldMap, map2.MapId);
        Assert.Equal(2, map2.SevenSignsPeriod);

        var calc = NetCodec.DecodeShowCalculator(
            NetCodec.EncodeShowCalculator(new ShowCalculatorNetMsg { ItemId = Calculator }));
        Assert.Equal(Calculator, calc.ItemId);

        var seal = NetCodec.DecodeShowXMasSeal(
            NetCodec.EncodeShowXMasSeal(new ShowXMasSealNetMsg { ItemId = XMasSeal }));
        Assert.Equal(XMasSeal, seal.ItemId);
    }

    [Fact]
    public async Task Calculator_opens_ui_without_consuming()
    {
        var kit = new NetTestKit();
        var rec = Recorder(kit);
        var (s, p) = await kit.EnterAsync(classId: 0);
        int oid = Grant(kit, p, Calculator);

        Assert.Null(await kit.World.TryUseItemAsync(s, oid, CancellationToken.None));

        Assert.Equal(1, p.Inventory!.CountOf(Calculator));
        var msg = Assert.IsType<ShowCalculatorMsg>(
            rec.Of(nameof(IWorldEventPublisher.FanoutShowCalculator)).Last()[1]);
        Assert.Equal(Calculator, msg.ItemId);
    }

    [Fact]
    public async Task Map_opens_minimap_with_seven_signs_period()
    {
        var kit = new NetTestKit();
        var rec = Recorder(kit);
        var (s, p) = await kit.EnterAsync(classId: 0);
        int oid = Grant(kit, p, WorldMap);

        Assert.Null(await kit.World.TryUseItemAsync(s, oid, CancellationToken.None));

        Assert.Equal(1, p.Inventory!.CountOf(WorldMap));
        var msg = Assert.IsType<ShowMiniMapMsg>(
            rec.Of(nameof(IWorldEventPublisher.FanoutShowMiniMap)).Last()[1]);
        Assert.Equal(WorldMap, msg.MapId);
        Assert.Equal((int)kit.World.SevenSigns.Period, msg.SevenSignsPeriod);
    }

    [Fact]
    public async Task Xmas_seal_opens_ui()
    {
        var kit = new NetTestKit();
        var rec = Recorder(kit);
        var (s, p) = await kit.EnterAsync(classId: 0);
        int oid = Grant(kit, p, XMasSeal);

        Assert.Null(await kit.World.TryUseItemAsync(s, oid, CancellationToken.None));

        var msg = Assert.IsType<ShowXMasSealMsg>(
            rec.Of(nameof(IWorldEventPublisher.FanoutShowXMasSeal)).Last()[1]);
        Assert.Equal(XMasSeal, msg.ItemId);
        Assert.Equal(1, p.Inventory!.CountOf(XMasSeal));
    }

    [Fact]
    public async Task Book_sends_help_html()
    {
        var kit = new NetTestKit();
        var rec = Recorder(kit);
        var (s, p) = await kit.EnterAsync(classId: 0);
        var held = p.Inventory!.Items.FirstOrDefault(item => item.ItemId == GuideBook);
        int oid = held?.ObjectId ?? Grant(kit, p, GuideBook);

        Assert.Null(await kit.World.TryUseItemAsync(s, oid, CancellationToken.None));

        var html = Assert.IsType<NpcHtmlMsg>(
            rec.Of(nameof(IWorldEventPublisher.FanoutNpcHtml)).Last()[1]);
        Assert.Equal(GuideBook, html.ItemId);
        Assert.False(string.IsNullOrWhiteSpace(html.Html));
        Assert.Equal(1, p.Inventory!.CountOf(GuideBook));
    }

    [Fact]
    public async Task Dice_broadcasts_roll_and_system_line()
    {
        var kit = new NetTestKit();
        var rec = Recorder(kit);
        var (s, p) = await kit.EnterAsync(classId: 0);
        int oid = Grant(kit, p, Dice);

        Assert.Null(await kit.World.TryUseItemAsync(s, oid, CancellationToken.None));

        var dice = Assert.IsType<DiceMsg>(
            rec.Of(nameof(IWorldEventPublisher.FanoutDice)).Last()[1]);
        Assert.Equal(p.ObjectId, dice.ObjectId);
        Assert.Equal(Dice, dice.ItemId);
        Assert.InRange(dice.Number, 1, 6);

        var line = Assert.IsType<SystemMessageMsg>(
            rec.Of(nameof(IWorldEventPublisher.FanoutSystemMessage)).Last()[1]);
        Assert.Equal(ItemHandlerProtocol.DiceRolled, line.Id);
        Assert.Equal(1, p.Inventory!.CountOf(Dice));
        Assert.True(p.DiceReadyAtMs > 0);
    }

    [Fact]
    public async Task Dice_flood_protector_refuses_second_roll()
    {
        var kit = new NetTestKit();
        var rec = Recorder(kit);
        var (s, p) = await kit.EnterAsync(classId: 0);
        int oid = Grant(kit, p, Dice);

        Assert.Null(await kit.World.TryUseItemAsync(s, oid, CancellationToken.None));
        rec.Calls.Clear();
        Assert.Null(await kit.World.TryUseItemAsync(s, oid, CancellationToken.None));

        Assert.Empty(rec.Of(nameof(IWorldEventPublisher.FanoutDice)));
        var line = Assert.IsType<SystemMessageMsg>(
            rec.Of(nameof(IWorldEventPublisher.FanoutSystemMessage)).Last()[1]);
        Assert.Equal(ItemHandlerProtocol.DiceNotReady, line.Id);
    }
}
