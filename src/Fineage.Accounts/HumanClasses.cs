namespace Fineage.Accounts;

/// <summary>Interlude race and class ids, plus the CLI names for the two human base classes.</summary>
public static class HumanClasses
{
    public const int RaceHuman = 0;
    public const int SexMale = 0;
    public const int SexFemale = 1;
    public const int Fighter = 0;
    public const int Mystic = 10;
    public const int MaxCharactersPerAccount = 7;

    public static bool TryParseArchetype(string text, out int classId)
    {
        switch ((text ?? "").Trim().ToLowerInvariant())
        {
            case "fighter":
            case "0":
                classId = Fighter;
                return true;
            case "mystic":
            case "mage":
            case "10":
                classId = Mystic;
                return true;
            default:
                classId = -1;
                return false;
        }
    }

    public static string ArchetypeName(int classId) => classId switch
    {
        Fighter => "fighter",
        Mystic => "mystic",
        _ => classId.ToString()
    };
}

public static class NameRules
{
    public static bool IsAccountName(string? name) => IsAlnum(name, min: 3, max: 16);

    public static bool IsCharacterName(string? name) => IsAlnum(name, min: 1, max: 16);

    /// <summary>
    /// Mobius <c>RequestChangePetName</c> name charset: ASCII letters/digits, length 3–16 (no trim —
    /// spaces / punctuation fail like an invalid character).
    /// </summary>
    public static bool IsPetName(string? name)
    {
        if (name == null || name.Length < 3 || name.Length > 16)
            return false;
        foreach (char ch in name)
        {
            if (ch is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9')
                continue;
            return false;
        }

        return true;
    }

    public static bool IsPassword(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < 4 || password.Length > 64)
            return false;
        foreach (char ch in password)
        {
            if (ch < ' ' || ch == '\u007f')
                return false;
        }

        return true;
    }

    static bool IsAlnum(string? name, int min, int max)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;
        name = name.Trim();
        if (name.Length < min || name.Length > max)
            return false;
        foreach (char ch in name)
        {
            if (ch is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9')
                continue;
            return false;
        }

        return true;
    }
}
