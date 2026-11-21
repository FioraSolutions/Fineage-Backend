using Fineage.Game.L10n;

namespace Fineage.Zone.NpcHtml;

/// <summary>
/// One HTML read for every NPC, quest, and bypass page. The player's locale is searched first.
/// A miss logs the catalog's fallback warning (key and locale) and serves the English page.
/// A page missing in English too logs the catalog's missing-key error.
/// </summary>
public sealed class LocalizedHtml
{
    readonly LocalizationCatalog _l10n;
    readonly NpcHtmlCatalog _english;

    public LocalizedHtml(LocalizationCatalog l10n, NpcHtmlCatalog english)
    {
        _l10n = l10n ?? throw new ArgumentNullException(nameof(l10n));
        _english = english ?? throw new ArgumentNullException(nameof(english));
    }

    public string ReadNpc(string? locale, string? npcType, int npcId, int page)
    {
        var candidates = NpcHtmlPaths.Candidates(npcType, npcId, page);
        string tag = PlayerLocale.Normalize(locale);
        foreach (string relative in candidates)
        {
            if (TryExact(tag, relative, out string hit))
                return hit;
        }

        string primary = "html/" + candidates[0];
        if (!PlayerLocale.IsDefault(tag))
        {
            foreach (string relative in candidates)
            {
                if (TryExact(PlayerLocale.Default, relative, out string english))
                    return _l10n.Resolve(primary, tag, english);
            }

            _l10n.Get(primary, tag);
            return NpcHtmlText.NothingToSay;
        }

        _l10n.Get(primary, tag);
        return NpcHtmlText.NothingToSay;
    }

    public NpcHtmlPaths.LinkPage ReadLink(string? locale, string? npcType, string command)
    {
        if (!NpcHtmlPaths.TryRelative(command, out string relative))
            return NpcHtmlPaths.LinkPage.Rejected();

        if (relative.StartsWith("teleporter/", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(npcType, "Teleporter", StringComparison.Ordinal))
            return NpcHtmlPaths.LinkPage.Body(NpcHtmlText.NothingToSay);

        string? html = File(locale, relative);
        if (html != null)
            return NpcHtmlPaths.LinkPage.Body(html);

        return NpcHtmlPaths.LinkPage.Body("<html><body>My Text is missing:<br>" + relative + "</body></html>");
    }

    /// <summary>The page at <paramref name="relative"/>, or null when neither locale has it (the miss is logged).</summary>
    public string? File(string? locale, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative))
            return null;

        string key = Key(relative);
        string tag = PlayerLocale.Normalize(locale);
        if (_l10n.TryExact(tag, key, out string hit))
            return hit;
        if (PlayerLocale.IsDefault(tag) && _english.TryGet(relative, out string english) && english.Length > 0)
            return english;

        if (!PlayerLocale.IsDefault(tag))
        {
            if (_l10n.TryExact(PlayerLocale.Default, key, out string fromCatalog))
                return _l10n.Resolve(key, tag, fromCatalog);
            if (_english.TryGet(relative, out string fromFiles) && fromFiles.Length > 0)
                return _l10n.Resolve(key, tag, fromFiles);
        }

        _l10n.Get(key, tag);
        return null;
    }

    /// <summary>
    /// Quest (or any) document. <paramref name="englishBody"/> is the shipped English text when the
    /// catalog has not indexed that file. A non-English miss still logs.
    /// </summary>
    public string Document(string? locale, string? key, string englishBody)
    {
        if (string.IsNullOrEmpty(key))
            return englishBody ?? "";
        string tag = PlayerLocale.Normalize(locale);
        if (_l10n.TryExact(tag, key, out string hit))
            return hit;
        return _l10n.Resolve(key, locale, englishBody);
    }

    public string Name(string? locale, string kind, int id, string english) =>
        id <= 0 ? english ?? "" : _l10n.Resolve($"name.{kind}.{id}", locale, english ?? "");

    bool TryExact(string locale, string relative, out string html)
    {
        if (_l10n.TryExact(locale, Key(relative), out html!))
            return true;
        if (PlayerLocale.IsDefault(locale) && _english.TryGet(relative, out html!) && html.Length > 0)
            return true;
        html = "";
        return false;
    }

    static string Key(string relative) => "html/" + relative.Trim().Replace('\\', '/').TrimStart('/');
}
