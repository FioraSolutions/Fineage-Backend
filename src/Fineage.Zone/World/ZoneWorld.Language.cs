using Fineage.Accounts;
using Fineage.Game.L10n;
using Fineage.Protocol;
using Fineage.Zone.NpcHtml;
using Fineage.Zone.Spawns;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    static LocalizationCatalog LoadLocalization()
    {
        var log = new FineageLocalizationLog();
        string? directory = GameDataPaths.FindDataDirectory(AppContext.BaseDirectory)
            ?? GameDataPaths.FindDataDirectory(Directory.GetCurrentDirectory());
        return directory == null ? LocalizationCatalog.Empty(log) : LocalizationCatalog.Load(directory, log);
    }

    /// <summary>The NPC page for this player's language. Missing pages log through the catalog.</summary>
    public string ReadNpcPage(string? locale, string? npcType, int npcId, int page) =>
        _pages.ReadNpc(locale, npcType, npcId, page);

    internal bool TryPage(string? locale, string relative, out string html)
    {
        string? page = _pages.File(locale, relative);
        if (page == null)
        {
            html = "";
            return false;
        }

        html = page;
        return true;
    }

    /// <summary>Fills <see cref="SystemMessageMsg.Text"/> from <see cref="SystemMessageMsg.Key"/> for this player.</summary>
    public SystemMessageMsg Localize(ZoneSession session, SystemMessageMsg message)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(message);
        if (string.IsNullOrEmpty(message.Key))
            return message;
        message.Text = _l10n.Format(message.Key, session.Language, Args(message.Params));
        return message;
    }

    SystemMessageMsg Localize(ZoneEntity player, SystemMessageMsg message) =>
        Localize(player.Session ?? new ZoneSession(), message);

    static object?[] Args(string[]? parms)
    {
        if (parms == null || parms.Length == 0)
            return [];
        var args = new object?[parms.Length];
        for (int i = 0; i < parms.Length; i++)
            args[i] = parms[i];
        return args;
    }

    /// <summary>
    /// Applies the EnterWorld language tail. A sent tag is stored on the account and copied onto the
    /// session. An omitted tail (older clients) uses the language stored at login, which defaults to English.
    /// </summary>
    public void ApplyPlayerLanguage(ZoneSession session, string? sent)
    {
        ArgumentNullException.ThrowIfNull(session);
        string language;
        if (sent != null)
        {
            language = PlayerLocale.Normalize(sent);
            _accounts?.TrySetLanguage(session.Account, language);
        }
        else
        {
            language = _accounts?.GetLanguage(session.Account) ?? PlayerLocale.Default;
        }

        session.SetLanguage(language);
    }
}
