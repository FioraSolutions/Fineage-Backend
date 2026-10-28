using System.Text.RegularExpressions;

namespace Fineage.Login;

/// <summary>
/// Character-create policy. Defaults match Mobius Interlude: 7 characters, Human fighter and mystic,
/// forbidden-name fragments, a <c>.*</c> name template, and no starting adena.
/// </summary>
public sealed class CharacterCreateOptions
{
    public static IReadOnlyList<string> DefaultForbiddenNames { get; } =
        ["annou", "ammou", "amnou", "anmou", "anou", "amou"];

    public static CharacterCreateOptions Default { get; } = new();

    public bool CreateAllowed { get; init; } = true;

    /// <summary>0 means unlimited. Pending deletes count.</summary>
    public int CharMaxNumber { get; init; } = HumanClasses.MaxCharactersPerAccount;

    public IReadOnlyList<string> ForbiddenNames { get; init; } = DefaultForbiddenNames;

    public Regex NameTemplate { get; init; } = new(".*", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public IReadOnlySet<int> AllowedRaces { get; init; } = new HashSet<int> { HumanClasses.RaceHuman };

    public IReadOnlySet<int> AllowedClasses { get; init; } = new HashSet<int> { HumanClasses.Fighter, HumanClasses.Mystic };

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
            AllowedRaces = ReadIdSet("FINEAGE_ALLOWED_RACES", [HumanClasses.RaceHuman]),
            AllowedClasses = ReadIdSet("FINEAGE_ALLOWED_CLASSES", [HumanClasses.Fighter, HumanClasses.Mystic]),
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

    static IReadOnlySet<int> ReadIdSet(string name, IReadOnlyList<int> fallback)
    {
        string? text = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(text))
            return new HashSet<int>(fallback);
        var ids = new HashSet<int>();
        foreach (string part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(part, out int id))
                ids.Add(id);
        }

        return ids.Count == 0 ? new HashSet<int>(fallback) : ids;
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
