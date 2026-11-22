using Fineage.Accounts;
using Fineage.Game;
using Fineage.Game.Economy;
using Fineage.Login;
using Fineage.Protocol;
using Fineage.Zone.Interest;
using Fineage.Zone.Siege;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Mobius <c>CastleManager.removeCirclet</c>: circlets and the lord crown leave every member's bag, including offline.</summary>
[Trait("Category", "Integration")]
public sealed class NetSiegeOfflineCircletTests
{
    const int Gludio = 1;

    [Fact]
    public void Remove_circlets_strips_offline_member_inventory_on_the_ledger()
    {
        int circlet = SiegeProtocol.CircletItemIds[Gludio];
        var store = MemoryAccountStore.InMemory();
        var auth = new LoginService(store, new HandoffTokens(HandoffTokens.DefaultSecret));
        Assert.True(auth.Register("leader", "secret1").Ok);
        Assert.True(auth.Register("offline", "secret2").Ok);
        Assert.Equal(CreateCharacterStatus.Ok, store.TryAddCharacter("leader", "Leader", HumanClasses.Fighter, 0, 0, out var leader, level: 40));
        Assert.Equal(CreateCharacterStatus.Ok, store.TryAddCharacter("offline", "Away", HumanClasses.Fighter, 0, 0, out var away, level: 20));
        var clan = store.InsertClan(
            new ClanRecord(0, "Defenders", 4, leader!.Id, Gludio, 0, 0,
            [
                new ClanMemberRecord(leader.Id, "leader", "Leader", HumanClasses.Fighter, 40, 0),
                new ClanMemberRecord(away!.Id, "offline", "Away", HumanClasses.Fighter, 20, 0)
            ]),
            new CharacterClanState("leader", leader.Id, 0, 0, 0));
        Assert.NotNull(clan);
        Assert.True(store.TryReplaceItems("offline", away.Id,
            [new StoredItem(circlet, 1, -1, 0, 42_001), new StoredItem(PledgeProtocol.AdenaItemId, 100, -1)]));
        Assert.True(store.TryReplaceItems("leader", leader.Id,
            [new StoredItem(SiegeProtocol.LordsCrownItemId, 1, -1, 0, 42_002)]));

        var ledger = new MemoryLedgerJournal();
        var world = new ZoneWorld(
            new RangeAoi(1_000),
            TestCatalogs.Game,
            accounts: store,
            clock: () => DateTimeOffset.FromUnixTimeMilliseconds(1_800_000_000_000),
            siege: SiegeSettings.Mobius,
            ledgerJournal: ledger);
        Assert.True(world.ReloadClansForTests());
        world.RemoveCircletsForTest(clan!.Id, Gludio);

        var offlineBag = store.FindById("offline", away.Id);
        Assert.NotNull(offlineBag);
        Assert.DoesNotContain(offlineBag!.Items, row => row.ItemId == circlet);
        Assert.Contains(offlineBag.Items, row => row.ItemId == PledgeProtocol.AdenaItemId);

        var leaderBag = store.FindById("leader", leader.Id);
        Assert.NotNull(leaderBag);
        Assert.DoesNotContain(leaderBag!.Items, row => row.ItemId == SiegeProtocol.LordsCrownItemId);

        var offlineLedger = LedgerAccount.Inventory(away.Id);
        var destroy = Assert.Single(ledger.Entries
            .Select(e => e.Transaction)
, t => t.Kind == LedgerTxKind.Destroy && t.CausedBy == away.Id);
        Assert.Equal(Gludio, destroy.Reference);
        Assert.Contains(destroy.Postings, p => p.Account == offlineLedger && p.ItemId == circlet && p.Amount < 0);
    }
}