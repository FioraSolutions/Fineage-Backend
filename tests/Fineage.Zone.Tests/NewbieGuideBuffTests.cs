using Fineage.Game;
using Fineage.Accounts;
using Fineage.Net.Contract;
using Fineage.Zone.NpcHtml;
using Fineage.Zone.Spawns;
using Xunit;
using Fineage.Protocol;

namespace Fineage.Zone.Tests;

/// <summary>
/// The talking-island newbie guide's support magic: the pages it links to, and the buff set it casts on a fighter.
/// Ported off the V2 wire; the casts and effects are read from the recorded Net fanout.
/// </summary>
public sealed class NewbieGuideBuffTests
{
    const int GuideNpcId = 30598;
    // Fighter support set: Might, Shield, and the rest of the beginner list.
    const int MightBuff = 4322;
    const int ShieldBuff = 4327;
    const int MageOnlyBuff = 4331;

    [Fact]
    public void Spawned_newbie_guides_offer_support_magic_and_the_training_helper_does_not()
    {
        var html = NpcHtmlCatalog.LoadDefault();
        foreach (int npcId in new[] { 30598, 30599, 30600, 30601, 30602, 31076, 31077 })
        {
            Assert.True(html.TryGet($"default/{npcId}.htm", out var page), $"missing {npcId}.htm");
            Assert.Contains("default/SupportMagic.htm", page, StringComparison.Ordinal);
            Assert.Contains("default/BlessingOfProtection.htm", page, StringComparison.Ordinal);
        }

        Assert.True(html.TryGet("default/30009.htm", out var helper));
        Assert.Contains("Cedric's Training Hall", helper, StringComparison.Ordinal);
        Assert.Contains("bypass Script", helper, StringComparison.Ordinal);
        Assert.DoesNotContain("SupportMagic", helper, StringComparison.Ordinal);
        Assert.True(html.TryGet("default/SupportMagic.htm", out var magic));
        Assert.Contains("npc_%objectId%_SupportMagic", magic, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Talking_island_newbie_guide_applies_the_fighter_set()
    {
        var village = SpawnCatalog.LoadDefault().GetMap(SpawnCatalog.TalkingIslandVillageMapId);
        var guide = Assert.Single(village.Npcs, row => row.NpcId == GuideNpcId);
        var bench = new WorldBench(
            spawns: SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
            {
                [SpawnCatalog.TalkingIslandVillageMapId] = village
            }),
            mapId: SpawnCatalog.TalkingIslandVillageMapId);
        BenchPlayer actor = await bench.EnterAsync(
            "guide", "Rookie",
            classId: HumanClasses.Fighter,
            level: 16,
            x: guide.X, y: guide.Y, z: guide.Z);

        Assert.Null(await actor.Talk(guide.ObjectId));
        Assert.Contains("SupportMagic.htm", LastHtml(bench), StringComparison.Ordinal);
        Assert.Contains($"npc_{guide.ObjectId}_", LastHtml(bench), StringComparison.Ordinal);

        Assert.Null(await actor.BypassAsync(guide.ObjectId, "Link default/SupportMagic.htm"));
        Assert.Contains("Receive supplemental magic.", LastHtml(bench), StringComparison.Ordinal);
        Assert.Contains($"npc_{guide.ObjectId}_SupportMagic", LastHtml(bench), StringComparison.Ordinal);

        bench.Rec.Calls.Clear();
        Assert.Null(await actor.BypassAsync(guide.ObjectId, $"bypass -h npc_{guide.ObjectId}_SupportMagic"));

        var casts = bench.Rec.Of(nameof(FanoutMagicSkillUse))
            .Select(args => ((MagicSkillUseNetMsg)args[1]).SkillId)
            .ToArray();
        var abnormal = Assert.IsType<AbnormalStatusUpdateMsg>(
            bench.Rec.Of(nameof(FanoutAbnormalStatusUpdate)).Last()[1]);
        Assert.Contains(abnormal.Effects, effect => effect.Id == MightBuff && effect.Value == 3 && effect.DurationSeconds == 3600);
        Assert.Contains(abnormal.Effects, effect => effect.Id == ShieldBuff);
        // A fighter must not receive the mage-only buff.
        Assert.DoesNotContain(abnormal.Effects, effect => effect.Id == MageOnlyBuff);

        Assert.Contains(MightBuff, casts);
        Assert.Contains(ShieldBuff, casts);
    }

    static string LastHtml(WorldBench bench) =>
        Assert.IsType<NpcHtmlMsg>(bench.Rec.Of(nameof(FanoutNpcHtml)).Last()[1]).Html;

    const string FanoutNpcHtml = "FanoutNpcHtml";
    const string FanoutMagicSkillUse = "FanoutMagicSkillUse";
    const string FanoutAbnormalStatusUpdate = "FanoutAbnormalStatusUpdate";
}
