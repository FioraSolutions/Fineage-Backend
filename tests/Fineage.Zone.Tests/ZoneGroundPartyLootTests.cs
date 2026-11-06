using Fineage.Game.Services;
using Fineage.Protocol;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Mobius Party.distributeItem(spoil=false) on ground pickup.</summary>
public sealed class ZoneGroundPartyLootTests
{
    const int Stem = 1864;
    const int Adena = 57;

    [Fact]
    public async Task By_turn_pickup_gives_item_to_next_party_member()
    {
        var kit = new NetTestKit();
        var (s1, leader) = await kit.EnterAsync(classId: 54, race: 4);
        var (s2, mate) = await kit.EnterAsync(classId: 54, race: 4, x: leader.X + 20, y: leader.Y, z: leader.Z);
        // Leader first → ByTurn cursor 0→1 → mate
        kit.World.JoinPartyForTest(PartyDistributionType.ByTurn, leader, mate);

        Assert.Null(await kit.World.TryGiveItemAsync(s1, new GiveItemMsg { ItemId = Stem, Count = 3 }, default));
        var stem = leader.Inventory!.FindByItemId(Stem)!;
        Assert.Null(await kit.World.TryDropItemAsync(s1, new DropItemRequestMsg
        {
            ObjectId = stem.ObjectId,
            Count = 3,
            X = leader.X + 5,
            Y = leader.Y,
            Z = leader.Z
        }, default));

        Assert.Equal(1, kit.World.GroundItemCountForTest());
        int groundId = FindGroundId(kit);

        long beforeLeader = leader.Inventory.CountOf(Stem);
        long beforeMate = mate.Inventory!.CountOf(Stem);
        Assert.Null(await kit.World.TryPickupAsync(s1, new PickupRequestMsg { ObjectId = groundId }, default));
        Assert.Equal(beforeLeader, leader.Inventory.CountOf(Stem));
        Assert.Equal(beforeMate + 3, mate.Inventory.CountOf(Stem));
        Assert.Equal(0, kit.World.GroundItemCountForTest());
    }

    [Fact]
    public async Task Adena_pickup_splits_among_in_range_party()
    {
        var kit = new NetTestKit();
        var (s1, leader) = await kit.EnterAsync(classId: 54, race: 4);
        var (_, mate) = await kit.EnterAsync(classId: 54, race: 4, x: leader.X + 20, y: leader.Y, z: leader.Z);
        kit.World.JoinPartyForTest(PartyDistributionType.FindersKeepers, leader, mate);

        Assert.Null(await kit.World.TryGiveItemAsync(s1, new GiveItemMsg { ItemId = Adena, Count = 100 }, default));
        var bag = leader.Inventory!.FindByItemId(Adena)!;
        Assert.Null(await kit.World.TryDropItemAsync(s1, new DropItemRequestMsg
        {
            ObjectId = bag.ObjectId,
            Count = 100,
            X = leader.X + 5,
            Y = leader.Y,
            Z = leader.Z
        }, default));

        int groundId = FindGroundId(kit);
        long beforeL = leader.Inventory.CountOf(Adena);
        long beforeM = mate.Inventory!.CountOf(Adena);
        Assert.Null(await kit.World.TryPickupAsync(s1, new PickupRequestMsg { ObjectId = groundId }, default));
        Assert.Equal(beforeL + 50, leader.Inventory.CountOf(Adena));
        Assert.Equal(beforeM + 50, mate.Inventory.CountOf(Adena));
    }

    [Fact]
    public async Task Finders_keepers_pickup_stays_with_picker()
    {
        var kit = new NetTestKit();
        var (s1, leader) = await kit.EnterAsync(classId: 54, race: 4);
        var (_, mate) = await kit.EnterAsync(classId: 54, race: 4, x: leader.X + 20, y: leader.Y, z: leader.Z);
        kit.World.JoinPartyForTest(PartyDistributionType.FindersKeepers, leader, mate);

        Assert.Null(await kit.World.TryGiveItemAsync(s1, new GiveItemMsg { ItemId = Stem, Count = 2 }, default));
        var stem = leader.Inventory!.FindByItemId(Stem)!;
        Assert.Null(await kit.World.TryDropItemAsync(s1, new DropItemRequestMsg
        {
            ObjectId = stem.ObjectId,
            Count = 2,
            X = leader.X + 5,
            Y = leader.Y,
            Z = leader.Z
        }, default));

        int groundId = FindGroundId(kit);
        long beforeMate = mate.Inventory!.CountOf(Stem);
        Assert.Null(await kit.World.TryPickupAsync(s1, new PickupRequestMsg { ObjectId = groundId }, default));
        Assert.Equal(2, leader.Inventory.CountOf(Stem));
        Assert.Equal(beforeMate, mate.Inventory.CountOf(Stem));
    }

    static int FindGroundId(NetTestKit kit)
    {
        // Ground stacks are the only GroundItem entities after a controlled drop.
        return kit.World.FindGroundObjectIdForTest();
    }
}
