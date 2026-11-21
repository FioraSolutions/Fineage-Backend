using Fineage.Game.L10n;
using Microsoft.Extensions.Logging;

namespace Fineage.Accounts;

/// <summary>
/// Writes localization misses through <see cref="FineageLogging"/>.
/// Fallback is a warning that names the key and the requested locale.
/// A key missing in English is an error that names the key.
/// </summary>
public sealed class FineageLocalizationLog : ILocalizationLog
{
    public const string Category = "Fineage.L10n";

    public void Fallback(string key, string locale) =>
        FineageLogging.Write(Category, "Warning", "l10n_fallback", FallbackFields(key, locale));

    public void Missing(string key) =>
        FineageLogging.Write(Category, "Error", "l10n_missing", MissingFields(key));

    /// <summary>The warning line <see cref="Fallback"/> writes. Names the key and the requested locale.</summary>
    public static string FormatFallback(string key, string locale) =>
        FineageLogging.FormatKeyValue(LogLevel.Warning, Category, "l10n_fallback", FallbackFields(key, locale));

    /// <summary>The error line <see cref="Missing"/> writes. Names the key.</summary>
    public static string FormatMissing(string key) =>
        FineageLogging.FormatKeyValue(LogLevel.Error, Category, "l10n_missing", MissingFields(key));

    static (string Key, object? Value)[] FallbackFields(string key, string locale) =>
        [("key", key), ("locale", locale), ("fallback", PlayerLocale.Default)];

    static (string Key, object? Value)[] MissingFields(string key) =>
        [("key", key), ("locale", PlayerLocale.Default)];
}
