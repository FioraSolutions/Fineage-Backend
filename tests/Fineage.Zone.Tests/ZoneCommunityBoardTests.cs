using Fineage.Accounts;
using Fineage.Game;
using Fineage.Game.Economy;
using Fineage.Login;
using Fineage.Protocol;
using Fineage.Zone.CommunityBoard;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Mobius community board: open / read / write through one service (#929).</summary>
public sealed class ZoneCommunityBoardTests
{
    static async Task<(ZoneWorld World, ZoneSession Session, ZoneEntity Player)> Enter(
        MemoryAccountStore store, int characterId, string account, string name,
        CommunityBoardCatalog? board = null)
    {
        var world = new ZoneWorld(
            accounts: store,
            ledgerJournal: new MemoryLedgerJournal(),
            communityBoard: board);
        var session = new ZoneSession();
        session.Bind(characterId, account, name, HumanClasses.Fighter);
        Assert.True(await world.TryEnterAsync(session, CancellationToken.None));
        Assert.True(world.TryGetLivePlayer(session, out var player));
        return (world, session, player);
    }

    static (MemoryAccountStore Store, LoginService Login) NewStore()
    {
        var store = MemoryAccountStore.InMemory();
        var login = new LoginService(store, new HandoffTokens(HandoffTokens.DefaultSecret));
        Assert.True(login.Register("bbsboard", "pass1234").Ok);
        return (store, login);
    }

    static CommunityBoardCatalog Pages(params (string Key, string Html)[] rows) =>
        CommunityBoardCatalog.FromDocuments(rows.ToDictionary(r => r.Key, r => r.Html));

    [Fact]
    public async Task ShowBoard_sends_home_through_one_service()
    {
        var (store, login) = NewStore();
        var a = login.CreateHuman("bbsboard", "Boarder", HumanClasses.Fighter).Value!;
        var catalog = Pages(
            ("home.html", "<html><body>HOME %fav_count% %region_count% %clan_count%</body></html>"));
        var (world, session, _) = await Enter(store, a.Id, "bbsboard", "Boarder", catalog);

        Assert.Null(await world.TryShowBoardAsync(session, CancellationToken.None));
        Assert.Contains(world.CommunityBoard.LastSentContents,
            c => c.StartsWith("101\u0008", StringComparison.Ordinal) && c.Contains("HOME 0 9 0", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Bypass_reads_memo_page()
    {
        var (store, login) = NewStore();
        var a = login.CreateHuman("bbsboard", "Reader", HumanClasses.Fighter).Value!;
        var catalog = Pages(("memo.html", "<html><body>MEMO</body></html>"));
        var (world, session, _) = await Enter(store, a.Id, "bbsboard", "Reader", catalog);

        Assert.Null(await world.TryBypassAsync(session, npcObjectId: 0, "_bbsmemo", CancellationToken.None));
        Assert.Contains(world.CommunityBoard.LastSentContents,
            c => c.Contains("MEMO", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Missing_page_sends_nothing()
    {
        var (store, login) = NewStore();
        var a = login.CreateHuman("bbsboard", "Misser", HumanClasses.Fighter).Value!;
        var world = new ZoneWorld(
            accounts: store,
            ledgerJournal: new MemoryLedgerJournal(),
            communityBoard: Pages(("other.html", "x")));
        var session = new ZoneSession();
        session.Bind(a.Id, "bbsboard", "Misser", HumanClasses.Fighter);
        Assert.True(await world.TryEnterAsync(session, CancellationToken.None));

        world.CommunityBoard.LastSentContents.Clear();
        Assert.Null(await world.TryShowBoardAsync(session, CancellationToken.None));
        Assert.Empty(world.CommunityBoard.LastSentContents);
    }

    [Fact]
    public async Task Write_goes_through_board_service()
    {
        var (store, login) = NewStore();
        var a = login.CreateHuman("bbsboard", "Writer", HumanClasses.Fighter).Value!;
        var (world, session, _) = await Enter(store, a.Id, "bbsboard", "Writer", Pages());

        Assert.Null(await world.TryBBSwriteAsync(session, new BBSwriteMsg
        {
            Url = "Topic",
            Arg1 = "t",
            Arg2 = "b",
            Arg3 = "",
            Arg4 = "",
            Arg5 = ""
        }, CancellationToken.None));

        // Unknown URL still answers through the same service (implemented HTML stub).
        world.CommunityBoard.LastSentContents.Clear();
        Assert.Null(await world.TryBBSwriteAsync(session, new BBSwriteMsg { Url = "Weird" }, CancellationToken.None));
        Assert.Contains(world.CommunityBoard.LastSentContents,
            c => c.Contains("not implemented yet", StringComparison.Ordinal));
    }

    [Fact]
    public void Service_missing_page_warns_with_page_name()
    {
        var warnings = new List<string>();
        var sent = new List<ShowBoardMsg>();
        var service = new CommunityBoardService(
            Pages(("memo.html", "ok")),
            sent.Add,
            warnings.Add);

        service.Parse(1, "A", "_bbshome", clanCount: 0);
        Assert.Empty(sent);
        Assert.Contains(warnings, w => w.Contains("missing page=home.html", StringComparison.Ordinal));
    }
}
