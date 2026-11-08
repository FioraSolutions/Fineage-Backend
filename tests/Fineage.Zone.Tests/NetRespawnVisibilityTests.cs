using Fineage.Zone.Net;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>A respawned mob inside view must reach Net clients right away (EntitySpawn via knownlist diff).</summary>
public sealed class NetRespawnVisibilityTests
{
    [Fact]
    public async Task Respawned_npc_in_view_is_pushed_to_net_client()
    {
        var kit = new NetTestKit();
        var (_, player) = await kit.EnterAsync();
        int npcId = player.Knownlist.First(id => kit.World.TryGetEntityById(id, out var e) && e.Kind == ZoneEntityKind.Npc && e.Attackable);
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;

        kit.World.DespawnNpcForTests(npcId);
        Assert.DoesNotContain(npcId, player.Knownlist);
        rec.Calls.Clear();

        kit.Now = kit.Now.AddHours(2);
        kit.World.AdvanceSpawnsForTests();

        Assert.Contains(npcId, player.Knownlist);
        Assert.Contains(rec.Of(nameof(IWorldEventPublisher.FanoutKnownlistDiff)),
            a => (int)a[0]! == player.ObjectId && ((IReadOnlyList<int>)a[1]!).Contains(npcId));
    }
}
