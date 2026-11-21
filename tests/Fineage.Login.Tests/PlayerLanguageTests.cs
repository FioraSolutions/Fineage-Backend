using Fineage.Accounts;
using Fineage.Game.L10n;
using Xunit;

namespace Fineage.Login.Tests;

public sealed class PlayerLanguageTests
{
    [Fact]
    public void Language_is_stored_on_the_account_and_roundtrips_through_the_snapshot()
    {
        var store = MemoryAccountStore.InMemory();
        Assert.Equal(AddAccountStatus.Created, store.TryAddAccount("alice", "hash", "salt", 1, email: null));
        Assert.Equal(PlayerLocale.Default, store.GetLanguage("alice"));
        Assert.False(store.TrySetLanguage("missing", "ru"));

        Assert.True(store.TrySetLanguage("alice", "RU"));
        Assert.Equal("ru", store.GetLanguage("Alice"));

        var restored = MemoryAccountStore.FromSnapshot(store.ExportSnapshot());
        Assert.Equal("ru", restored.GetLanguage("alice"));
    }

    [Fact]
    public void RememberLanguage_ignores_an_omitted_tail()
    {
        var store = MemoryAccountStore.InMemory();
        var service = new LoginService(store, new HandoffTokens(HandoffTokens.DefaultSecret));
        Assert.True(service.Register("alice", "secret1").Ok);
        service.RememberLanguage("alice", "de");
        service.RememberLanguage("alice", null);
        Assert.Equal("de", store.GetLanguage("alice"));

        service.RememberLanguage("alice", "nope");
        Assert.Equal("en", store.GetLanguage("alice"));
    }

    [Fact]
    public void Fallback_log_names_the_key_and_the_locale()
    {
        string warning = FineageLocalizationLog.FormatFallback("chat.welcome", "ru");
        Assert.Contains("level=Warning", warning, StringComparison.Ordinal);
        Assert.Contains("event=l10n_fallback", warning, StringComparison.Ordinal);
        Assert.Contains("key=chat.welcome", warning, StringComparison.Ordinal);
        Assert.Contains("locale=ru", warning, StringComparison.Ordinal);
        Assert.Contains("fallback=en", warning, StringComparison.Ordinal);

        string error = FineageLocalizationLog.FormatMissing("chat.welcome");
        Assert.Contains("level=Error", error, StringComparison.Ordinal);
        Assert.Contains("event=l10n_missing", error, StringComparison.Ordinal);
        Assert.Contains("key=chat.welcome", error, StringComparison.Ordinal);
        Assert.Contains("locale=en", error, StringComparison.Ordinal);
    }
}
