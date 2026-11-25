using Fineage.Accounts;
using Fineage.Game.Services;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

[Trait("Category", "Integration")]
public sealed class NetMountTests
{
    const int StriderBugle = 4422, WindStrider = 12526, BCrystal = 1460;
    const int X = 49000, Y = 50000, Z = -3000;

    static int CollarOf(BenchPlayer owner, int itemId) =>
        owner.Inventory.Items.Single(i => i.ItemId == itemId).ObjectId;

    static async Task<(WorldBench Bench, BenchPlayer Owner)> RiderAsync()
    {
        var bench = new WorldBench();
        var owner = await bench.EnterAsync(
            "mount",
            "Mount",
            [new StoredItem(57, 1_000_000, -1), new StoredItem(StriderBugle, 1, -1, 55), new StoredItem(BCrystal, 10, -1)],
            level: 40,
            x: X,
            y: Y,
            z: Z);
        owner.Player.CastingUntilMs = 0;
        Assert.Null(await bench.World.TryUseItemAsync(owner.Session, CollarOf(owner, StriderBugle), default));
        bench.World.ResolvePendingHitForTest(owner.Player);
        return (bench, owner);
    }

    [Fact]
    public async Task Strider_summon_then_ride_unsummons_pet_and_fans_ride_packet()
    {
        var (bench, owner) = await RiderAsync();
        bench.Rec.Calls.Clear();
        Assert.NotNull(bench.World.SummonOfForTest(owner.Player));

        Assert.Null(await bench.World.TryCharacterActionAsync(owner.Session, ActionProtocol.RideMount, 0, default));
        Assert.Null(bench.World.SummonOfForTest(owner.Player));
        Assert.True(owner.Player.IsMounted);
        Assert.Equal(PetMountKind.Strider, owner.Player.MountKind);

        var ride = (RideNetMsg)bench.Rec.Of(nameof(IWorldEventPublisher.FanoutRide)).Last()[1]!;
        Assert.Equal((owner.ObjectId, MountProtocol.RideMount, (byte)PetMountKind.Strider, WindStrider + MountProtocol.RideNpcOffset),
            (ride.ObjectId, ride.Action, ride.MountKind, ride.RideNpcId));

        var state = (PlayerActionStateNetMsg)bench.Rec.Of(nameof(IWorldEventPublisher.FanoutPlayerActionState)).Last()[1]!;
        Assert.True(state.Mounted);
        Assert.False(state.PetOut);
        Assert.False(state.CanRide);
    }

    [Fact]
    public async Task Dismount_via_ride_action_clears_mount_state()
    {
        var (bench, owner) = await RiderAsync();
        Assert.Null(await bench.World.TryCharacterActionAsync(owner.Session, ActionProtocol.RideMount, 0, default));
        bench.Rec.Calls.Clear();

        Assert.Null(await bench.World.TryCharacterActionAsync(owner.Session, ActionProtocol.RideMount, 0, default));
        Assert.False(owner.Player.IsMounted);
        var ride = (RideNetMsg)bench.Rec.Of(nameof(IWorldEventPublisher.FanoutRide)).Last()[1]!;
        Assert.Equal(MountProtocol.RideDismount, ride.Action);
    }

    [Fact]
    public async Task Wyvern_mount_blocks_attack()
    {
        var (bench, owner) = await RiderAsync();
        Assert.Null(await bench.World.TryCharacterActionAsync(owner.Session, ActionProtocol.RideMount, 0, default));
        var gremlin = bench.World.SpawnMonsterForTests(20001, X + 200, Y, Z);
        Assert.Null(await bench.World.TryCharacterActionAsync(owner.Session, ActionProtocol.Attack, gremlin.ObjectId, default));
        owner.Player.Mount = new MountState
        {
            Kind = PetMountKind.Wyvern,
            NpcId = 12621,
            WyvernRent = true,
            RunSpeed = 170,
            WalkSpeed = 80
        };
        Assert.Null(await bench.World.TryCharacterActionAsync(owner.Session, ActionProtocol.Attack, gremlin.ObjectId, default));
    }
}