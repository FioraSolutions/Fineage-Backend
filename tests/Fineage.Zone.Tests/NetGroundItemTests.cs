using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Net;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Mobius RequestDropItem / AI_INTENTION_PICK_UP on Net: far drops are refused with SysMsg 151 and the bag row
/// re-sent; near drops land a ground item visible to the dropper; pickups merge stacks as Modify (client applies
/// without relog) and far pickups run in first.
/// </summary>
public sealed class NetGroundItemTests
{
    const int Adena = 57;

    static async Task<(NetTestKit Kit, ZoneSession Session, ZoneEntity Player, NetBridgeRecorder Rec)> SetupAsync()
    {
        var kit = new NetTestKit();
        var (session, player) = await kit.EnterAsync();
        player.Inventory!.Add(Adena, 1000);
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        return (kit, session, player, rec);
    }

    static List<InventoryUpdateEntryNet> InvRows(NetBridgeRecorder rec) =>
        rec.Of(nameof(INetPresenceBridge.FanoutInventoryUpdate))
            .SelectMany(a => ((InventoryUpdateNetMsg)a[1]!).Changes).ToList();

    static List<int> SysIds(NetBridgeRecorder rec) =>
        rec.Of(nameof(INetPresenceBridge.FanoutSystemMessage)).Select(a => ((SystemMessageNetMsg)a[1]!).Id).ToList();

    [Fact]
    public async Task Far_drop_is_refused_with_151_and_bag_row_resent()
    {
        var (kit, session, player, rec) = await SetupAsync();
        var stack = player.Inventory!.FindByItemId(Adena)!;

        string? err = await kit.World.TryDropItemAsync(session, new DropItemRequestMsg
        {
            ObjectId = stack.ObjectId, Count = 100, X = player.X + 600, Y = player.Y, Z = player.Z
        }, CancellationToken.None);

        Assert.Equal("range", err);
        Assert.Equal(1000, stack.Count);
        Assert.Contains(ZoneWorld.SysMsgDiscardTooFar, SysIds(rec));
        Assert.Contains(InvRows(rec), r => r.ObjectId == stack.ObjectId && r.Count == 1000 && r.Change == InventoryChange.Modify);
        Assert.Empty(rec.Of(nameof(INetPresenceBridge.FanoutDropItem)));
    }

    [Fact]
    public async Task Near_drop_lands_ground_item_for_dropper_and_pickup_merges_as_modify()
    {
        var (kit, session, player, rec) = await SetupAsync();
        var stack = player.Inventory!.FindByItemId(Adena)!;

        Assert.Null(await kit.World.TryDropItemAsync(session, new DropItemRequestMsg
        {
            ObjectId = stack.ObjectId, Count = 100, X = player.X + 60, Y = player.Y, Z = player.Z
        }, CancellationToken.None));
        Assert.Equal(900, stack.Count);
        var drop = (DropItemNetMsg)Assert.Single(rec.Of(nameof(INetPresenceBridge.FanoutDropItem)))[1]!;
        Assert.Equal(Adena, drop.ItemId);
        Assert.Equal(100, drop.Count);
        int groundId = (int)drop.EntityValue;
        Assert.Contains(groundId, player.Knownlist);

        rec.Calls.Clear();
        Assert.Null(await kit.World.TryPickupAsync(session, new PickupRequestMsg { ObjectId = groundId }, CancellationToken.None));
        Assert.Equal(1000, stack.Count);
        var row = Assert.Single(InvRows(rec));
        Assert.Equal(stack.ObjectId, row.ObjectId);
        Assert.Equal(InventoryChange.Modify, row.Change); // Add for a held objectId is ignored by the client
        Assert.Equal(1000, row.Count);
        Assert.Contains(ZoneWorld.SysMsgObtainedAdena, SysIds(rec));
        Assert.NotEmpty(rec.Of(nameof(INetPresenceBridge.FanoutEntityDespawn)));
        Assert.False(kit.World.TryGetEntityById(groundId, out _));
    }

    [Fact]
    public async Task Far_pickup_runs_in_then_picks_up_on_arrival()
    {
        var (kit, session, player, rec) = await SetupAsync();
        var stack = player.Inventory!.FindByItemId(Adena)!;
        Assert.Null(await kit.World.TryDropItemAsync(session, new DropItemRequestMsg
        {
            ObjectId = stack.ObjectId, Count = 100, X = player.X + 100, Y = player.Y, Z = player.Z
        }, CancellationToken.None));
        int groundId = (int)((DropItemNetMsg)rec.Of(nameof(INetPresenceBridge.FanoutDropItem))[0][1]!).EntityValue;

        // Walk away 400u (pickup radius is 150).
        Assert.True(kit.World.TryGetEntityById(groundId, out var ground));
        player.X -= 400;
        rec.Calls.Clear();

        Assert.Null(await kit.World.TryPickupAsync(session, new PickupRequestMsg { ObjectId = groundId }, CancellationToken.None));
        Assert.Equal(900, stack.Count);
        Assert.True(player.MoveActive);
        Assert.Equal(groundId, session.PendingPickupId);
        Assert.Empty(InvRows(rec));

        kit.Now = kit.Now.AddMilliseconds(session.PendingPickupAtUnixMs - kit.Now.ToUnixTimeMilliseconds() + 50);
        await kit.World.RunPlayerCombatTickAsync(CancellationToken.None);

        Assert.Equal(1000, stack.Count);
        Assert.True(ZoneCombat.InPickupRange(player.X, player.Y, ground.X, ground.Y));
        Assert.Contains(InvRows(rec), r => r.ObjectId == stack.ObjectId && r.Count == 1000);
        Assert.False(kit.World.TryGetEntityById(groundId, out _));
    }
}
