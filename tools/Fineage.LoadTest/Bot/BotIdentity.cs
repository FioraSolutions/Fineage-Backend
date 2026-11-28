namespace Fineage.LoadTest.Bot;

/// <summary>
/// Deterministic account/character identity for one load-test run.
/// <para>
/// The Login server enforces a single global, case-insensitive character-name index, while accounts are namespaced
/// by the scenario's account prefix. Naming characters only by bot id therefore makes two independent runs with
/// different prefixes collide: the second run's fresh accounts have empty rosters but its <c>Bot0000</c> create is
/// rejected with <c>name_taken</c> by the first run's character. Deriving the character name from the same prefix
/// that namespaces the accounts keeps independent runs isolated, and keeps the name stable for reconnects and
/// reruns of the same prefix (an existing roster is reused instead of re-created).
/// </para>
/// </summary>
public static class BotIdentity
{
    /// <summary>The historical scenario default, kept on the legacy <c>Bot0000</c> character names.</summary>
    public const string DefaultAccountPrefix = "loadbot";

    /// <summary>Login's <see cref="Fineage.Accounts.NameRules.IsCharacterName"/> upper bound.</summary>
    public const int MaxCharacterNameLength = 16;

    /// <summary>Login's lower bound for an acceptable character name.</summary>
    public const int MinCharacterNameLength = 3;

    const int BotIdDigits = 4;
    const int NamespaceHashLength = 4;
    const string Base36 = "0123456789abcdefghijklmnopqrstuvwxyz";

    /// <summary>Account for <paramref name="botId"/>: the scenario prefix plus the zero-padded bot id.</summary>
    public static string AccountName(string accountPrefix, int botId)
    {
        if (accountPrefix == null)
            throw new ArgumentNullException(nameof(accountPrefix));
        if (botId < 0)
            throw new ArgumentOutOfRangeException(nameof(botId), botId, "bot id must be non-negative");
        return $"{accountPrefix}{botId:D5}";
    }

    /// <summary>
    /// Character name for <paramref name="botId"/> inside the namespace of <paramref name="accountPrefix"/>.
    /// The default prefix keeps the historical <c>Bot0000</c> names so existing loadbot rosters still match.
    /// Any other prefix contributes a readable prefix part plus a stable hash of the full prefix, so prefixes that
    /// truncate to the same readable part still get distinct names. The result is always 3-16 alphanumerics.
    /// </summary>
    public static string CharacterName(string accountPrefix, int botId)
    {
        if (accountPrefix == null)
            throw new ArgumentNullException(nameof(accountPrefix));
        if (botId < 0)
            throw new ArgumentOutOfRangeException(nameof(botId), botId, "bot id must be non-negative");

        string prefix = accountPrefix.Trim();
        if (prefix.Length == 0)
            throw new InvalidOperationException("load-test account prefix is empty; cannot derive a character name");

        if (string.Equals(prefix, DefaultAccountPrefix, StringComparison.OrdinalIgnoreCase))
            return $"Bot{botId:D4}";

        string id = botId.ToString($"D{BotIdDigits}");
        int readableBudget = MaxCharacterNameLength - NamespaceHashLength - id.Length;
        if (readableBudget < 1)
            throw new InvalidOperationException(
                $"bot id {botId} leaves no room for a prefix-derived character name within {MaxCharacterNameLength} characters");

        string readable = ReadablePrefix(prefix);
        if (readable.Length > readableBudget)
            readable = readable[..readableBudget];

        return $"{readable}{NamespaceHash(prefix)}{id}";
    }

    /// <summary>
    /// Accounts are case-insensitive, so the readable part is lowercased: <c>S1154</c> and <c>s1154</c> name the
    /// same account namespace and must produce the same character name.
    /// </summary>
    static string ReadablePrefix(string prefix)
    {
        var chars = new char[prefix.Length];
        int length = 0;
        foreach (char ch in prefix)
        {
            if (ch is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9')
                chars[length++] = char.ToLowerInvariant(ch);
        }

        // Account names themselves must be alphanumeric, so a prefix with no alphanumerics cannot log in. Keep the
        // identity function total and deterministic instead of throwing here; the login validation reports it.
        return length == 0 ? "b" : new string(chars, 0, length);
    }

    /// <summary>Stable FNV-1a hash of the case-folded prefix, encoded as four base-36 characters.</summary>
    static string NamespaceHash(string prefix)
    {
        uint hash = 2166136261;
        foreach (char ch in prefix)
        {
            hash ^= char.ToLowerInvariant(ch);
            hash *= 16777619;
        }

        var tag = new char[NamespaceHashLength];
        for (int i = NamespaceHashLength - 1; i >= 0; i--)
        {
            tag[i] = Base36[(int)(hash % 36)];
            hash /= 36;
        }

        return new string(tag);
    }
}
