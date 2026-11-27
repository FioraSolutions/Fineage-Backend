using Fineage.Protocol;
using Fineage.Zone.NpcHtml;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// #1112: every NpcTalk ends in a page, a reject or a refusal message. A gatekeeper traveller sometimes got none of
/// them and reported "no dialog after 0 page(s)"; these are the server paths that answered with nothing.
/// </summary>
public sealed class NpcTalkReplyTests
{
    const int MerchantObjectId = 61000;
    const int MerchantNpcId = 30001;

    [Fact]
    public async Task A_sitting_talker_out_of_range_is_refused_not_ignored()
    {
        var (bench, actor) = await Open(npcDz: 0, playerDx: 1_000);
        actor.Player.Sitting = true;

        // Mobius CreatureAI.setIntentionInteract: REST → ActionFailed, no INTERACT intention.
        Assert.Equal(ActionProtocol.Sitting, await actor.Talk(MerchantObjectId));
        Assert.Equal(0, actor.Player.Intent.PendingTalkNpcId);
        Assert.Empty(bench.Rec.Of(FanoutHtml));
    }

    [Fact]
    public async Task A_casting_talker_out_of_range_is_refused_not_ignored()
    {
        var (bench, actor) = await Open(npcDz: 0, playerDx: 1_000);
        actor.Player.CastingUntilMs = long.MaxValue;

        // Mobius setIntentionInteract while casting → ActionFailed; the interact target is not saved for after the cast.
        Assert.Equal(NpcHtmlProtocol.Busy, await actor.Talk(MerchantObjectId));
        Assert.Equal(0, actor.Player.Intent.PendingTalkNpcId);
        Assert.Empty(bench.Rec.Of(FanoutHtml));
    }

    [Fact]
    public async Task Standing_under_a_ledge_the_sphere_cannot_reach_is_a_range_refusal()
    {
        // The pawn already stands at the approach stop (right under the npc) and the npc is 300 up: no walk can help.
        var (bench, actor) = await Open(npcDz: 300, playerDx: 0);

        Assert.Equal("range", await actor.Talk(MerchantObjectId));
        Assert.Equal(0, actor.Player.Intent.PendingTalkNpcId);
        Assert.Empty(bench.Rec.Of(FanoutHtml));
    }

    [Fact]
    public async Task A_talk_that_arrives_out_of_the_sphere_answers_with_the_action_failed_message()
    {
        var clock = new MutableClock { Now = ManualSimulationDriver.FixedStartUtc };
        var (bench, actor) = await Open(npcDz: 300, playerDx: 150, clock);
        var driver = new ManualSimulationDriver(bench.World, clock);

        // 150 away along the ground: the talk starts AI_INTENTION_INTERACT and walks in.
        Assert.Null(await actor.Talk(MerchantObjectId));
        Assert.Equal(MerchantObjectId, actor.Player.Intent.PendingTalkNpcId);

        driver.Advance(TimeSpan.FromSeconds(3));
        bench.World.WaitForFanoutIdle();

        // The walk ends under the ledge, still 300 below the npc. The talk that opens on arrival used to drop its
        // "range" result, so the client waited for a dialog that never came.
        Assert.Equal(0, actor.Player.Intent.PendingTalkNpcId);
        Assert.Empty(bench.Rec.Of(FanoutHtml));
        Assert.Contains(bench.Rec.Of(FanoutSystemMessage),
            m => ((SystemMessageMsg)m[1]!).Id == ZoneWorld.SysMsgTargetOutOfRange);
    }

    [Fact]
    public async Task A_talk_that_walks_into_range_opens_the_page()
    {
        var clock = new MutableClock { Now = ManualSimulationDriver.FixedStartUtc };
        var (bench, actor) = await Open(npcDz: 0, playerDx: 400, clock);
        var driver = new ManualSimulationDriver(bench.World, clock);

        Assert.Null(await actor.Talk(MerchantObjectId));
        driver.Advance(TimeSpan.FromSeconds(3));
        bench.World.WaitForFanoutIdle();

        Assert.NotEmpty(bench.Rec.Of(FanoutHtml));
        Assert.DoesNotContain(bench.Rec.Of(FanoutSystemMessage),
            m => ((SystemMessageMsg)m[1]!).Id == ZoneWorld.SysMsgTargetOutOfRange);
    }

    const string FanoutHtml = "FanoutNpcHtml";
    const string FanoutSystemMessage = "FanoutSystemMessage";

    static async Task<(WorldBench Bench, BenchPlayer Actor)> Open(int npcDz, int playerDx, MutableClock? clock = null)
    {
        var catalog = NpcHtmlCatalog.FromDocuments(new Dictionary<string, string>
        {
            ["merchant/30001.htm"] = "<html><body>%npcname% %objectId%</body></html>"
        });
        var npc = new SpawnNpcDefinition(
            MerchantObjectId, MerchantNpcId, "Lector", Spawns.HumanStartX, Spawns.HumanStartY, Spawns.HumanStartZ + npcDz,
            0, false, 100, 80f, 40f, 60, 0, "Trader", "Merchant");
        var spawns = SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
        {
            [SpawnCatalog.DefaultMapId] = new(SpawnCatalog.DefaultMapId, true, [npc])
        });
        var bench = new WorldBench(
            spawns: spawns,
            html: catalog,
            bypasses: BypassRegistry.CreateDefault(catalog),
            clock: clock == null ? null : () => clock.Now);
        var actor = await bench.EnterAsync("talker", "Talker",
            x: Spawns.HumanStartX + playerDx, y: Spawns.HumanStartY, z: Spawns.HumanStartZ);
        return (bench, actor);
    }
}
