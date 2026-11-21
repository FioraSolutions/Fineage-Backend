using System.Text.RegularExpressions;

namespace Fineage.Game.L10n;

/// <summary>
/// Player language tag. The only shipped locale is <see cref="Default"/>.
/// A tag is a lowercase language (<c>en</c>) or language-region (<c>pt-br</c>).
/// Anything else is not a locale: callers store and serve <see cref="Default"/>.
/// </summary>
public static partial class PlayerLocale
{
    public const string Default = "en";

    [GeneratedRegex("^[a-z]{2}(-[a-z0-9]{2,8})?$", RegexOptions.CultureInvariant)]
    private static partial Regex TagPattern();

    /// <summary>
    /// True when <paramref name="raw"/> is a locale tag. Empty and unknown strings are not tags.
    /// </summary>
    public static bool TryNormalize(string? raw, out string locale)
    {
        locale = Default;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        string text = raw.Trim().ToLowerInvariant();
        if (!TagPattern().IsMatch(text))
            return false;

        locale = text;
        return true;
    }

    /// <summary>A locale tag, or <see cref="Default"/> when <paramref name="raw"/> is empty or not a tag.</summary>
    public static string Normalize(string? raw) =>
        TryNormalize(raw, out string locale) ? locale : Default;

    public static bool IsDefault(string? locale) =>
        string.Equals(Normalize(locale), Default, StringComparison.Ordinal);
}
