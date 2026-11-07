using Fineage.Game;
using Fineage.Game.Services;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Net;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Mobius carry limits on the two paths that used to transfer unconditionally:
/// <list type="bullet">
/// <item><c>Player.doPickupItem</c> (<c>Player.java:4228</c>) → <c>Inventory.validateCapacity(target)</c> only.
/// Weight is deliberately *not* checked on pickup (<c>validateWeight</c> lives on buy / withdraw / trade), so an
/// overloaded character still loots and just carries the 4270 penalty.</item>
/// <item><c>TradeList.doExchange</c> (<c>TradeList.java:574-583</c>) → <c>validateWeight</c> then
/// <c>validateCapacity</c> per receiving side, and both partners are told the same reason.</item>
/// </list>
/// </summary>
public sealed class CarryLimitTests
{
    /// <summary>Non-stackable etc item (Mobius <c>template.isStackable() == false</c>): one bag slot per piece.</summary>
    const int Harvester = 5125;
    /// <summary>Another non-stackable etc item, used to fill a bag without colliding with the traded payload.</summary>
    const int FishingManual = 7561;
    /// <summary>Stackable template (weight 6), so a pickup can overload without spending a free slot.</summary>
    const int WoodenArrow = 17;

    static List<int> SysIds(NetBridgeRecorder rec) =>
        rec.Of(nameof(INetPresenceBridge.FanoutSystemMessage)).Select(a => ((SystemMessageNetMsg)a[1]!).Id).ToList();

    static List<InventoryUpdateEntryNet> InvRows(NetBridgeRecorder rec) =>
        rec.Of(nameof(INetPresenceBridge.FanoutInventoryUpdate))
            .SelectMany(a => ((InventoryUpdateNetMsg)a[1]!).Changes).ToList();

    /// <summary>Fills the bag to exactly <see cref="WarehouseRules.InventorySlots"/> rows with single pieces.</summary>
    static void FillBag(ZoneEntity player, int itemId = FishingManual)
    {
        int limit = WarehouseRules.InventorySlots(player.Race);
        int need = limit - player.Inventory!.Items.Count;
        Assert.True(need > 0, "starter kit already fills the bag");
        player.Inventory.Add(itemId, need);
        Assert.Equal(limit, player.Inventory.Items.Count);
    }

    static async Task<(NetTestKit Kit, ZoneSession Session, ZoneEntity Player, NetBridgeRecorder Rec)> SetupAsync(
        ItemWeights? weights = null, int classId = 25, int race = 1)
    {
        var kit = new NetTestKit(weights: weights);
        var (session, player) = await kit.EnterAsync(classId: classId, race: race);
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        return (kit, session, player, rec);
    }

    [Fact]
    public async Task Full_bag_refuses_ground_pickup_with_129_and_leaves_the_item_on_the_ground()
    {
        var (kit, session, player, rec) = await SetupAsync();
        FillBag(player);
        int ground = kit.World.PlaceGroundForTest(player, Harvester);

        Assert.Equal("slots", await kit.World.TryPickupAsync(session, ground, CancellationToken.None));

        Assert.Contains(ZoneWorld.SysMsgInventoryFull, SysIds(rec));
        Assert.True(kit.World.ExistsForTest(ground));
        Assert.Empty(InvRows(rec));
        Assert.Equal(WarehouseRules.InventorySlots(player.Race), player.Inventory!.Items.Count);
    }

    [Fact]
    public async Task Stackable_item_already_carried_still_picks_up_into_the_full_bag()
    {
        var (kit, session, player, rec) = await SetupAsync();
        var arrows = player.Inventory!.Add(WoodenArrow, 10);
        FillBag(player); // the arrow row is already counted, so the bag is at the slot limit
        int ground = kit.World.PlaceGroundForTest(player, WoodenArrow, count: 5);

        Assert.Null(await kit.World.TryPickupAsync(session, ground, CancellationToken.None));

        Assert.Equal(15, arrows.Count);
        Assert.DoesNotContain(ZoneWorld.SysMsgInventoryFull, SysIds(rec));
        Assert.False(kit.World.ExistsForTest(ground));
    }

    [Fact]
    public async Task Overloaded_pickup_is_allowed_because_mobius_does_not_weight_check_loot()
    {
        var (kit, session, player, rec) = await SetupAsync();
        int maxLoad = kit.World.MaxLoadOf(player);
        // Land one arrow so the payload merges into an existing stack: no slot is spent, only weight changes.
        var arrows = player.Inventory!.Add(WoodenArrow, 1);
        int weightPer = arrows.Template.Weight;
        Assert.True(weightPer > 0, "water arrow template needs a weight");
        int count = (int)(maxLoad / weightPer) + 100;
        int ground = kit.World.PlaceGroundForTest(player, WoodenArrow, count);

        Assert.Null(await kit.World.TryPickupAsync(session, ground, CancellationToken.None));

        Assert.Equal(1 + count, arrows.Count);
        Assert.True(ZoneWorld.CurrentLoadOf(player) > maxLoad);
        // The weight penalty (4270) is the only consequence — Mobius never answers a pickup with 129/422 for weight.
        Assert.DoesNotContain(ZoneWorld.SysMsgWeightLimit, SysIds(rec));
        Assert.DoesNotContain(ZoneWorld.SysMsgInventoryFull, SysIds(rec));
        Assert.False(kit.World.ExistsForTest(ground));
    }

    /// <summary>Two characters standing on the same tile with an open trade window, ready to confirm.</summary>
    static async Task<(NetTestKit Kit, ZoneSession A, ZoneEntity Left, ZoneSession B, ZoneEntity Right, NetBridgeRecorder Rec)>
        OpenTradeAsync(ItemWeights? weights = null)
    {
        var kit = new NetTestKit(weights: weights);
        var (leftSession, left) = await kit.EnterAsync();
        var (rightSession, right) = await kit.EnterAsync();
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;

        Assert.Null(await kit.World.TryTradeRequestAsync(
            leftSession, new TradeRequestMsg { TargetObjectId = right.ObjectId }, CancellationToken.None));
        Assert.Null(await kit.World.TryTradeAnswerAsync(
            rightSession, new TradeAnswerRequestMsg { RequestorObjectId = left.ObjectId, Response = 1 }, CancellationToken.None));
        rec.Calls.Clear();
        return (kit, leftSession, left, rightSession, right, rec);
    }

    static async Task ConfirmBothAsync(NetTestKit kit, ZoneSession a, ZoneSession b)
    {
        Assert.Null(await kit.World.TryTradeDoneAsync(a, new TradeDoneRequestMsg { Response = 1 }, CancellationToken.None));
        Assert.Null(await kit.World.TryTradeDoneAsync(b, new TradeDoneRequestMsg { Response = 1 }, CancellationToken.None));
    }

    [Fact]
    public async Task Trade_that_would_overload_the_receiver_is_refused_with_422_for_both()
    {
        // One piece of this item is heavier than the whole limit, so the receiver's validateWeight must fail.
        var (kit, a, left, b, right, rec) = await OpenTradeAsync(
            weights: new ItemWeights(new Dictionary<int, int> { [Harvester] = 1_000_000 }));
        var heavy = left.Inventory!.Add(Harvester, 1);

        Assert.Null(await kit.World.TryTradeAddItemAsync(
            a, new TradeAddItemRequestMsg { InventoryObjectId = heavy.ObjectId, Count = 1 }, CancellationToken.None));
        await ConfirmBothAsync(kit, a, b);

        Assert.Equal(2, SysIds(rec).Count(id => id == ZoneWorld.SysMsgWeightLimit));
        Assert.DoesNotContain(ZoneWorld.SysMsgInventoryFull, SysIds(rec));
        // Nothing moved: the heavy piece stays with the seller and the receiver's bag is untouched.
        Assert.Equal(1, left.Inventory.CountOf(Harvester));
        Assert.Equal(0, right.Inventory!.CountOf(Harvester));
        Assert.Empty(InvRows(rec));
        Assert.Contains(rec.Of(nameof(INetPresenceBridge.FanoutTradeFinished)),
            args => ((TradeFinishedNetMsg)args[1]!).Response == 0);
    }

    [Fact]
    public async Task Trade_that_would_overflow_the_receiver_bag_is_refused_with_129_for_both()
    {
        var (kit, a, left, b, right, rec) = await OpenTradeAsync();
        FillBag(right);
        var piece = left.Inventory!.Add(Harvester, 1);

        Assert.Null(await kit.World.TryTradeAddItemAsync(
            a, new TradeAddItemRequestMsg { InventoryObjectId = piece.ObjectId, Count = 1 }, CancellationToken.None));
        await ConfirmBothAsync(kit, a, b);

        Assert.Equal(2, SysIds(rec).Count(id => id == ZoneWorld.SysMsgInventoryFull));
        Assert.Equal(1, left.Inventory.CountOf(Harvester));
        Assert.Equal(0, right.Inventory!.CountOf(Harvester));
        Assert.Equal(WarehouseRules.InventorySlots(right.Race), right.Inventory.Items.Count);
        Assert.Empty(InvRows(rec));
        Assert.Contains(rec.Of(nameof(INetPresenceBridge.FanoutTradeFinished)),
            args => ((TradeFinishedNetMsg)args[1]!).Response == 0);
    }

    [Fact]
    public async Task Trade_into_an_existing_stack_needs_no_slot_and_goes_through()
    {
        var (kit, a, left, b, right, rec) = await OpenTradeAsync();
        var arrows = left.Inventory!.Add(WoodenArrow, 10);
        right.Inventory!.Add(WoodenArrow, 1); // the receiver already holds the stack
        FillBag(right);

        Assert.Null(await kit.World.TryTradeAddItemAsync(
            a, new TradeAddItemRequestMsg { InventoryObjectId = arrows.ObjectId, Count = 4 }, CancellationToken.None));
        await ConfirmBothAsync(kit, a, b);

        Assert.Equal(6, left.Inventory.CountOf(WoodenArrow));
        Assert.Equal(5, right.Inventory.CountOf(WoodenArrow));
        Assert.Empty(SysIds(rec));
        Assert.Contains(rec.Of(nameof(INetPresenceBridge.FanoutTradeFinished)),
            args => ((TradeFinishedNetMsg)args[1]!).Response == 1);
    }
}
