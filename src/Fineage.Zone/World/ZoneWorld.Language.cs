using Fineage.Game.L10n;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
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
