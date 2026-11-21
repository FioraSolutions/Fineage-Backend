namespace Fineage.Game.L10n;

/// <summary>
/// The one log sink for a missed translation. Fallback to <see cref="PlayerLocale.Default"/>
/// is a warning that names the key and the requested locale. A key missing in
/// <see cref="PlayerLocale.Default"/> as well is an error that names the key.
/// </summary>
public interface ILocalizationLog
{
    void Fallback(string key, string locale);

    void Missing(string key);
}

/// <summary>Records every fallback and missing-key event for tests.</summary>
public sealed class ListLocalizationLog : ILocalizationLog
{
    public List<(string Kind, string Key, string Locale)> Events { get; } = [];

    public void Fallback(string key, string locale) => Events.Add(("fallback", key, locale));

    public void Missing(string key) => Events.Add(("missing", key, PlayerLocale.Default));
}
