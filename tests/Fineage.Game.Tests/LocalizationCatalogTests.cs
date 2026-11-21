using Fineage.Game.L10n;
using Xunit;

namespace Fineage.Game.Tests;

public sealed class LocalizationCatalogTests
{
    [Fact]
    public void Get_returns_the_locale_row()
    {
        var log = new ListLocalizationLog();
        var catalog = LocalizationCatalog.FromTables(
            log,
            ("en", new Dictionary<string, string> { ["chat.welcome"] = "Welcome, {0}." }),
            ("ru", new Dictionary<string, string> { ["chat.welcome"] = "Добро пожаловать, {0}." }));

        Assert.Equal("Добро пожаловать, Aria.", catalog.Format("chat.welcome", "RU", "Aria"));
        Assert.Empty(log.Events);
    }

    [Fact]
    public void Get_logs_the_key_and_locale_when_it_falls_back_to_english()
    {
        var log = new ListLocalizationLog();
        var catalog = LocalizationCatalog.FromTables(
            log,
            ("en", new Dictionary<string, string> { ["chat.flood"] = "Please do not spam the chat." }));

        string text = catalog.Get("chat.flood", "ru");

        Assert.Equal("Please do not spam the chat.", text);
        Assert.Equal([("fallback", "chat.flood", "ru")], log.Events);
    }

    [Fact]
    public void Get_logs_an_error_when_english_is_missing_too()
    {
        var log = new ListLocalizationLog();
        var catalog = LocalizationCatalog.FromTables(
            log,
            ("en", new Dictionary<string, string>()));

        string text = catalog.Get("chat.missing", "ru");

        Assert.Equal("chat.missing", text);
        Assert.Equal(
            [("fallback", "chat.missing", "ru"), ("missing", "chat.missing", "en")],
            log.Events);
    }

    [Fact]
    public void English_miss_logs_only_the_missing_key()
    {
        var log = new ListLocalizationLog();
        var catalog = LocalizationCatalog.Empty(log);

        Assert.Equal("chat.missing", catalog.Get("chat.missing", "en"));
        Assert.Equal([("missing", "chat.missing", "en")], log.Events);
    }

    [Fact]
    public void Unknown_tag_is_english_and_does_not_pretend_to_be_a_fallback()
    {
        Assert.Equal("en", PlayerLocale.Normalize(null));
        Assert.Equal("en", PlayerLocale.Normalize(""));
        Assert.Equal("en", PlayerLocale.Normalize("../ru"));
        Assert.Equal("pt-br", PlayerLocale.Normalize("PT-BR"));
        Assert.False(PlayerLocale.TryNormalize("english", out _));

        var log = new ListLocalizationLog();
        var catalog = LocalizationCatalog.FromTables(
            log,
            ("en", new Dictionary<string, string> { ["chat.flood"] = "Please do not spam the chat." }));

        Assert.Equal("Please do not spam the chat.", catalog.Get("chat.flood", "not a tag"));
        Assert.Empty(log.Events);
    }

    [Fact]
    public void Load_reads_a_locale_folder_and_keeps_english_html_when_the_locale_has_none()
    {
        string root = Path.Combine(Path.GetTempPath(), "fineage-l10n-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "l10n", "ru", "html", "merchant"));
        Directory.CreateDirectory(Path.Combine(root, "l10n", "en", "html", "merchant"));
        Directory.CreateDirectory(Path.Combine(root, "l10n", "ru", "names"));
        Directory.CreateDirectory(Path.Combine(root, "l10n", "not-a-locale"));
        File.WriteAllText(Path.Combine(root, "l10n", "ru", "strings.json"), """{"chat.flood":"Не спамьте."}""");
        File.WriteAllText(Path.Combine(root, "l10n", "ru", "html", "merchant", "30001.htm"), "<html>ru</html>");
        File.WriteAllText(Path.Combine(root, "l10n", "en", "html", "merchant", "30002.htm"), "<html>en-only</html>");
        File.WriteAllText(Path.Combine(root, "l10n", "ru", "names", "items.json"), """{"57":"Адена"}""");
        File.WriteAllText(Path.Combine(root, "l10n", "not-a-locale", "strings.json"), """{"chat.flood":"nope"}""");

        try
        {
            var log = new ListLocalizationLog();
            var catalog = LocalizationCatalog.Load(root, log);

            Assert.Equal("Не спамьте.", catalog.Get("chat.flood", "ru"));
            Assert.Equal("<html>ru</html>", catalog.Get("html/merchant/30001.htm", "ru"));
            Assert.Equal("<html>en-only</html>", catalog.Get("html/merchant/30002.htm", "ru"));
            Assert.Equal("Адена", catalog.Get("name.item.57", "ru"));
            Assert.Equal([("fallback", "html/merchant/30002.htm", "ru")], log.Events);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Resolve_logs_fallback_and_serves_the_supplied_english_text()
    {
        var log = new ListLocalizationLog();
        var catalog = LocalizationCatalog.Empty(log);

        Assert.Equal("Adena", catalog.Resolve("name.item.57", "ru", "Adena"));
        Assert.Equal([("fallback", "name.item.57", "ru")], log.Events);

        log.Events.Clear();
        Assert.Equal("Adena", catalog.Resolve("name.item.57", "en", "Adena"));
        Assert.Empty(log.Events);
    }
}
