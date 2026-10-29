using System.Text.RegularExpressions;

namespace Fineage.Login;

/// <summary>
/// Character-create policy. Defaults match Mobius Interlude character limits and name rules.
/// A null class or race set means every base class in the datapack (and the races those classes use).
/// That default is the permanent Fineage playtest: all five Interlude races and the nine starter classes.
/// </summary>
public sealed class CharacterCreateOptions
{
    /// <summary>
    /// Human-only values written when create was still the fighter/mystic MVP.
    /// <see cref="FromEnvironment"/> treats these as unset so they cannot hide the other races.
    /// </summary>
    static readonly HashSet<int> LegacyHumanRaces = [HumanClasses.RaceHuman];

    static readonly HashSet<int> LegacyHumanClasses = [HumanClasses.Fighter, HumanClasses.Mystic];

    /// <summary>Interlude create pairs the live lobby must accept. Dwarf has no mystic.</summary>
    public static readonly (int ClassId, int Race)[] PlaytestStarters =
    [
        (HumanClasses.Fighter, HumanClasses.RaceHuman),
        (HumanClasses.Mystic, HumanClasses.RaceHuman),
        (18, 1),
        (25, 1),
        (31, 2),
        (38, 2),
        (44, 3),
        (49, 3),
        (53, 4)
    ];

    public static IReadOnlyList<string> DefaultForbiddenNames { get; } =
        ["annou", "ammou", "amnou", "anmou", "anou", "amou"];

    public static CharacterCreateOptions Default { get; } = new();

    public bool CreateAllowed { get; init; } = true;

    /// <summary>0 means unlimited. Pending deletes count.</summary>
    public int CharMaxNumber { get; init; } = HumanClasses.MaxCharactersPerAccount;

    public IReadOnlyList<string> ForbiddenNames { get; init; } = DefaultForbiddenNames;

    public Regex NameTemplate { get; init; } = new(".*", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    /// <summary>Null allows every race that has a base class.</summary>
    public IReadOnlySet<int>? AllowedRaces { get; init; }

    /// <summary>Null allows every base class. A set may only name base classes.</summary>
    public IReadOnlySet<int>? AllowedClasses { get; init; }

    public long StartingAdena { get; init; }

    public int StartingLevel { get; init; } = 1;

    public int StartingSp { get; init; }

    public static CharacterCreateOptions FromEnvironment()
    {
        return new CharacterCreateOptions
        {
            CreateAllowed = !IsDisabled(Environment.GetEnvironmentVariable("FINEAGE_CHARACTER_CREATE")),
            CharMaxNumber = ReadInt("FINEAGE_CHAR_MAX_NUMBER", HumanClasses.MaxCharactersPerAccount, 0, 100),
            ForbiddenNames = ReadForbidden(Environment.GetEnvironmentVariable("FINEAGE_FORBIDDEN_NAMES")),
            NameTemplate = ReadTemplate(Environment.GetEnvironmentVariable("FINEAGE_CNAME_TEMPLATE")),
            AllowedRaces = ReadIdSet("FINEAGE_ALLOWED_RACES", LegacyHumanRaces),
            AllowedClasses = ReadIdSet("FINEAGE_ALLOWED_CLASSES", LegacyHumanClasses),
            StartingAdena = ReadLong("FINEAGE_STARTING_ADENA", 0),
            StartingLevel = ReadInt("FINEAGE_STARTING_LEVEL", 1, 1, 80),
            StartingSp = ReadInt("FINEAGE_STARTING_SP", 0, 0, int.MaxValue)
        };
    }

    static bool IsDisabled(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        return text.Trim() is "0" or "false" or "off" or "no";
    }

    static IReadOnlyList<string> ReadForbidden(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return DefaultForbiddenNames;
        var parts = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 ? DefaultForbiddenNames : parts;
    }

    static Regex ReadTemplate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Default.NameTemplate;
        try
        {
            return new Regex(text, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        }
        catch (RegexParseException ex)
        {
            throw new InvalidOperationException("FINEAGE_CNAME_TEMPLATE is not a valid regex.", ex);
        }
    }

    /// <summary>
    /// True when the process env still names the Human-only MVP
    /// (<c>FINEAGE_ALLOWED_RACES=0</c> and/or <c>FINEAGE_ALLOWED_CLASSES=0,10</c>).
    /// Those values are ignored; create stays open for every datapack base class.
    /// </summary>
    public static bool EnvironmentUsesLegacyHumanOnly() =>
        IsLegacySet(Environment.GetEnvironmentVariable("FINEAGE_ALLOWED_RACES"), LegacyHumanRaces) ||
        IsLegacySet(Environment.GetEnvironmentVariable("FINEAGE_ALLOWED_CLASSES"), LegacyHumanClasses);

    static bool IsLegacySet(string? text, HashSet<int> legacy)
    {
        var ids = ParseIds(text);
        return ids != null && ids.SetEquals(legacy);
    }

    static HashSet<int>? ParseIds(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var ids = new HashSet<int>();
        foreach (string part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(part, out int id))
                ids.Add(id);
        }

        return ids.Count == 0 ? null : ids;
    }

    static IReadOnlySet<int>? ReadIdSet(string name, HashSet<int> legacyHumanOnly)
    {
        string? text = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var ids = ParseIds(text);
        if (ids == null)
            throw new InvalidOperationException($"{name} did not contain an id.");
        // Stuck Human-only MVP flag: open every base race/class instead of keeping the temporary list.
        if (ids.SetEquals(legacyHumanOnly))
            return null;
        return ids;
    }

    static int ReadInt(string name, int fallback, int min, int max)
    {
        string? text = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(text) || !int.TryParse(text, out int value))
            return fallback;
        return Math.Clamp(value, min, max);
    }

    static long ReadLong(string name, long fallback)
    {
        string? text = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(text) || !long.TryParse(text, out long value) || value < 0)
            return fallback;
        return value;
    }
}
