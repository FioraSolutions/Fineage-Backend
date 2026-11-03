using Fineage.Game.Quests;
using Fineage.Net.Contract;
using Fineage.Zone.Net;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Mobius Quest.onKill reaches the killer on Net: quest drop lands in the bag (InventoryUpdate).</summary>
public sealed class NetQuestKillTests
{
    [Fact]
    public async Task Spore_fungus_kill_drops_spore_sacs_for_collect_spores()
    {
        var kit = new NetTestKit();
        var (_, killer) = await kit.EnterAsync();
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        killer.Quests.Add(313, QuestPhase.Started).Replace(QuestPhase.Started, new Dictionary<string, string> { ["cond"] = "1" });

        for (int i = 0; i < 80; i++)
            kit.World.QuestKillForTest(killer, 20509);

        Assert.Equal(10, killer.Inventory!.CountOf(1118));
        Assert.Equal(2, killer.Quests.Find(313)!.Cond);
        Assert.NotEmpty(rec.Of(nameof(INetPresenceBridge.FanoutInventoryUpdate)));
    }
}
