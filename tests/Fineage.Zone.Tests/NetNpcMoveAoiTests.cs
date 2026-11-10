using Fineage.Zone.Net;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>A walking/chasing NPC revalidates who sees it: Net EntityDespawn when it leaves view, EntitySpawn when it enters.</summary>
public sealed class NetNpcMoveAoiTests
{
    static void Arrive(NetTestKit kit, ZoneEntity npc, int x, int y)
    {
        npc.X = x; npc.Y = y;
        npc.MoveDestX = x; npc.MoveDestY = y; npc.MoveDestZ = npc.Z;
        kit.World.SetMoveActive(npc, true);
        npc.MoveLastTickMs = kit.Now.ToUnixTimeMilliseconds();
        kit.Now = kit.Now.AddMilliseconds(100);
    }

    static bool Diff(NetBridgeRecorder rec, int self, int id, bool added) =>
        rec.Of(nameof(IWorldEventPublisher.FanoutKnownlistDiff)).Any(a =>
            (int)a[0]! == self && ((IReadOnlyList<int>)a[added ? 1 : 2]!).Contains(id));

    [Fact]
    public async Task Moving_npc_leaves_and_reenters_net_view()
    {
        var kit = new NetTestKit(aoi: new Fineage.Zone.Interest.GridAoi());
        var (_, player) = await kit.EnterAsync();
        int npcId = 50001;
        Assert.True(kit.World.TryGetEntityById(npcId, out var npc));
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;

        Arrive(kit, npc, player.X + 30_000, player.Y);
        await kit.World.RunPlayerCombatTickAsync(CancellationToken.None);
        Assert.DoesNotContain(npcId, player.Knownlist);

        rec.Calls.Clear();
        Arrive(kit, npc, player.X + 200, player.Y);
        await kit.World.RunPlayerCombatTickAsync(CancellationToken.None);
        Assert.Contains(npcId, player.Knownlist);
        Assert.True(Diff(rec, player.ObjectId, npcId, added: true));

        rec.Calls.Clear();
        Arrive(kit, npc, player.X + 30_000, player.Y);
        await kit.World.RunPlayerCombatTickAsync(CancellationToken.None);
        Assert.DoesNotContain(npcId, player.Knownlist);
        Assert.True(Diff(rec, player.ObjectId, npcId, added: false));
    }
}
