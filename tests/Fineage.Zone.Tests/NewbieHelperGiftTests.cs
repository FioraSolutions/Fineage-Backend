using Fineage.Game.Services;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Spawns;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// The talking-island helper hands a new character its kit once, on the first talk. Ported off the V2 wire; the gift
/// and the page are read from the recorded Net fanout.
/// </summary>
public sealed class NewbieHelperGiftTests
{
    const int GuideNpcId = 30598;

    [Fact]
    public async Task Talking_island_guide_gives_soothers_shots_and_tokens_once()
    {
        var guide = Assert.Single(
            SpawnCatalog.LoadDefault().GetMap(SpawnCatalog.TalkingIslandVillageMapId).Npcs,
            row => row.NpcId == GuideNpcId);
        var bench = Open();
        // Start on top of the guide: talk needs the 150 unit radius, and the village is far from the default spawn.
        BenchPlayer actor = await bench.EnterAsync("gift", "Gifted", level: 5, x: guide.X, y: guide.Y, z: guide.Z);

        Assert.Null(await actor.Talk(guide.ObjectId));

        var inventory = Assert.IsType<InventoryUpdateNetMsg>(
            bench.Rec.Of(nameof(FanoutInventoryUpdate)).Last()[1]);
        Assert.Contains(inventory.Changes, row => row.ItemId == NewbieHelperGift.SoulshotNovice && row.Count == NewbieHelperGift.SoulshotCount);
        Assert.Contains(inventory.Changes, row => row.ItemId == NewbieHelperGift.TravelToken && row.Count == NewbieHelperGift.TokenCount);
        Assert.Contains(inventory.Changes, row => row.ItemId == NewbieHelperGift.ScrollRecovery && row.Count == NewbieHelperGift.ScrollCount);

        Assert.Contains("SupportMagic.htm", LastHtml(bench), StringComparison.Ordinal);

        // Second talk: the page again, but no second kit.
        bench.Rec.Calls.Clear();
        Assert.Null(await actor.Talk(guide.ObjectId));
        Assert.Empty(bench.Rec.Of(nameof(FanoutInventoryUpdate)));
        Assert.Contains("SupportMagic.htm", LastHtml(bench), StringComparison.Ordinal);
    }

    static WorldBench Open()
    {
        var village = SpawnCatalog.LoadDefault().GetMap(SpawnCatalog.TalkingIslandVillageMapId);
        return new WorldBench(
            spawns: SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
            {
                [SpawnCatalog.TalkingIslandVillageMapId] = village
            }),
            mapId: SpawnCatalog.TalkingIslandVillageMapId);
    }

    static string LastHtml(WorldBench bench) =>
        Assert.IsType<NpcHtmlNetMsg>(bench.Rec.Of(nameof(FanoutNpcHtml)).Last()[1]).Html;

    const string FanoutNpcHtml = "FanoutNpcHtml";
    const string FanoutInventoryUpdate = "FanoutInventoryUpdate";
}
