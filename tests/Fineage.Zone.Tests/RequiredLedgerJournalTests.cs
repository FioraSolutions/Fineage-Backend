using Fineage.Accounts;
using Fineage.Game.Economy;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class RequiredLedgerJournalTests
{
    [Fact]
    public void World_with_accounts_requires_explicit_journal()
    {
        var store = MemoryAccountStore.InMemory();
        var error = Assert.Throws<InvalidOperationException>(() => new ZoneWorld(accounts: store));
        Assert.Contains("ILedgerJournal", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void World_without_accounts_still_defaults_memory_journal_for_unit_tests()
    {
        var world = new ZoneWorld();
        Assert.Equal(LedgerChain.Genesis, world.LedgerForTests.Head);
    }
}