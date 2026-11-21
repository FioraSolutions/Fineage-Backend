using Fineage.Accounts;
using Fineage.Game.Economy;
using Fineage.Login;
using Fineage.Protocol;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>P1-5e: starter kit and GM grant ledger postings.</summary>
public sealed class GrantLedgerTests
{
    const int Adena = 57;
    const int StarterWeapon = 219;
    static readonly LedgerAccount StarterKit = LedgerAccount.Of(SystemAccount.StarterKit);
    static readonly LedgerAccount GmGrants = LedgerAccount.Of(SystemAccount.GmGrants);

    [Fact]
    public async Task Legacy_character_starter_kit_posts_from_starter_kit_account()
    {
        var bench = new WorldBench();
        Assert.True(bench.Store.TryAddAccount("grant", "hash", "salt", 1, null) == AddAccountStatus.Created);
        var status = bench.Store.TryAddCharacter("grant", "Fresh", HumanClasses.Fighter, 0, 0, out var created);
        Assert.Equal(CreateCharacterStatus.Ok, status);
        Assert.Null(created!.Items);

        int before = bench.Ledger.Entries.Count;
        var login = new LoginService(bench.Store, new HandoffTokens(HandoffTokens.DefaultSecret), bench.Now);
        var play = login.Select("grant", created.Id);
        Assert.True(play.Ok && play.Value != null, play.Message);
        var session = new ZoneSession();
        session.Bind(play.Value!.CharacterId, play.Value.Account, play.Value.CharacterName, play.Value.ClassId, play.Value.Race, play.Value.Sex);
        Assert.True(await bench.World.TryEnterAsync(session, CancellationToken.None));
        Assert.True(bench.World.TryGetLivePlayer(session, out var live));
        var actor = new BenchPlayer(bench.World, bench.Store, session, live, play.Value);

        var tx = Assert.Single(bench.Ledger.Entries.Skip(before), e => e.Transaction.Kind == LedgerTxKind.StarterKit).Transaction;
        Assert.Equal(HumanClasses.Fighter, tx.Reference);
        var bag = LedgerAccount.Inventory(actor.ObjectId);
        Assert.Contains(tx.Postings, p => p.Account == StarterKit && p.ItemId == StarterWeapon && p.Amount < 0);
        Assert.Contains(tx.Postings, p => p.Account == bag && p.ItemId == StarterWeapon && p.Amount > 0);
    }

    [Fact]
    public async Task Admin_grant_posts_from_gm_grants_account()
    {
        var bench = new WorldBench();
        var player = await bench.EnterAsync(items: [new StoredItem(Adena, 1, -1)]);
        int before = bench.Ledger.Entries.Count;
        Assert.Null(await bench.World.TryGrantItemAsync(player.Session, Adena, 25, CancellationToken.None));
        var tx = Assert.Single(bench.Ledger.Entries.Skip(before), e => e.Transaction.Kind == LedgerTxKind.GmGrant).Transaction;
        Assert.Equal(Adena, tx.Reference);
        var bag = LedgerAccount.Inventory(player.ObjectId);
        Assert.Contains(tx.Postings, p => p.Account == GmGrants && p.ItemId == Adena && p.Amount == -25);
        Assert.Contains(tx.Postings, p => p.Account == bag && p.ItemId == Adena && p.Amount == 25);
    }
}