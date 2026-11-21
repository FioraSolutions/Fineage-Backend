using Fineage.Accounts;
using Fineage.Game.Economy;
using Fineage.Game.L10n;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class PlayerLanguageTests
{
    [Fact]
    public void Enter_uses_the_account_language_when_the_client_omits_the_tail()
    {
        var store = MemoryAccountStore.InMemory();
        Assert.Equal(AddAccountStatus.Created, store.TryAddAccount("alice", "h", "s", 1, null));
        Assert.True(store.TrySetLanguage("alice", "ru"));

        var world = new ZoneWorld(accounts: store, ledgerJournal: new MemoryLedgerJournal());
        var session = new ZoneSession();
        session.Bind(1, "alice", "Aria", 0);

        world.ApplyPlayerLanguage(session, sent: null);
        Assert.Equal("ru", session.Language);

        world.ApplyPlayerLanguage(session, "de");
        Assert.Equal("de", session.Language);
        Assert.Equal("de", store.GetLanguage("alice"));

        world.ApplyPlayerLanguage(session, "not a tag");
        Assert.Equal(PlayerLocale.Default, session.Language);
        Assert.Equal(PlayerLocale.Default, store.GetLanguage("alice"));
    }
}
