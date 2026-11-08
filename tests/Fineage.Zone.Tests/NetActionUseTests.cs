using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Net;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Mobius <c>RequestActionUse</c> over Net: 0 = sit/stand (<c>ChangeWaitType</c>), 12–14 = a social gesture
/// (<c>SocialAction</c>). Both are broadcast to self and every peer holding the actor in view.
/// </summary>
public sealed class NetActionUseTests
{
    static async Task<NetTestKit> KitWithBridgeAsync()
    {
        var kit = new NetTestKit();
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        return kit;
    }

    [Fact]
    public async Task Sit_fans_ChangeWaitType_to_self()
    {
        var kit = await KitWithBridgeAsync();
        var rec = (NetBridgeRecorder)(object)kit.World.NetBridge!;
        var (session, elf) = await kit.EnterAsync();

        Assert.Null(await kit.World.TryCharacterActionAsync(session, ActionProtocol.SitStand, 0, default));
        Assert.True(elf.Sitting);

        var waits = rec.Of(nameof(IWorldEventPublisher.FanoutChangeWaitType));
        var call = Assert.Single(waits);
        var watchers = Assert.IsAssignableFrom<IReadOnlyList<int>>(call[0]);
        Assert.Contains(elf.ObjectId, watchers);
        var msg = Assert.IsType<ChangeWaitTypeNetMsg>(call[1]);
        Assert.Equal(ActionProtocol.WaitSitting, msg.WaitType);
        Assert.Equal(elf.ObjectId, (int)msg.EntityValue);
    }

    [Fact]
    public async Task Stand_again_toggles_back_and_keeps_the_position()
    {
        var kit = await KitWithBridgeAsync();
        var rec = (NetBridgeRecorder)(object)kit.World.NetBridge!;
        var (session, elf) = await kit.EnterAsync();

        await kit.World.TryCharacterActionAsync(session, ActionProtocol.SitStand, 0, default);
        await kit.World.TryCharacterActionAsync(session, ActionProtocol.SitStand, 0, default);

        Assert.False(elf.Sitting);
        var waits = rec.Of(nameof(IWorldEventPublisher.FanoutChangeWaitType));
        Assert.Equal(2, waits.Count);
        var standing = Assert.IsType<ChangeWaitTypeNetMsg>(waits[1][1]);
        Assert.Equal(ActionProtocol.WaitStanding, standing.WaitType);
        Assert.Equal(elf.X, standing.X);
        Assert.Equal(elf.Y, standing.Y);
        Assert.Equal(elf.Z, standing.Z);
    }

    [Fact]
    public async Task Social_gesture_fans_SocialAction()
    {
        var kit = await KitWithBridgeAsync();
        var rec = (NetBridgeRecorder)(object)kit.World.NetBridge!;
        var (session, elf) = await kit.EnterAsync();

        Assert.Null(await kit.World.TryCharacterActionAsync(session, ActionProtocol.SocialVictory, 0, default));

        var call = Assert.Single(rec.Of(nameof(IWorldEventPublisher.FanoutSocialAction)));
        Assert.Equal(elf.ObjectId, (int)call[1]!);
        Assert.Equal(ActionProtocol.SocialVictory, (int)call[2]!);
    }

    [Fact]
    public async Task Walk_run_toggle_fans_ChangeMoveType_to_self_and_peers()
    {
        var kit = await KitWithBridgeAsync();
        var rec = (NetBridgeRecorder)(object)kit.World.NetBridge!;
        var (session, elf) = await kit.EnterAsync();
        Assert.True(elf.Running);

        Assert.Null(await kit.World.TryCharacterActionAsync(session, ActionProtocol.WalkRun, 0, default));

        Assert.False(elf.Running);
        var call = Assert.Single(rec.Of(nameof(IWorldEventPublisher.FanoutChangeMoveType)));
        var watchers = Assert.IsAssignableFrom<IReadOnlyList<int>>(call[0]);
        Assert.Contains(elf.ObjectId, watchers);
        Assert.Equal(elf.ObjectId, (int)call[1]!);
        Assert.False((bool)call[2]!);
    }
}
