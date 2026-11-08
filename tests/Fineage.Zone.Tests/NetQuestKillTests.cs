using Fineage.Game.Quests;
using Fineage.Net.Contract;
using Fineage.Zone.Net;
using Fineage.Zone.World;
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

/// <summary>Mobius getRandomPartyMemberState: Q316 kill credit goes to one eligible party member within ALT_PARTY_RANGE.</summary>
public sealed class NetQuestPartyKillTests
{
    static void Start316(Fineage.Zone.World.ZoneEntity p) =>
        p.Quests.Add(316, QuestPhase.Started).Replace(QuestPhase.Started, new Dictionary<string, string> { ["cond"] = "1" });

    [Fact]
    public async Task Party_member_with_the_quest_gets_the_fang_when_killer_has_none()
    {
        var kit = new NetTestKit();
        var (_, killer) = await kit.EnterAsync();
        var (_, mate) = await kit.EnterAsync();
        mate.X = killer.X + 500; mate.Y = killer.Y;
        kit.World.JoinPartyForTest(killer, mate);
        Start316(mate);

        kit.World.QuestKillForTest(killer, 20040);
        // Mobius giveItemRandomly count=1 (chance 10/5 ≈ always); older pack wrongly dropped 2.
        Assert.Equal(1, mate.Inventory!.CountOf(1042));
        Assert.Equal(0, killer.Inventory!.CountOf(1042));
    }

    [Fact]
    public async Task Out_of_range_or_not_in_party_gets_nothing()
    {
        var kit = new NetTestKit();
        var (_, killer) = await kit.EnterAsync();
        var (_, far) = await kit.EnterAsync();
        var (_, stranger) = await kit.EnterAsync();
        far.X = killer.X + Fineage.Zone.World.ZoneWorld.QuestPartyRange + 10; far.Y = killer.Y;
        kit.World.JoinPartyForTest(killer, far);
        Start316(far);
        Start316(stranger);

        kit.World.QuestKillForTest(killer, 20040);
        Assert.Equal(0, far.Inventory!.CountOf(1042));
        Assert.Equal(0, stranger.Inventory!.CountOf(1042));
    }

    [Fact]
    public async Task Varool_fang_skips_members_who_already_hold_one()
    {
        var kit = new NetTestKit();
        var (_, killer) = await kit.EnterAsync();
        var (_, mate) = await kit.EnterAsync();
        mate.X = killer.X; mate.Y = killer.Y;
        kit.World.JoinPartyForTest(killer, mate);
        Start316(killer);
        Start316(mate);

        kit.World.QuestKillForTest(killer, 27020);
        kit.World.QuestKillForTest(killer, 27020);
        Assert.Equal(1, killer.Inventory!.CountOf(1043));
        Assert.Equal(1, mate.Inventory!.CountOf(1043));
        kit.World.QuestKillForTest(killer, 27020);
        Assert.Equal(1, killer.Inventory!.CountOf(1043));
        Assert.Equal(1, mate.Inventory!.CountOf(1043));
    }
}
