using Fineage.Accounts;
using Fineage.Game;
using Fineage.Game.Economy;
using Fineage.Zone.Drops;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class ItemObjectIdAllocatorBootTests
{
    const int Harvester = 5125;
    const int OwnedInstance = 100_000;

    [Fact]
    public void World_without_accounts_fails_when_the_journal_already_owns_instances()
    {
        var journal = SeedJournalWithInstance(OwnedInstance);
        var error = Assert.Throws<InvalidOperationException>(() => new ZoneWorld(ledgerJournal: journal));
        Assert.Contains("IAccountStore.NextItemObjectId", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Non_stackable_loot_uses_an_id_above_journal_owned_instances()
    {
        var journal = SeedJournalWithInstance(OwnedInstance);
        var store = MemoryAccountStore.InMemory();
        Assert.Equal(AddAccountStatus.Created, store.TryAddAccount("boot", "h", "s", 1, null));
        Assert.Equal(CreateCharacterStatus.Ok,
            store.TryAddCharacter("boot", "Killer", classId: 0, race: 0, sex: 0, out _, level: 1));

        var world = new ZoneWorld(
            accounts: store,
            ledgerJournal: journal,
            spawnRandom: new Random(MobiusDropTestSeed.DefaultSeed),
            dropRandom: new Random(MobiusDropTestSeed.DefaultSeed));
        Assert.True(OwnedInstance < world.NextItemObjectIdForTests);
        Assert.Equal(OwnedInstance, world.LedgerForTests.MaxInstanceId);
        Assert.Equal(LedgerAccount.Inventory(99), world.LedgerForTests.OwnerOf(OwnedInstance));

        var session = new ZoneSession();
        session.Bind(1, "boot", "Killer", 0);
        Assert.True(await world.TryEnterAsync(session, CancellationToken.None));
        Assert.True(world.TryGetLivePlayer(session, out var killer));

        world.DropDeathLootForTest(MobiusDropTestSeed.TrainingHallGremlinNpcId, killer.X, killer.Y, killer.Z, killer);

        var lootTx = journal.Entries.Select(e => e.Transaction).Last(tx => tx.Kind == LedgerTxKind.Loot);
        var groundPost = Assert.Single(lootTx.Postings, p => p.Account.Container == LedgerContainer.Ground);
        Assert.True(groundPost.Account.OwnerId > OwnedInstance);
        Assert.DoesNotContain(OwnedInstance, groundPost.InstanceIds);
    }

    static MemoryLedgerJournal SeedJournalWithInstance(int instanceId)
    {
        var journal = new MemoryLedgerJournal();
        var ledger = new EconomyLedger(journal, 100);
        var move = new LedgerMove(ledger, () => new LedgerTxId(1, 0), LedgerTxKind.QuestReward, 0, 99, 1);
        var bag = LedgerAccount.Inventory(99);
        var quests = LedgerAccount.Of(SystemAccount.Quests);
        var empty = new LedgerHoldings();
        var after = new LedgerHoldings().AddPiece(Harvester, instanceId);
        move.Observe(bag, empty);
        ZoneWorld.PostChange(move, bag, empty, after, quests);
        Assert.True(move.Commit(a => a.Equals(bag) ? after : new LedgerHoldings()).Ok);
        return journal;
    }
}