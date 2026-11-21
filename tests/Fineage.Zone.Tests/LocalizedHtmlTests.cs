using Fineage.Accounts;
using Fineage.Game.Economy;
using Fineage.Game.L10n;
using Fineage.Zone.NpcHtml;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class LocalizedHtmlTests
{
    [Fact]
    public void Each_player_gets_the_html_for_their_language()
    {
        var log = new ListLocalizationLog();
        var english = NpcHtmlCatalog.FromDocuments(new Dictionary<string, string>
        {
            ["merchant/30001.htm"] = "<html>en</html>",
            ["default/30001.htm"] = "<html>en-default</html>"
        });
        var catalog = LocalizationCatalog.FromTables(
            log,
            ("ru", new Dictionary<string, string> { ["html/merchant/30001.htm"] = "<html>ru</html>" }));
        var pages = new LocalizedHtml(catalog, english);

        Assert.Equal("<html>en</html>", pages.ReadNpc("en", "Merchant", 30001, 0));
        Assert.Equal("<html>ru</html>", pages.ReadNpc("ru", "Merchant", 30001, 0));
        Assert.Equal("<html>en</html>", pages.ReadNpc("de", "Merchant", 30001, 0));
        Assert.Equal([("fallback", "html/merchant/30001.htm", "de")], log.Events);
        Assert.Empty(log.Events.FindAll(e => e.Kind == "missing"));
    }

    [Fact]
    public void The_world_serves_each_session_its_own_page()
    {
        var log = new ListLocalizationLog();
        var english = NpcHtmlCatalog.FromDocuments(new Dictionary<string, string>
        {
            ["merchant/30001.htm"] = "<html>en-merchant</html>"
        });
        var catalog = LocalizationCatalog.FromTables(
            log,
            ("ru", new Dictionary<string, string> { ["html/merchant/30001.htm"] = "<html>ru-merchant</html>" }));
        var world = new ZoneWorld(html: english, localization: catalog, ledgerJournal: new MemoryLedgerJournal());

        var englishSession = new ZoneSession();
        englishSession.SetLanguage("en");
        var russianSession = new ZoneSession();
        russianSession.SetLanguage("ru");

        Assert.Equal("<html>en-merchant</html>", world.ReadNpcPage(englishSession.Language, "Merchant", 30001, 0));
        Assert.Equal("<html>ru-merchant</html>", world.ReadNpcPage(russianSession.Language, "Merchant", 30001, 0));
        Assert.Empty(log.Events);
    }

    [Fact]
    public void A_missing_english_page_is_an_error_naming_the_key()
    {
        var log = new ListLocalizationLog();
        var pages = new LocalizedHtml(LocalizationCatalog.Empty(log), NpcHtmlCatalog.Empty);
        Assert.Equal(NpcHtmlText.NothingToSay, pages.ReadNpc("en", "Guard", 30040, 0));
        Assert.Contains(log.Events, e => e.Kind == "missing" && e.Key == "html/guard/30040.htm" && e.Locale == "en");
    }
}
