using Fineage.Protocol;
using Fineage.Zone.Olympiad;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Mobius <c>RequestWriteHeroWords</c> — claimed heroes only (#932).</summary>
public sealed class HeroWordsTests
{
    static readonly DateTimeOffset Evening =
        new(2026, 3, 20, 18, 0, 0, TimeSpan.Zero);

    const int Duelist = 88;

    [Fact]
    public async Task Hero_can_write_inscription_and_it_persists_on_list()
    {
        var store = new MemoryOlympiadStore();
        var board = new OlympiadBoard(Evening, new OlympiadSettings(), store);
        var kit = new WorldBench(clock: () => Evening, olympiad: board);
        BenchPlayer h = await kit.EnterAsync("hero", "Hero", classId: Duelist, level: 78);

        var snap = store.Load();
        snap.Heroes =
        [
            new OlympiadHeroRow
            {
                CharacterId = h.ObjectId,
                ClassId = Duelist,
                Name = "Hero",
                Claimed = true,
                Count = 1
            }
        ];
        store.Save(snap);
        board = new OlympiadBoard(Evening, new OlympiadSettings(), store);
        kit = new WorldBench(clock: () => Evening, olympiad: board);
        h = await kit.EnterAsync("hero", "Hero", classId: Duelist, level: 78);
        h.Player.Hero = true;

        Assert.Null(await kit.World.TryWriteHeroWordsAsync(h.Session, "For the glory of Aden", CancellationToken.None));
        Assert.Equal("For the glory of Aden", kit.World.Olympiad.HeroMessage(h.ObjectId));

        // Reloaded board keeps the inscription.
        var again = new OlympiadBoard(Evening, new OlympiadSettings(), store);
        Assert.Equal("For the glory of Aden", again.HeroMessage(h.ObjectId));

        var manager = kit.World.SpawnFolkForTests(31688, h.Player.X + 20, h.Player.Y, h.Player.Z);
        Assert.Null(await h.BypassAsync(manager.ObjectId, "Olympiad 4"));
        string html = kit.Rec.Of(nameof(IWorldEventPublisher.FanoutNpcHtml))
            .Select(a => Assert.IsType<NpcHtmlMsg>(a[1]).Html)
            .Last();
        Assert.Contains("For the glory of Aden", html, StringComparison.Ordinal);
        Assert.Contains("Hero", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Non_hero_is_rejected_and_character_id_is_logged()
    {
        var kit = new WorldBench(clock: () => Evening);
        BenchPlayer p = await kit.EnterAsync("mortal", "Mortal", classId: Duelist, level: 78);
        Assert.False(p.Player.Hero);

        Assert.Equal("forbidden", await kit.World.TryWriteHeroWordsAsync(p.Session, "nope", CancellationToken.None));
        Assert.Equal("", kit.World.Olympiad.HeroMessage(p.ObjectId));
    }

    [Fact]
    public async Task Oversized_inscription_is_bad_request()
    {
        var store = new MemoryOlympiadStore();
        var board = new OlympiadBoard(Evening, new OlympiadSettings(), store);
        var kit = new WorldBench(clock: () => Evening, olympiad: board);
        BenchPlayer h = await kit.EnterAsync("hero2", "HeroTwo", classId: Duelist, level: 78);
        var snap = store.Load();
        snap.Heroes =
        [
            new OlympiadHeroRow
            {
                CharacterId = h.ObjectId,
                ClassId = Duelist,
                Name = "HeroTwo",
                Claimed = true
            }
        ];
        store.Save(snap);
        board = new OlympiadBoard(Evening, new OlympiadSettings(), store);
        kit = new WorldBench(clock: () => Evening, olympiad: board);
        h = await kit.EnterAsync("hero2", "HeroTwo", classId: Duelist, level: 78);
        h.Player.Hero = true;

        string tooLong = new('x', OlympiadProtocol.HeroMessageMaxLength + 1);
        Assert.Equal("bad_request", await kit.World.TryWriteHeroWordsAsync(h.Session, tooLong, CancellationToken.None));
    }
}
